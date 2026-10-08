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

    public List<HarvestingReportCandidateRow> HarvestingCandidates { get; } = [];

    public SeedingReportRow? SeedingReport { get; set; }

    public List<VerdictSummaryRow> Verdicts { get; } = [];

    public List<EvidenceCountRow> EvidenceCounts { get; } = [];

    public int WriteCalls { get; private set; }

    public int ReportReadCalls { get; private set; }

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

    public Task<BatchResultRows> GetResultsByBatchAsync(string batchId, CancellationToken cancellationToken)
    {
        ReportReadCalls++;
        return Task.FromResult(BuildRows(batchId));
    }

    private BatchResultRows BuildRows(string batchId) =>
        new(
            [.. HarvestingCandidates.Where(candidate => candidate.BatchId == batchId)],
            SeedingReport?.BatchId == batchId ? SeedingReport : null);

    public Task<IReadOnlyList<VerdictSummaryRow>> GetVerdictSummariesByBatchAsync(
        string batchId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VerdictSummaryRow>>(Verdicts);

    public Task<IReadOnlyList<EvidenceCountRow>> GetEvidenceCountsByBatchAsync(
        string batchId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EvidenceCountRow>>(EvidenceCounts);

    public Task<IReadOnlyList<ChunkKnowledgeRow>> GetChunkKnowledgeByBatchAsync(
        string batchId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChunkKnowledgeRow>>(
            [.. Chunks.Where(chunk => chunk.BatchId == batchId).Select(ToKnowledgeRow)]);

    private static ChunkKnowledgeRow ToKnowledgeRow(ChunkRow chunk) => new()
    {
        Id = chunk.Id,
        DocumentId = chunk.DocumentId,
        Knowledge = chunk.Knowledge
    };
}
