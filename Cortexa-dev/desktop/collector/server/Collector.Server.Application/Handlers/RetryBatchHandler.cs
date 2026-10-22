using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Application.Handlers;

public sealed class RetryBatchHandler(
    IPipelineRowStore store,
    IngestionEventDispatcher dispatcher,
    ILogger<RetryBatchHandler> logger)
{
    public async Task<RetryBatchOutcome> HandleAsync(
        string batchId,
        UploadCaller caller,
        CancellationToken cancellationToken)
    {
        var saga = await store.GetSagaAsync(batchId, cancellationToken);
        if (saga is null || !SagaOwnership.OwnedBy(saga, caller))
        {
            return RetryBatchOutcome.NotFound();
        }

        var resetSaga = ResetDocumentsToQueued(saga);
        await store.ReplaceSagaAsync(resetSaga, cancellationToken);

        var documents = await LoadDocumentsAsync(resetSaga, cancellationToken);
        await dispatcher.PublishAsync(batchId, documents, cancellationToken);

        logger.LogInformation(
            "Retried batch {BatchId}: {DocumentCount} documents requeued.",
            batchId,
            documents.Count);
        return RetryBatchOutcome.Ok([.. documents.Select(document => document.Id)]);
    }

    private static SagaRow ResetDocumentsToQueued(SagaRow saga) =>
        saga with
        {
            State = RowConstants.SagaStateInProgress,
            Documents =
            [
                .. saga.Documents.Select(document => document with
                {
                    State = RowConstants.SagaDocumentStateQueued
                })
            ]
        };

    private async Task<IReadOnlyList<DocumentRow>> LoadDocumentsAsync(SagaRow saga, CancellationToken cancellationToken)
    {
        var rows = await store.GetDocumentsByBatchAsync(saga.BatchId, cancellationToken);
        var rowsById = rows.ToDictionary(row => row.Id);
        var documents = new List<DocumentRow>(saga.Documents.Count);

        foreach (var document in saga.Documents)
        {
            if (rowsById.TryGetValue(document.DocumentId, out var row))
            {
                documents.Add(row);
                continue;
            }

            logger.LogWarning(
                "Document row {DocumentId} missing for batch {BatchId}, not retried.",
                document.DocumentId,
                saga.BatchId);
        }

        return documents;
    }
}
