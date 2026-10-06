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
}
