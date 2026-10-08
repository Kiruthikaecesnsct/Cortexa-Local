using Collector.Domain.History;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Reads;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;

namespace Collector.Server.Application.Handlers;

public sealed class GetBatchResultsHandler(IPipelineRowStore store)
{
    public async Task<GetBatchResultsOutcome> HandleAsync(
        string batchId,
        UploadCaller caller,
        CancellationToken cancellationToken)
    {
        var saga = await store.GetSagaAsync(batchId, cancellationToken);
        if (saga is null || !OwnedBy(saga, caller))
        {
            return GetBatchResultsOutcome.NotFound();
        }

        var resultsTask = store.GetResultsByBatchAsync(batchId, cancellationToken);
        var verdictsTask = store.GetVerdictSummariesByBatchAsync(batchId, cancellationToken);
        var evidenceTask = store.GetEvidenceCountsByBatchAsync(batchId, cancellationToken);
        var chunksTask = store.GetChunkKnowledgeByBatchAsync(batchId, cancellationToken);
        await Task.WhenAll(resultsTask, verdictsTask, evidenceTask, chunksTask);

        var candidates = CandidateAssembler.Assemble(
            await resultsTask,
            await verdictsTask,
            await evidenceTask,
            await chunksTask);
        return GetBatchResultsOutcome.Ok(new BatchResults { BatchId = batchId, Candidates = candidates });
    }

    private static bool OwnedBy(SagaRow saga, UploadCaller caller) =>
        string.Equals(saga.OwnerUserId, caller.UserId, StringComparison.Ordinal) &&
        string.Equals(saga.OrgId, caller.OrgId, StringComparison.Ordinal);
}
