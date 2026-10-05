using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class CosmosDocumentRepository : IDocumentRepository
{
    private readonly CosmosClient _client;
    private readonly CosmosSettings _settings;

    public CosmosDocumentRepository(CosmosClient client, IOptions<CosmosSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    private Container Container => _client.GetContainer(_settings.Database, _settings.DocumentsContainer);

    public async Task CreateManyAsync(IReadOnlyList<DocumentRecord> records, CancellationToken ct)
    {
        foreach (var record in records)
        {
            var doc = new CosmosDocumentRecord
            {
                Id = record.DocumentId,
                BatchId = record.BatchId,
                Filename = record.Filename,
                BlobUri = record.BlobUri,
                Status = record.Status,
                CreatedAt = record.CreatedAt,
                SavedRepository = CosmosSavedRepository.FromDomain(record.SavedRepository)
            };
            await Container.CreateItemAsync(doc, new PartitionKey(record.BatchId), cancellationToken: ct);
        }
    }

    public async Task<IReadOnlyList<DocumentRecord>> ListByBatchAsync(string batchId, CancellationToken ct)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.batch_id = @batchId")
            .WithParameter("@batchId", batchId);

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(batchId) };
        var iterator = Container.GetItemQueryIterator<CosmosDocumentRecord>(query, requestOptions: options);
        var result = new List<DocumentRecord>();

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            result.AddRange(page.Select(MapToDomain));
        }

        return result;
    }

    public async Task<DocumentRecord?> GetAsync(string batchId, string documentId, CancellationToken ct)
    {
        try
        {
            var response = await Container.ReadItemAsync<CosmosDocumentRecord>(
                documentId,
                new PartitionKey(batchId),
                cancellationToken: ct);

            return MapToDomain(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpdateStatusAsync(string batchId, string documentId, string status, CancellationToken ct)
    {
        var patch = new[] { PatchOperation.Set("/status", status) };
        await Container.PatchItemAsync<CosmosDocumentRecord>(
            documentId,
            new PartitionKey(batchId),
            patch,
            cancellationToken: ct);
    }

    private static DocumentRecord MapToDomain(CosmosDocumentRecord doc) =>
        new(doc.Id, doc.BatchId, doc.Filename, doc.BlobUri)
        {
            Status = doc.Status,
            CreatedAt = doc.CreatedAt,
            SavedRepository = doc.SavedRepository?.ToDomain()
        };
}

internal sealed class CosmosDocumentRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = string.Empty;
    [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;
    [JsonPropertyName("blob_uri")] public string BlobUri { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }

    // Read by the ingestion service to load the document from a saved repository folder.
    [JsonPropertyName("saved_repository")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CosmosSavedRepository? SavedRepository { get; set; }
}

internal sealed class CosmosSavedRepository
{
    [JsonPropertyName("provider")] public string Provider { get; set; } = string.Empty;
    [JsonPropertyName("owner")] public string Owner { get; set; } = string.Empty;
    [JsonPropertyName("repository")] public string Repository { get; set; } = string.Empty;
    [JsonPropertyName("branch")] public string Branch { get; set; } = string.Empty;

    [JsonPropertyName("selected_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? SelectedFiles { get; set; }

    public static CosmosSavedRepository? FromDomain(SavedRepositoryRef? saved) =>
        saved is null
            ? null
            : new CosmosSavedRepository
            {
                Provider = saved.Provider,
                Owner = saved.Owner,
                Repository = saved.Repository,
                Branch = saved.Branch,
                SelectedFiles = saved.SelectedFiles
            };

    public SavedRepositoryRef ToDomain() => new(Provider, Owner, Repository, Branch) { SelectedFiles = SelectedFiles };
}
