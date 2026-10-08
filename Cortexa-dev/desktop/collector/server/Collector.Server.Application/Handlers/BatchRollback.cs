using Collector.Server.Application.Building;
using Collector.Server.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Application.Handlers;

public sealed class BatchRollback(IPipelineRowStore store, ILogger<BatchRollback> logger)
{
    private const string SagaContainer = "batches";
    private const string ProvenanceContainer = "provenance_maps";
    private const string ChunksContainer = "chunks";
    private const string DocumentsContainer = "documents";
    private const int RollbackTimeoutSeconds = 30;

    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(RollbackTimeoutSeconds);

    public async Task RollBackAsync(BatchPlan plan, WriteStage failedStage)
    {
        var batchId = plan.Saga.BatchId;
        using var timeout = new CancellationTokenSource(RollbackTimeout);

        try
        {
            if (await ShouldSkipAsync(batchId, failedStage, timeout.Token))
            {
                return;
            }

            if (!await DeleteSagaIfStartedAsync(batchId, failedStage, timeout.Token))
            {
                return;
            }

            await DeleteRowsAsync(plan, failedStage, timeout.Token);
            logger.LogInformation("Rolled back batch {BatchId} from stage {Stage}.", batchId, failedStage);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Rollback aborted for batch {BatchId} from stage {Stage}.", batchId, failedStage);
        }
    }

    private async Task<bool> ShouldSkipAsync(string batchId, WriteStage failedStage, CancellationToken token)
    {
        if (failedStage >= WriteStage.Saga)
        {
            return false;
        }

        var saga = await store.GetSagaAsync(batchId, token);
        if (saga is null)
        {
            return false;
        }

        logger.LogWarning("Concurrent owner found for batch {BatchId}, skipping rollback.", batchId);
        return true;
    }

    private async Task<bool> DeleteSagaIfStartedAsync(string batchId, WriteStage failedStage, CancellationToken token)
    {
        if (failedStage != WriteStage.Saga)
        {
            return true;
        }

        var failed = await store.DeleteSagaAsync(batchId, token);
        if (failed.Count == 0)
        {
            return true;
        }

        LogFailures(SagaContainer, batchId, failed);
        return false;
    }

    private async Task DeleteRowsAsync(BatchPlan plan, WriteStage failedStage, CancellationToken token)
    {
        var batchId = plan.Saga.BatchId;

        if (failedStage >= WriteStage.Provenance)
        {
            var ids = plan.Provenance.Select(row => row.Id).ToList();
            LogFailures(ProvenanceContainer, batchId, await store.DeleteProvenanceAsync(batchId, ids, token));
        }

        if (failedStage >= WriteStage.Chunks)
        {
            var ids = plan.Chunks.Select(row => row.Id).ToList();
            LogFailures(ChunksContainer, batchId, await store.DeleteChunksAsync(batchId, ids, token));
        }

        var documentIds = plan.Documents.Select(row => row.Id).ToList();
        LogFailures(DocumentsContainer, batchId, await store.DeleteDocumentsAsync(batchId, documentIds, token));
    }

    private void LogFailures(string container, string batchId, IReadOnlyList<string> failedIds)
    {
        foreach (var rowId in failedIds)
        {
            logger.LogError("Rollback failed {Container} {RowId} {BatchId}", container, rowId, batchId);
        }
    }
}
