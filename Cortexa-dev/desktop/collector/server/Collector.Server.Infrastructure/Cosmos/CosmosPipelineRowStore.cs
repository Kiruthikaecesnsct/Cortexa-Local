using System.Net;
using Collector.Server.Application.Errors;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;
using Collector.Server.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure.Cosmos;

public sealed class CosmosPipelineRowStore : IPipelineRowStore
{
    private const string DocumentsStage = "documents";
    private const string ChunksStage = "chunks";
    private const string ProvenanceStage = "provenance_maps";
    private const string SagaStage = "batches";

    private readonly CosmosClient _client;
    private readonly CosmosOptions _options;

    public CosmosPipelineRowStore(CosmosClient client, IOptions<CosmosOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRow> rows, CancellationToken cancellationToken) =>
        UpsertAllAsync(_options.DocumentsContainer, DocumentsStage, rows, cancellationToken);

    public Task UpsertChunksAsync(IReadOnlyList<ChunkRow> rows, CancellationToken cancellationToken) =>
        UpsertAllAsync(_options.ChunksContainer, ChunksStage, rows, cancellationToken);

    public Task UpsertProvenanceAsync(IReadOnlyList<ProvenanceRow> rows, CancellationToken cancellationToken) =>
        UpsertAllAsync(_options.ProvenanceMapsContainer, ProvenanceStage, rows, cancellationToken);

    public async Task CreateSagaAsync(SagaRow saga, CancellationToken cancellationToken)
    {
        var container = _client.GetContainer(_options.Database, _options.BatchesContainer);

        try
        {
            await container.CreateItemAsync(saga, new PartitionKey(saga.BatchId), cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            throw new SagaAlreadyExistsException(saga.BatchId);
        }
        catch (CosmosException exception)
        {
            throw new PipelineWriteException(saga.BatchId, SagaStage, exception);
        }
    }

    public async Task<SagaRow?> GetSagaAsync(string batchId, CancellationToken cancellationToken)
    {
        var container = _client.GetContainer(_options.Database, _options.BatchesContainer);

        try
        {
            var response = await container.ReadItemAsync<SagaRow>(
                batchId,
                new PartitionKey(batchId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException exception)
        {
            throw new PipelineWriteException(batchId, SagaStage, exception);
        }
    }

    private async Task UpsertAllAsync<T>(
        string containerName,
        string stage,
        IReadOnlyList<T> rows,
        CancellationToken cancellationToken)
        where T : IPipelineRow
    {
        if (rows.Count == 0)
        {
            return;
        }

        var container = _client.GetContainer(_options.Database, containerName);
        var parallelism = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxConcurrentWrites,
            CancellationToken = cancellationToken
        };

        try
        {
            await Parallel.ForEachAsync(
                rows,
                parallelism,
                (row, token) => UpsertOneAsync(container, row, token));
        }
        catch (CosmosException exception)
        {
            throw new PipelineWriteException(rows[0].BatchId, stage, exception);
        }
    }

    private static async ValueTask UpsertOneAsync<T>(Container container, T row, CancellationToken cancellationToken)
        where T : IPipelineRow =>
        await container.UpsertItemAsync(row, new PartitionKey(row.BatchId), cancellationToken: cancellationToken);
}
