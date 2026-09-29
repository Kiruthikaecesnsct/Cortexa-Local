using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class CreateBatchFanOutHandler
{
    private readonly ISagaRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly OrchestratorSettings _settings;

    public CreateBatchFanOutHandler(
        ISagaRepository repository,
        IEventPublisher publisher,
        IOptions<OrchestratorSettings> settings)
    {
        _repository = repository;
        _publisher = publisher;
        _settings = settings.Value;
    }

    public async Task<HandlerResult> HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var documentIds = ExtractDocumentIds(envelope);

        var saga = await _repository.GetAsync(envelope.BatchId, ct);
        var isNew = saga is null;

        saga ??= CreateNewSaga(envelope.BatchId);

        var beforeActive = saga.ActiveDocumentIds.Count;
        saga.SeedFanOut(documentIds, _settings.ConcurrencyCap);

        if (saga.ActiveDocumentIds.Count == beforeActive && !isNew)
            return HandlerResult.AlreadyApplied;

        // Publish before persisting: if SB fails mid-loop the saga remains un-persisted,
        // so the idempotency guard won't suppress future retries.
        await PublishIngestionRequestsAsync(envelope, saga, saga.ActiveDocumentIds, ct);

        if (isNew)
            await _repository.CreateAsync(saga, ct);
        else
            await _repository.UpdateAsync(saga, ct);

        return HandlerResult.Applied;
    }

    private static List<string> ExtractDocumentIds(EventEnvelope envelope)
    {
        if (!envelope.Payload.TryGetValue("document_ids", out var raw))
            throw new PermanentProcessingException("batch.created payload missing required 'document_ids' field.");

        if (raw is not JsonElement element)
            throw new PermanentProcessingException("batch.created 'document_ids' field has unexpected type.");

        var ids = element.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();

        if (ids.Count == 0)
            throw new PermanentProcessingException("batch.created 'document_ids' must contain at least one entry.");

        return ids;
    }

    private static BatchSaga CreateNewSaga(string batchId)
    {
        return new BatchSaga(
            batchId,
            BatchState.Queued,
            [],
            wantsHarvesting: false,
            wantsSeeding: false,
            version: 0,
            eTag: null,
            schemaVersion: 1);
    }

    private async Task PublishIngestionRequestsAsync(
        EventEnvelope source,
        BatchSaga saga,
        IEnumerable<string> activeDocIds,
        CancellationToken ct)
    {
        foreach (var docId in activeDocIds)
        {
            var outbound = new EventEnvelope
            {
                EventType = SagaEventType.IngestionRequested,
                BatchId = source.BatchId,
                DocumentId = docId,
                CorrelationId = string.IsNullOrEmpty(source.CorrelationId) ? source.BatchId : source.CorrelationId,
                Payload = []
            };
            AiModelEnvelopeStamper.Stamp(outbound, saga);
            RepoEnvelopeStamper.StampRepoFields(outbound, saga);
            await _publisher.PublishAsync(outbound, ct);
        }
    }
}
