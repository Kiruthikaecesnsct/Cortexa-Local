using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Enums;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class StartBatchHandler
{
    private readonly ISagaRepository _sagas;
    private readonly IDocumentRepository _documents;
    private readonly IEventPublisher _publisher;

    public StartBatchHandler(ISagaRepository sagas, IDocumentRepository documents, IEventPublisher publisher)
    {
        _sagas = sagas;
        _documents = documents;
        _publisher = publisher;
    }

    public async Task<StartBatchResponse> HandleAsync(string batchId, CancellationToken ct)
    {
        var saga = await _sagas.GetAsync(batchId, ct)
            ?? throw new BatchNotFoundException(batchId);

        if (saga.State != BatchState.Queued)
            return new StartBatchResponse(batchId, "started");

        var docRecords = await _documents.ListByBatchAsync(batchId, ct);
        if (docRecords.Count == 0)
            throw new InvalidOperationException($"No documents found for batch '{batchId}'.");

        var documentIds = docRecords.Select(d => d.DocumentId).ToArray();

        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.BatchCreated,
            BatchId = batchId,
            CorrelationId = batchId,
            Payload = new Dictionary<string, object> { ["document_ids"] = documentIds }
        };

        await _publisher.PublishAsync(envelope, ct);

        return new StartBatchResponse(batchId, "started");
    }
}
