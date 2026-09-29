using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Enums;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class RetryDocumentHandler
{
    private readonly ISagaRepository _sagas;
    private readonly IDocumentRepository _documents;
    private readonly IEventPublisher _publisher;

    public RetryDocumentHandler(ISagaRepository sagas, IDocumentRepository documents, IEventPublisher publisher)
    {
        _sagas = sagas;
        _documents = documents;
        _publisher = publisher;
    }

    public async Task HandleAsync(string batchId, string documentId, CancellationToken ct)
    {
        var saga = await _sagas.GetAsync(batchId, ct)
            ?? throw new BatchNotFoundException(batchId);

        var doc = saga.FindDocument(documentId)
            ?? throw new DocumentNotFoundException(documentId);

        if (doc.State != DocumentState.Failed)
            throw new InvalidOperationException($"Document '{documentId}' must be in Failed state to retry.");

        doc.State = DocumentState.Queued;
        doc.FailureReason = null;
        saga.ActiveDocumentIds.Add(documentId);

        if (saga.State == BatchState.Failed)
            saga.State = BatchState.InProgress;

        saga.IncrementVersion();

        await _sagas.UpdateAsync(saga, ct);
        await _documents.UpdateStatusAsync(batchId, documentId, "queued", ct);

        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.IngestionRequested,
            BatchId = batchId,
            DocumentId = documentId,
            CorrelationId = batchId,
            Payload = []
        };

        await _publisher.PublishAsync(envelope, ct);
    }
}
