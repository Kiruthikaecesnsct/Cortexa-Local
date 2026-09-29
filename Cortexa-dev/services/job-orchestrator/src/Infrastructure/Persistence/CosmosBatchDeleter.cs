using System.Net;
using Cortexa.JobOrchestrator.Application.Constants;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Helpers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

/// <summary>
/// Removes a batch's items from the 9 partition-scoped data containers.
/// The saga (batches) container is deleted last by the handler so a
/// crash mid-cascade leaves the saga present and the operation stays resumable.
/// </summary>
public sealed class CosmosBatchDeleter : IBatchDeleter
{
    private const string IdQuery = "SELECT c.id FROM c WHERE c.batch_id = @batchId";

    private readonly CosmosClient _client;
    private readonly string _database;
    private readonly IReadOnlyList<string> _containerNames;
    private readonly IRetryPolicy _retryPolicy;

    public CosmosBatchDeleter(CosmosClient client, IOptions<CosmosSettings> settings, IRetryPolicy retryPolicy)
    {
        var s = settings.Value;
        _client = client;
        _database = s.Database;
        _retryPolicy = retryPolicy;
        _containerNames = BatchScopedContainers.DataContainersExcludingSaga
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string StoreName => "Cosmos";

    public async Task<StoreDeletionResult> DeleteAsync(string batchId, DeleteBatchContext context, CancellationToken ct)
    {
        var total = 0;
        string? error = null;

        foreach (var containerName in _containerNames)
        {
            try
            {
                total += await CosmosRetryHelper.ExecuteWithRetryAsync(
                    () => DeleteFromContainerAsync(containerName, batchId, ct),
                    _retryPolicy,
                    ct: ct);
            }
            catch (CosmosException ex)
            {
                error ??= $"{containerName}: {ex.StatusCode}";
            }
        }

        return new StoreDeletionResult(StoreName, total, error is null, error);
    }

    private async Task<int> DeleteFromContainerAsync(string containerName, string batchId, CancellationToken ct)
    {
        var container = _client.GetContainer(_database, containerName);
        var partitionKey = new PartitionKey(batchId);
        var query = new QueryDefinition(IdQuery).WithParameter("@batchId", batchId);
        var iterator = container.GetItemQueryIterator<IdRecord>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        var count = 0;
        while (iterator.HasMoreResults)
            count += await DeletePageAsync(container, iterator, partitionKey, ct);

        return count;
    }

    private static async Task<int> DeletePageAsync(
        Container container,
        FeedIterator<IdRecord> iterator,
        PartitionKey partitionKey,
        CancellationToken ct)
    {
        var page = await iterator.ReadNextAsync(ct);
        var count = 0;

        foreach (var record in page)
        {
            await DeleteItemIdempotentAsync(container, record.Id, partitionKey, ct);
            count++;
        }

        return count;
    }

    private static async Task DeleteItemIdempotentAsync(
        Container container,
        string id,
        PartitionKey partitionKey,
        CancellationToken ct)
    {
        try
        {
            await container.DeleteItemAsync<object>(id, partitionKey, cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private sealed record IdRecord(string Id);
}
