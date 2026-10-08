using Collector.Domain.Upload;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Application.Handlers;

public sealed class UploadReplayHandler(
    IPipelineRowStore store,
    IngestionEventDispatcher dispatcher,
    ILogger<UploadReplayHandler> logger)
{
    public async Task<UploadOutcome> ReplayAsync(
        SagaRow saga,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!RequestFingerprint.Matches(saga.RequestFingerprint, fingerprint))
        {
            logger.LogWarning("Idempotency key reused with a different request for batch {BatchId}.", saga.BatchId);
            return UploadOutcome.Conflict();
        }

        await RepublishQueuedAsync(saga, cancellationToken);
        return Replayed(saga);
    }

    public Task<UploadOutcome> ReplayWithoutPublishAsync(SagaRow saga) =>
        Task.FromResult(Replayed(saga));

    private UploadOutcome Replayed(SagaRow saga)
    {
        logger.LogInformation(
            "Replayed upload for batch {BatchId}: {DocumentCount} documents.",
            saga.BatchId,
            saga.Documents.Count);
        return UploadOutcome.Replayed(ToResult(saga));
    }

    private async Task RepublishQueuedAsync(SagaRow saga, CancellationToken cancellationToken)
    {
        var queuedIds = saga.Documents
            .Where(document => document.State == RowConstants.SagaDocumentStateQueued)
            .Select(document => document.DocumentId)
            .ToList();
        if (queuedIds.Count == 0)
        {
            return;
        }

        var rows = await store.GetDocumentsByBatchAsync(saga.BatchId, cancellationToken);
        var rowsById = rows.ToDictionary(row => row.Id);
        var documents = new List<DocumentRow>(queuedIds.Count);

        foreach (var id in queuedIds)
        {
            if (rowsById.TryGetValue(id, out var row))
            {
                documents.Add(row);
                continue;
            }

            logger.LogWarning("Document row {DocumentId} missing for batch {BatchId}, not republished.", id, saga.BatchId);
        }

        await dispatcher.PublishAsync(saga.BatchId, documents, cancellationToken);
    }

    private static KnowledgeUploadResult ToResult(SagaRow saga) =>
        new()
        {
            BatchId = saga.BatchId,
            DocumentIds = [.. saga.Documents.Select(document => document.DocumentId)]
        };
}
