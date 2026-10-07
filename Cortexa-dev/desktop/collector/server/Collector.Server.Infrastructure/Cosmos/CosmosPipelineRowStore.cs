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
    private const string ResultsStage = "results";
    private const string HarvestingEngine = "harvesting";
    private const string SeedingEngine = "seeding";
    private const string ReportCandidateDocType = "report_candidate";
    private const string BatchIdParameter = "@batchId";
    private const string OwnerUserIdParameter = "@ownerUserId";
    private const string OrgIdParameter = "@orgId";
    private const string EngineParameter = "@engine";
    private const string DocTypeParameter = "@docType";

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

    public async Task<IReadOnlyList<SagaRow>> ListSagasByOwnerAsync(
        string ownerUserId,
        string orgId,
        CancellationToken cancellationToken)
    {
        var container = _client.GetContainer(_options.Database, _options.BatchesContainer);
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.owner_user_id = @ownerUserId AND c.org_id = @orgId")
            .WithParameter(OwnerUserIdParameter, ownerUserId)
            .WithParameter(OrgIdParameter, orgId);

        try
        {
            return await ReadAllAsync<SagaRow>(container, query, cancellationToken: cancellationToken);
        }
        catch (CosmosException exception)
        {
            throw new PipelineWriteException(ownerUserId, SagaStage, exception);
        }
    }

    public async Task<IReadOnlyList<ChunkRow>> GetChunksByBatchAsync(string batchId, CancellationToken cancellationToken)
    {
        var container = _client.GetContainer(_options.Database, _options.ChunksContainer);

        try
        {
            return await ReadByBatchAsync<ChunkRow>(container, batchId, cancellationToken);
        }
        catch (CosmosException exception)
        {
            throw new PipelineWriteException(batchId, ChunksStage, exception);
        }
    }

    public async Task<IReadOnlyList<BatchResultJoinRow>> GetResultsByBatchAsync(string batchId, CancellationToken cancellationToken)
    {
        try
        {
            var container = _client.GetContainer(_options.Database, _options.ReportsContainer);
            var candidates = await ReadHarvestingCandidatesAsync(container, batchId, cancellationToken);
            var seedingReport = await ReadSeedingReportAsync(container, batchId, cancellationToken);

            var rows = new List<BatchResultJoinRow>();
            AppendHarvestingRows(rows, candidates);
            AppendSeedingRows(rows, seedingReport);
            return rows;
        }
        catch (CosmosException exception)
        {
            throw new PipelineWriteException(batchId, ResultsStage, exception);
        }
    }

    private static Task<IReadOnlyList<HarvestingReportCandidateRow>> ReadHarvestingCandidatesAsync(
        Container container,
        string batchId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.batch_id = @batchId AND c.engine = @engine AND c.doc_type = @docType")
            .WithParameter(BatchIdParameter, batchId)
            .WithParameter(EngineParameter, HarvestingEngine)
            .WithParameter(DocTypeParameter, ReportCandidateDocType);
        var requestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(batchId) };
        return ReadAllAsync<HarvestingReportCandidateRow>(container, query, requestOptions, cancellationToken);
    }

    private static async Task<SeedingReportRow?> ReadSeedingReportAsync(
        Container container,
        string batchId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.batch_id = @batchId AND c.engine = @engine")
            .WithParameter(BatchIdParameter, batchId)
            .WithParameter(EngineParameter, SeedingEngine);
        var requestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(batchId) };
        var reports = await ReadAllAsync<SeedingReportRow>(container, query, requestOptions, cancellationToken);
        return reports.Count > 0 ? reports[0] : null;
    }

    private static void AppendHarvestingRows(
        List<BatchResultJoinRow> rows,
        IReadOnlyList<HarvestingReportCandidateRow> candidates)
    {
        foreach (var candidate in candidates)
        {
            foreach (var link in candidate.ProvenanceLinks)
            {
                rows.Add(new BatchResultJoinRow
                {
                    Engine = HarvestingEngine,
                    ChunkId = link.ChunkId,
                    DocumentId = link.DocumentId,
                    SourceChunkIndex = link.SourceChunkIndex
                });
            }
        }
    }

    private static void AppendSeedingRows(List<BatchResultJoinRow> rows, SeedingReportRow? report)
    {
        if (report is null)
        {
            return;
        }

        foreach (var opportunity in report.Opportunities)
        {
            var chunkIds = opportunity.GroundedIn?.ChunkIds ?? [];
            foreach (var chunkId in chunkIds)
            {
                rows.Add(new BatchResultJoinRow { Engine = SeedingEngine, ChunkId = chunkId });
            }
        }
    }

    private static Task<IReadOnlyList<T>> ReadByBatchAsync<T>(
        Container container,
        string batchId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.batch_id = @batchId")
            .WithParameter(BatchIdParameter, batchId);
        var requestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(batchId) };
        return ReadAllAsync<T>(container, query, requestOptions, cancellationToken);
    }

    private static async Task<IReadOnlyList<T>> ReadAllAsync<T>(
        Container container,
        QueryDefinition query,
        QueryRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: requestOptions);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        return results;
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
