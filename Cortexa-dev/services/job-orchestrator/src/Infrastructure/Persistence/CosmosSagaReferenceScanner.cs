using System.Net;
using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class CosmosSagaReferenceScanner : ISagaReferenceScanner
{
    private const string SagaProjectionQuery = "SELECT c.id, c.documents FROM c";

    private readonly Container _batchesContainer;
    private readonly Container _documentsContainer;
    private readonly ILogger<CosmosSagaReferenceScanner> _logger;

    public CosmosSagaReferenceScanner(
        CosmosClient client,
        IOptions<CosmosSettings> settings,
        ILogger<CosmosSagaReferenceScanner> logger)
    {
        var s = settings.Value;
        _batchesContainer = client.GetContainer(s.Database, s.BatchesContainer);
        _documentsContainer = client.GetContainer(s.Database, s.DocumentsContainer);
        _logger = logger;
    }

    public async Task<IReadOnlyList<DanglingReference>> FindDanglingAsync(CancellationToken ct)
    {
        var dangling = new List<DanglingReference>();
        var iterator = _batchesContainer.GetItemQueryIterator<SagaProjection>(
            new QueryDefinition(SagaProjectionQuery));

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            foreach (var saga in page)
                await CheckSagaDocumentsAsync(saga, dangling, ct);
        }

        return dangling;
    }

    private async Task CheckSagaDocumentsAsync(
        SagaProjection saga,
        List<DanglingReference> dangling,
        CancellationToken ct)
    {
        foreach (var doc in saga.Documents)
        {
            if (string.IsNullOrWhiteSpace(doc.DocumentId))
                continue;

            try
            {
                await _documentsContainer.ReadItemAsync<object>(
                    doc.DocumentId,
                    new PartitionKey(saga.Id),
                    cancellationToken: ct);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                dangling.Add(new DanglingReference(saga.Id, "document_id", doc.DocumentId));
            }
            catch (CosmosException ex)
            {
                _logger.LogWarning(
                    "Saga reference check failed. batch_id={BatchId} document_id={DocumentId} status={Status}",
                    saga.Id,
                    doc.DocumentId,
                    ex.StatusCode);
            }
        }
    }

    private sealed class SagaProjection
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("documents")]
        public List<DocumentRef> Documents { get; set; } = [];
    }

    private sealed class DocumentRef
    {
        [JsonPropertyName("document_id")]
        public string DocumentId { get; set; } = string.Empty;
    }
}
