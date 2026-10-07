using Collector.Server.Application.Errors;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;

namespace Collector.Server.Tests.Fakes;

internal sealed class FakePipelineRowStore : IPipelineRowStore
{
    public List<DocumentRow> Documents { get; } = [];

    public List<ChunkRow> Chunks { get; } = [];

    public List<ProvenanceRow> Provenance { get; } = [];

    public Dictionary<string, SagaRow> Sagas { get; } = [];

    public Dictionary<string, List<BatchResultJoinRow>> Results { get; } = [];

    public int WriteCalls { get; private set; }

    public SagaRow? RaceWinner { get; set; }

    public bool ConflictWithoutSaga { get; set; }

    public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRow> rows, CancellationToken cancellationToken)
    {
        WriteCalls++;
        Documents.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task UpsertChunksAsync(IReadOnlyList<ChunkRow> rows, CancellationToken cancellationToken)
    {
        WriteCalls++;
        Chunks.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task UpsertProvenanceAsync(IReadOnlyList<ProvenanceRow> rows, CancellationToken cancellationToken)
    {
        WriteCalls++;
        Provenance.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task CreateSagaAsync(SagaRow saga, CancellationToken cancellationToken)
    {
        WriteCalls++;
        if (RaceWinner is not null)
        {
            Sagas[RaceWinner.BatchId] = RaceWinner;
        }

        if (ConflictWithoutSaga || Sagas.ContainsKey(saga.BatchId))
        {
            return Task.FromException(new SagaAlreadyExistsException(saga.BatchId));
        }

        Sagas[saga.BatchId] = saga;
        return Task.CompletedTask;
    }

    public Task<SagaRow?> GetSagaAsync(string batchId, CancellationToken cancellationToken) =>
        Task.FromResult(Sagas.GetValueOrDefault(batchId));

    public Task<IReadOnlyList<SagaRow>> ListSagasByOwnerAsync(
        string ownerUserId,
        string orgId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SagaRow>>(
            [.. Sagas.Values.Where(saga => saga.OwnerUserId == ownerUserId && saga.OrgId == orgId)]);

    public Task<IReadOnlyList<ChunkRow>> GetChunksByBatchAsync(string batchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChunkRow>>([.. Chunks.Where(chunk => chunk.BatchId == batchId)]);

    public Task<IReadOnlyList<BatchResultJoinRow>> GetResultsByBatchAsync(string batchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BatchResultJoinRow>>(
            Results.TryGetValue(batchId, out var rows) ? rows : []);

    public void AddResult(string batchId, BatchResultJoinRow row)
    {
        if (!Results.TryGetValue(batchId, out var rows))
        {
            rows = [];
            Results[batchId] = rows;
        }

        rows.Add(row);
    }
}
