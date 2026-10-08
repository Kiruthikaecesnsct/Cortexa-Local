using Collector.Server.Application.Errors;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;

namespace Collector.Server.Tests.Fakes;

internal sealed class FakePipelineRowStore : IPipelineRowStore
{
    public const string DocumentsStage = "documents";
    public const string ChunksStage = "chunks";
    public const string ProvenanceStage = "provenance";
    public const string SagaStage = "saga";

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

    public string? FailAtStage { get; set; }

    public HashSet<string> FailDeleteIds { get; } = [];

    public List<string> DeleteCalls { get; } = [];

    public Dictionary<string, List<string>> DeletedIds { get; } = [];

    public bool AnyDeleteSawCancelledToken { get; private set; }

    public int RowCount => Documents.Count + Chunks.Count + Provenance.Count + Sagas.Count;

    public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRow> rows, CancellationToken cancellationToken) =>
        WriteAsync(DocumentsStage, rows, Documents, cancellationToken);

    public Task UpsertChunksAsync(IReadOnlyList<ChunkRow> rows, CancellationToken cancellationToken) =>
        WriteAsync(ChunksStage, rows, Chunks, cancellationToken);

    public Task UpsertProvenanceAsync(IReadOnlyList<ProvenanceRow> rows, CancellationToken cancellationToken) =>
        WriteAsync(ProvenanceStage, rows, Provenance, cancellationToken);

    public Task CreateSagaAsync(SagaRow saga, CancellationToken cancellationToken)
    {
        WriteCalls++;
        if (FailAtStage == SagaStage)
        {
            return Task.FromException(new PipelineWriteException(saga.BatchId, SagaStage, new InvalidOperationException("forced")));
        }

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

    public Task<IReadOnlyList<string>> DeleteSagaAsync(string batchId, CancellationToken cancellationToken)
    {
        RecordDelete(SagaStage, [batchId], cancellationToken);
        var failed = FailedAmong([batchId]);
        if (failed.Count == 0)
        {
            Sagas.Remove(batchId);
        }

        return Task.FromResult(failed);
    }

    public Task<IReadOnlyList<string>> DeleteProvenanceAsync(
        string batchId,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken) =>
        DeleteRows(ProvenanceStage, ids, Provenance, row => row.Id, cancellationToken);

    public Task<IReadOnlyList<string>> DeleteChunksAsync(
        string batchId,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken) =>
        DeleteRows(ChunksStage, ids, Chunks, row => row.Id, cancellationToken);

    public Task<IReadOnlyList<string>> DeleteDocumentsAsync(
        string batchId,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken) =>
        DeleteRows(DocumentsStage, ids, Documents, row => row.Id, cancellationToken);

    public Task<IReadOnlyList<DocumentRow>> GetDocumentsByBatchAsync(string batchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentRow>>([.. Documents.Where(document => document.BatchId == batchId)]);

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

    private Task WriteAsync<T>(string stage, IReadOnlyList<T> rows, List<T> target, CancellationToken cancellationToken)
    {
        WriteCalls++;
        if (FailAtStage == stage)
        {
            target.AddRange(rows.Take(1));
            return Task.FromException(new PipelineWriteException("batch", stage, new InvalidOperationException("forced")));
        }

        target.AddRange(rows);
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

    private Task<IReadOnlyList<string>> DeleteRows<T>(
        string stage,
        IReadOnlyList<string> ids,
        List<T> target,
        Func<T, string> idOf,
        CancellationToken cancellationToken)
    {
        RecordDelete(stage, ids, cancellationToken);
        var failed = FailedAmong(ids);
        var deletable = ids.Except(failed).ToHashSet();
        target.RemoveAll(row => deletable.Contains(idOf(row)));
        return Task.FromResult(failed);
    }

    private void RecordDelete(string stage, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        DeleteCalls.Add(stage);
        DeletedIds[stage] = [.. ids];
        AnyDeleteSawCancelledToken |= cancellationToken.IsCancellationRequested;
    }

    private IReadOnlyList<string> FailedAmong(IReadOnlyList<string> ids) => [.. ids.Where(FailDeleteIds.Contains)];
}
