using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.StateMachine;
using Microsoft.Extensions.Logging;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public enum HandlerResult
{
    Applied,
    AlreadyApplied
}

public sealed class AdvanceSagaHandler
{
    private const int DefaultMaxChunksPerExtractionUnit = 25;

    private readonly ISagaRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly ILogger<AdvanceSagaHandler> _logger;
    private readonly int _maxChunksPerExtractionUnit;
    private readonly IReadOnlyDictionary<string, TransitionResolver> _eventDispatchTable;

    public AdvanceSagaHandler(
        ISagaRepository repository,
        IEventPublisher publisher,
        ILogger<AdvanceSagaHandler> logger,
        int maxChunksPerExtractionUnit = DefaultMaxChunksPerExtractionUnit)
    {
        _repository = repository;
        _publisher = publisher;
        _logger = logger;
        _maxChunksPerExtractionUnit = maxChunksPerExtractionUnit > 0
            ? maxChunksPerExtractionUnit
            : DefaultMaxChunksPerExtractionUnit;
        _eventDispatchTable = BuildEventDispatchTable();
    }

    private Dictionary<string, TransitionResolver> BuildEventDispatchTable()
    {
        return new Dictionary<string, TransitionResolver>(StringComparer.OrdinalIgnoreCase)
        {
            [SagaEventType.IngestionCompleted] = (e, s, d) => ResolveIngestionCompleted(e, s, d),
            [SagaEventType.ExtractionCompleted] = (e, _, d) => ResolveExtractionCompleted(e, d),
            [SagaEventType.EvidenceCompleted] = (e, s, d) => ResolveEvidenceCompleted(e, s, d),
            [SagaEventType.ScoringCompleted] = (e, s, d) => ResolveScoringCompleted(e, s, d),
            [SagaEventType.EngineCompleted] = (e, s, d) => ResolveEngineCompleted(e, s, d),
            [SagaEventType.ExtractionFailed] = (e, _, d) => ResolveExtractionFailed(e, d),
            [SagaEventType.EvidenceFailed] = (e, s, d) => ResolveEvidenceFailed(e, s, d),
            [SagaEventType.ScoringFailed] = (e, s, d) => ResolveScoringFailed(e, s, d),
            [SagaEventType.HarvestingFailed] = (e, _, d) => ResolveHarvestingFailed(e, d),
            [SagaEventType.SeedingFailed] = (e, _, d) => ResolveSeedingFailed(e, d)
        };
    }

    public async Task<HandlerResult> HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["batch_id"] = envelope.BatchId,
            ["document_id"] = envelope.DocumentId ?? string.Empty,
            ["correlation_id"] = ResolveCorrelationId(envelope)
        });

        LogEventReceived(envelope);

        var (saga, document) = await LoadSagaAndDocumentAsync(envelope, ct);

        if (saga.State is BatchState.Completed or BatchState.Failed or BatchState.Cancelled)
        {
            LogAlreadyAppliedTerminalSagaState(envelope, saga);
            return HandlerResult.AlreadyApplied;
        }

        if (envelope.EventType.Equals(SagaEventType.AssetEmbeddingCompleted, StringComparison.OrdinalIgnoreCase))
            return await RecordAssetEmbeddingCompletionAsync(envelope, saga, ct);

        if (envelope.EventType.Equals(SagaEventType.DigestCompleted, StringComparison.OrdinalIgnoreCase))
            return await RecordDigestCompletionAsync(envelope, saga, ct);

        if (envelope.EventType.Equals(SagaEventType.LandscapeCompleted, StringComparison.OrdinalIgnoreCase))
            return await RecordLandscapeCompletionAsync(envelope, saga, ct);

        if (envelope.EventType.Equals(SagaEventType.IdeationCompleted, StringComparison.OrdinalIgnoreCase))
            return await RecordIdeationCompletionAsync(envelope, saga, document, ct);

        var (targetState, envelopesToPublish, forceProcessing) = ResolveTransition(envelope, saga, document);

        if (!NeedsProcessing(document, targetState, envelopesToPublish, forceProcessing))
        {
            LogAlreadyAppliedNoProcessingNeeded(envelope, document);
            return HandlerResult.AlreadyApplied;
        }

        var previousState = document.State;

        if (targetState.HasValue && targetState.Value != document.State)
        {
            DocumentTransitions.Advance(document, targetState.Value);
            LogStateTransitionApplied(previousState, document.State);
        }

        var releasedDocId = HandleTerminalIfNeeded(saga, document);

        saga.State = BatchTransitions.DeriveFromDocuments(saga.Documents, saga.State);
        saga.IncrementVersion();

        await _repository.UpdateAsync(saga, ct);

        AiModelEnvelopeStamper.StampAll(envelopesToPublish, saga);

        foreach (var outbound in envelopesToPublish)
        {
            await _publisher.PublishAsync(outbound, ct);
            LogEnvelopePublished(outbound);
        }

        await ReleaseNextIfAvailableAsync(envelope, saga, releasedDocId, ct);

        return HandlerResult.Applied;
    }

    private void LogEventReceived(EventEnvelope envelope)
    {
        _logger.LogInformation("Event received: {EventType}", envelope.EventType);
    }

    private void LogAlreadyAppliedTerminalSagaState(EventEnvelope envelope, BatchSaga saga)
    {
        _logger.LogInformation(
            "Event {EventType} already applied: saga is in terminal state {SagaState}",
            envelope.EventType,
            saga.State);
    }

    private void LogAlreadyAppliedNoProcessingNeeded(EventEnvelope envelope, DocumentProgress document)
    {
        _logger.LogInformation(
            "Event {EventType} already applied: no processing needed for document state {DocumentState}",
            envelope.EventType,
            document.State);
    }

    private void LogStateTransitionApplied(DocumentState previousState, DocumentState newState)
    {
        _logger.LogInformation(
            "State transition applied: {PreviousState} -> {NewState}",
            previousState,
            newState);
    }

    private void LogEnvelopePublished(EventEnvelope outbound)
    {
        _logger.LogInformation("Envelope published: {EventType}", outbound.EventType);
    }

    private async Task<(BatchSaga saga, DocumentProgress document)> LoadSagaAndDocumentAsync(
        EventEnvelope envelope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envelope.DocumentId))
            throw new PermanentProcessingException("Message missing required document_id field.");

        var saga = await _repository.GetAsync(envelope.BatchId, ct)
            ?? throw new PermanentProcessingException($"Saga not found for batch '{envelope.BatchId}'.");

        var document = saga.FindDocument(envelope.DocumentId);
        if (document is null)
        {
            document = new DocumentProgress(envelope.DocumentId, DocumentState.Queued);
            saga.AddDocument(document);
        }
        return (saga, document);
    }

    private static bool NeedsProcessing(
        DocumentProgress document,
        DocumentState? targetState,
        IReadOnlyList<EventEnvelope> envelopesToPublish,
        bool forceProcessing)
    {
        if (forceProcessing)
            return true;

        if (targetState is null && envelopesToPublish.Count == 0)
            return false;

        return !IsAlreadyAtOrPast(document.State, targetState);
    }

    private static string? HandleTerminalIfNeeded(BatchSaga saga, DocumentProgress document)
    {
        if (!document.IsTerminal)
            return null;

        saga.MarkActiveTerminal(document.DocumentId);
        return saga.TryReleaseNext();
    }

    private async Task ReleaseNextIfAvailableAsync(
        EventEnvelope source, BatchSaga saga, string? releasedDocId, CancellationToken ct)
    {
        if (releasedDocId is null)
            return;

        var outbound = BuildEnvelope(
            SagaEventType.IngestionRequested,
            source.BatchId,
            releasedDocId,
            ResolveCorrelationId(source),
            new Dictionary<string, object>());

        AiModelEnvelopeStamper.Stamp(outbound, saga);

        await _publisher.PublishAsync(outbound, ct);
    }

    private delegate (DocumentState?, IReadOnlyList<EventEnvelope>, bool) TransitionResolver(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document);

    private (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveTransition(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document)
    {
        if (_eventDispatchTable.TryGetValue(envelope.EventType, out var resolver))
            return resolver(envelope, saga, document);

        return ResolveDefaultTransition(envelope);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveDefaultTransition(
        EventEnvelope envelope)
    {
        var route = SagaEventRouter.Resolve(envelope.EventType);

        if (route is null)
            return (null, [], false);

        var envelopes = route.NextEvent is not null
            ? BuildSimpleTransition(envelope, route.NextEvent)
            : Array.Empty<EventEnvelope>();

        return (route.TargetState, envelopes, false);
    }

    private (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveIngestionCompleted(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document)
    {
        var chunkCount = ExtractPayloadIntOrDefault(envelope.Payload, PayloadKeys.ChunkCount, 1);
        var unitCount = ComputeExtractionUnitCount(chunkCount);

        document.SetExpectedExtractionUnits(unitCount);

        var envelopes = new List<EventEnvelope>(BuildExtractionRequests(envelope, chunkCount, unitCount));

        if (saga.WantsSeeding && saga.IsDeepSeeding)
        {
            envelopes.AddRange(BuildAssetEmbeddingRequests(envelope, chunkCount, unitCount));
            saga.AddExpectedAssetEmbeddingUnits(unitCount);
        }

        return (DocumentState.Ingested, envelopes, false);
    }

    private IReadOnlyList<EventEnvelope> BuildAssetEmbeddingRequests(EventEnvelope source, int chunkCount, int unitCount)
    {
        var envelopes = new List<EventEnvelope>(unitCount);

        for (var unitIndex = 0; unitIndex < unitCount; unitIndex++)
        {
            var chunkStart = unitIndex * _maxChunksPerExtractionUnit;
            var chunkEnd = Math.Min(chunkStart + _maxChunksPerExtractionUnit, chunkCount);

            envelopes.Add(BuildAssetEmbeddingRequestEnvelope(source, chunkStart, chunkEnd, unitIndex, unitCount));
        }

        return envelopes;
    }

    private static EventEnvelope BuildAssetEmbeddingRequestEnvelope(
        EventEnvelope source, int chunkStart, int chunkEnd, int unitIndex, int unitCount)
    {
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.DocumentId] = source.DocumentId!,
            [PayloadKeys.ChunkStart] = chunkStart,
            [PayloadKeys.ChunkEnd] = chunkEnd,
            [PayloadKeys.UnitIndex] = unitIndex,
            [PayloadKeys.UnitCount] = unitCount
        };

        return BuildEnvelope(
            SagaEventType.AssetEmbeddingRequested,
            source.BatchId,
            source.DocumentId!,
            ResolveCorrelationId(source),
            payload);
    }

    private async Task<HandlerResult> RecordAssetEmbeddingCompletionAsync(
        EventEnvelope envelope, BatchSaga saga, CancellationToken ct)
    {
        var unitIndex = ExtractPayloadIntOrDefault(envelope.Payload, PayloadKeys.UnitIndex, 0);
        var unitKey = $"{envelope.DocumentId}:{unitIndex}";

        if (!saga.TryRecordAssetEmbeddingUnit(unitKey))
            return HandlerResult.AlreadyApplied;

        var digestRequest = TryBuildDigestRequest(envelope, saga);

        if (digestRequest is not null)
        {
            AiModelEnvelopeStamper.Stamp(digestRequest, saga);
            await _publisher.PublishAsync(digestRequest, ct);
            LogEnvelopePublished(digestRequest);
        }

        saga.IncrementVersion();
        await _repository.UpdateAsync(saga, ct);

        LogAssetEmbeddingUnitRecorded(saga);
        return HandlerResult.Applied;
    }

    private static EventEnvelope? TryBuildDigestRequest(EventEnvelope source, BatchSaga saga)
    {
        var unitCount = ExtractPayloadIntOrDefault(source.Payload, PayloadKeys.UnitCount, 1);

        if (!saga.AllAssetEmbeddingUnitsRecordedFor(source.DocumentId!, unitCount))
            return null;

        if (!saga.TryRecordDigestRequested(source.DocumentId!))
            return null;

        return BuildDigestRequestEnvelope(source);
    }

    private static EventEnvelope BuildDigestRequestEnvelope(EventEnvelope source)
    {
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.DocumentId] = source.DocumentId!,
            [PayloadKeys.UnitIndex] = 0,
            [PayloadKeys.UnitCount] = 1
        };

        return BuildEnvelope(
            SagaEventType.DigestRequested,
            source.BatchId,
            source.DocumentId!,
            ResolveCorrelationId(source),
            payload);
    }

    private async Task<HandlerResult> RecordDigestCompletionAsync(
        EventEnvelope envelope, BatchSaga saga, CancellationToken ct)
    {
        if (!saga.TryRecordDigestCompleted(envelope.DocumentId!))
            return HandlerResult.AlreadyApplied;

        var landscapeRequest = TryBuildLandscapeRequest(envelope, saga);

        if (landscapeRequest is not null)
        {
            AiModelEnvelopeStamper.Stamp(landscapeRequest, saga);
            await _publisher.PublishAsync(landscapeRequest, ct);
            LogEnvelopePublished(landscapeRequest);
        }

        saga.IncrementVersion();
        await _repository.UpdateAsync(saga, ct);

        LogDigestCompletionRecorded(envelope);
        return HandlerResult.Applied;
    }

    private static EventEnvelope? TryBuildLandscapeRequest(EventEnvelope source, BatchSaga saga)
    {
        if (!saga.TryRecordLandscapeRequested(source.DocumentId!))
            return null;

        return BuildLandscapeRequestEnvelope(source);
    }

    private static EventEnvelope BuildLandscapeRequestEnvelope(EventEnvelope source)
    {
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.DocumentId] = source.DocumentId!
        };

        var briefId = ExtractPayloadStringOrDefault(source.Payload, PayloadKeys.BriefId, string.Empty);
        if (!string.IsNullOrWhiteSpace(briefId))
            payload[PayloadKeys.BriefId] = briefId;

        return BuildEnvelope(
            SagaEventType.LandscapeRequested,
            source.BatchId,
            source.DocumentId!,
            ResolveCorrelationId(source),
            payload);
    }

    private async Task<HandlerResult> RecordLandscapeCompletionAsync(
        EventEnvelope envelope, BatchSaga saga, CancellationToken ct)
    {
        if (!saga.TryRecordLandscapeCompleted(envelope.DocumentId!))
            return HandlerResult.AlreadyApplied;

        saga.IncrementVersion();
        await _repository.UpdateAsync(saga, ct);

        LogLandscapeCompletionRecorded(envelope);
        return HandlerResult.Applied;
    }

    private async Task<HandlerResult> RecordIdeationCompletionAsync(
        EventEnvelope envelope, BatchSaga saga, DocumentProgress document, CancellationToken ct)
    {
        if (document.IsTerminal)
            return HandlerResult.AlreadyApplied;

        var candidateIds = ExtractSeededCandidateIds(envelope.Payload);

        if (candidateIds.Count == 0)
            return await FailSeededValidationAsync(
                envelope, saga, document, "Ideation produced no seeded candidates to validate.", ct);

        if (!document.TryRecordSeededCandidates(candidateIds))
            return HandlerResult.AlreadyApplied;

        var jobId = ExtractJobId(envelope.Payload, envelope.BatchId);
        var evidenceEnvelopes = BuildEvidenceRequests(envelope, candidateIds, jobId);

        AiModelEnvelopeStamper.StampAll(evidenceEnvelopes, saga);

        saga.IncrementVersion();
        await _repository.UpdateAsync(saga, ct);

        foreach (var outbound in evidenceEnvelopes)
        {
            await _publisher.PublishAsync(outbound, ct);
            LogEnvelopePublished(outbound);
        }

        LogIdeationCompletionRecorded(document, candidateIds.Count);
        return HandlerResult.Applied;
    }

    private async Task<HandlerResult> FailSeededValidationAsync(
        EventEnvelope envelope, BatchSaga saga, DocumentProgress document, string reason, CancellationToken ct)
    {
        document.FailureReason ??= reason;
        DocumentTransitions.Advance(document, DocumentState.Failed);

        var releasedDocId = HandleTerminalIfNeeded(saga, document);

        saga.State = BatchTransitions.DeriveFromDocuments(saga.Documents, saga.State);
        saga.IncrementVersion();

        await _repository.UpdateAsync(saga, ct);

        await ReleaseNextIfAvailableAsync(envelope, saga, releasedDocId, ct);

        LogSeededValidationFailed(document, reason);
        return HandlerResult.Applied;
    }

    private void LogAssetEmbeddingUnitRecorded(BatchSaga saga)
    {
        _logger.LogInformation(
            "Asset-embedding unit recorded: {Completed}/{Expected}",
            saga.CompletedAssetEmbeddingUnits,
            saga.ExpectedAssetEmbeddingUnits);
    }

    private void LogDigestCompletionRecorded(EventEnvelope envelope)
    {
        _logger.LogInformation(
            "Digest completion recorded for document {DocumentId}",
            envelope.DocumentId);
    }

    private void LogLandscapeCompletionRecorded(EventEnvelope envelope)
    {
        _logger.LogInformation(
            "Landscape completion recorded for document {DocumentId}",
            envelope.DocumentId);
    }

    private void LogIdeationCompletionRecorded(DocumentProgress document, int seededCandidateCount)
    {
        _logger.LogInformation(
            "Ideation completion recorded for document {DocumentId}: fanned out evidence for {SeededCount} seeded candidates",
            document.DocumentId,
            seededCandidateCount);
    }

    private void LogSeededValidationFailed(DocumentProgress document, string reason)
    {
        _logger.LogInformation(
            "Seeded validation failed for document {DocumentId}: {Reason}",
            document.DocumentId,
            reason);
    }

    private int ComputeExtractionUnitCount(int chunkCount)
    {
        if (chunkCount <= 0)
            return 1;

        return Math.Max(1, (int)Math.Ceiling(chunkCount / (double)_maxChunksPerExtractionUnit));
    }

    private IReadOnlyList<EventEnvelope> BuildExtractionRequests(EventEnvelope source, int chunkCount, int unitCount)
    {
        var envelopes = new List<EventEnvelope>(unitCount);

        for (var unitIndex = 0; unitIndex < unitCount; unitIndex++)
        {
            var chunkStart = unitIndex * _maxChunksPerExtractionUnit;
            var chunkEnd = Math.Min(chunkStart + _maxChunksPerExtractionUnit, chunkCount);

            envelopes.Add(BuildExtractionRequestEnvelope(source, chunkStart, chunkEnd, unitIndex, unitCount));
        }

        return envelopes;
    }

    private static EventEnvelope BuildExtractionRequestEnvelope(
        EventEnvelope source, int chunkStart, int chunkEnd, int unitIndex, int unitCount)
    {
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.ChunkStart] = chunkStart,
            [PayloadKeys.ChunkEnd] = chunkEnd,
            [PayloadKeys.UnitIndex] = unitIndex,
            [PayloadKeys.UnitCount] = unitCount
        };

        return BuildEnvelope(
            SagaEventType.ExtractionRequested,
            source.BatchId,
            source.DocumentId!,
            ResolveCorrelationId(source),
            payload);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveExtractionCompleted(
        EventEnvelope envelope,
        DocumentProgress document)
    {
        var candidateIds = ExtractCandidateIds(envelope.Payload);
        var unitIndex = ExtractPayloadIntOrDefault(envelope.Payload, PayloadKeys.UnitIndex, 0);
        var unitCount = ExtractPayloadIntOrDefault(envelope.Payload, PayloadKeys.UnitCount, 1);

        document.EnsureExpectedExtractionUnits(unitCount);

        if (!document.RecordExtractionUnit(unitIndex, candidateIds))
            return (null, Array.Empty<EventEnvelope>(), false);

        var jobId = ExtractJobId(envelope.Payload, envelope.BatchId);
        var evidenceEnvelopes = BuildEvidenceRequests(envelope, candidateIds, jobId);

        if (!document.AllExtractionUnitsReceived)
            return (null, evidenceEnvelopes, true);

        var targetState = document.ExpectedCandidateCount == 0
            ? DocumentState.NoCandidates
            : DocumentState.Extracted;

        return (targetState, evidenceEnvelopes, false);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveEvidenceCompleted(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document)
    {
        var candidateId = ExtractPayloadString(envelope.Payload, PayloadKeys.CandidateId);
        var evidenceBundleId = ExtractPayloadString(envelope.Payload, PayloadKeys.EvidenceBundleId);
        var jobId = ExtractJobId(envelope.Payload, envelope.BatchId);

        if (!document.IsSeededCandidate(candidateId))
            RecordEvidenceCoverageFromPayload(envelope.Payload, saga, candidateId);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = candidateId,
            [PayloadKeys.EvidenceBundleId] = evidenceBundleId,
            [PayloadKeys.JobId] = jobId
        };

        var outbound = BuildEnvelope(
            SagaEventType.ScoringRequested,
            envelope.BatchId,
            envelope.DocumentId!,
            ResolveCorrelationId(envelope),
            payload);

        return (null, new[] { outbound }, false);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveScoringCompleted(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document)
    {
        var candidateId = ExtractPayloadString(envelope.Payload, PayloadKeys.CandidateId);

        if (document.IsSeededCandidate(candidateId))
            return ResolveSeededScoringCompleted(envelope, document, candidateId);

        if (document.CompletedCandidateIds.Contains(candidateId) || document.FailedCandidateIds.Contains(candidateId))
            return (null, [], false);

        document.MarkCandidateScored(candidateId);

        if (!document.AllCandidatesResolved)
            return (null, [], true);

        return ResolveAllCandidatesResolved(envelope, saga, document);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveSeededScoringCompleted(
        EventEnvelope envelope,
        DocumentProgress document,
        string candidateId)
    {
        if (document.CompletedSeededCandidateIds.Contains(candidateId) || document.FailedSeededCandidateIds.Contains(candidateId))
            return (null, [], false);

        document.MarkSeededScored(candidateId);

        if (!document.AllSeededResolved)
            return (null, [], true);

        return ResolveAllSeededResolved(envelope, document);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveSeededCandidateFailure(
        EventEnvelope envelope,
        DocumentProgress document,
        string candidateId)
    {
        if (document.CompletedSeededCandidateIds.Contains(candidateId) || document.FailedSeededCandidateIds.Contains(candidateId))
            return (null, [], false);

        document.MarkSeededFailed(candidateId);

        if (!document.AllSeededResolved)
            return (null, [], true);

        return ResolveAllSeededResolved(envelope, document);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveAllSeededResolved(
        EventEnvelope envelope,
        DocumentProgress document)
    {
        if (!document.HasAnySeededSuccess)
        {
            document.FailureReason ??= "All seeded candidates failed validation.";
            return (DocumentState.Failed, [], false);
        }

        var reportRequest = BuildSeedingReportRequest(envelope);
        return (null, new[] { reportRequest }, true);
    }

    private static EventEnvelope BuildSeedingReportRequest(EventEnvelope source)
    {
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.DocumentId] = source.DocumentId!,
            [PayloadKeys.ReportTrigger] = ReportTriggerValues.Validation
        };

        return BuildEnvelope(
            SagaEventType.SeedingReportRequested,
            source.BatchId,
            source.DocumentId!,
            ResolveCorrelationId(source),
            payload);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveAllCandidatesResolved(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document)
    {
        if (!document.HasAnySuccessfulCandidate)
        {
            document.FailureReason ??= "All candidates failed.";
            return (DocumentState.Failed, [], false);
        }

        var envelopes = new List<EventEnvelope>();

        if (saga.WantsHarvesting)
            envelopes.Add(BuildSimpleEnvelope(envelope, SagaEventType.HarvestingRequested));

        if (saga.WantsSeeding)
            envelopes.Add(BuildSeedingRequestedEnvelope(envelope, saga));

        if (envelopes.Count == 0)
            return (DocumentState.Complete, [], false);

        return (DocumentState.Scored, envelopes, false);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveEngineCompleted(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document)
    {
        envelope.Payload.TryGetValue("engine", out var engineObj);
        var engine = engineObj?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(engine) || (engine != "harvesting" && engine != "seeding"))
            throw new PermanentProcessingException($"engine.completed missing or unrecognised engine value: '{engine}'");

        var targetState = engine.Equals("harvesting", StringComparison.OrdinalIgnoreCase)
            ? DocumentState.Harvested
            : DocumentState.Seeded;

        var bothEnginesRequired = saga.WantsHarvesting && saga.WantsSeeding;

        if (!bothEnginesRequired)
            return (DocumentState.Complete, [], false);

        var otherDone = targetState == DocumentState.Harvested
            ? document.State == DocumentState.Seeded
            : document.State == DocumentState.Harvested;

        return otherDone ? (DocumentState.Complete, [], false) : (targetState, [], false);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveExtractionFailed(
        EventEnvelope envelope,
        DocumentProgress document)
    {
        envelope.Payload.TryGetValue("reason", out var reasonObj);
        document.FailureReason = reasonObj?.ToString() ?? "Extraction failed.";

        return (DocumentState.Failed, [], false);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveEvidenceFailed(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document)
    {
        var seededCandidateId = TryExtractCandidateId(envelope.Payload);
        if (seededCandidateId is not null && document.IsSeededCandidate(seededCandidateId))
            return ResolveSeededCandidateFailure(envelope, document, seededCandidateId);

        if (document.ExpectedCandidateCount == 0)
        {
            envelope.Payload.TryGetValue("reason", out var reasonObj);
            document.FailureReason = reasonObj?.ToString() ?? "Evidence failed.";
            return (DocumentState.Failed, [], false);
        }

        if (!envelope.Payload.TryGetValue(PayloadKeys.CandidateId, out var candidateIdObj))
        {
            envelope.Payload.TryGetValue("reason", out var reasonObj);
            document.FailureReason = reasonObj?.ToString() ?? "Evidence failed.";
            return (DocumentState.Failed, [], false);
        }

        var candidateId = candidateIdObj?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            envelope.Payload.TryGetValue("reason", out var reasonObj);
            document.FailureReason = reasonObj?.ToString() ?? "Evidence failed.";
            return (DocumentState.Failed, [], false);
        }

        if (document.CompletedCandidateIds.Contains(candidateId) || document.FailedCandidateIds.Contains(candidateId))
            return (null, [], false);

        document.MarkCandidateFailed(candidateId);

        if (!document.AllCandidatesResolved)
            return (null, [], true);

        return ResolveAllCandidatesResolved(envelope, saga, document);
    }

    private static void SetFailureReasonFromPayload(EventEnvelope envelope, DocumentProgress document, string defaultReason)
    {
        envelope.Payload.TryGetValue("reason", out var reasonObj);
        document.FailureReason = reasonObj?.ToString() ?? defaultReason;
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveScoringFailed(
        EventEnvelope envelope,
        BatchSaga saga,
        DocumentProgress document)
    {
        var seededCandidateId = TryExtractCandidateId(envelope.Payload);
        if (seededCandidateId is not null && document.IsSeededCandidate(seededCandidateId))
            return ResolveSeededCandidateFailure(envelope, document, seededCandidateId);

        if (document.ExpectedCandidateCount == 0)
        {
            SetFailureReasonFromPayload(envelope, document, "Scoring failed.");
            return (DocumentState.Failed, [], false);
        }

        if (!envelope.Payload.TryGetValue(PayloadKeys.CandidateId, out var candidateIdObj))
        {
            SetFailureReasonFromPayload(envelope, document, "Scoring failed.");
            return (DocumentState.Failed, [], false);
        }

        var candidateId = candidateIdObj?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            SetFailureReasonFromPayload(envelope, document, "Scoring failed.");
            return (DocumentState.Failed, [], false);
        }

        if (document.CompletedCandidateIds.Contains(candidateId) || document.FailedCandidateIds.Contains(candidateId))
            return (null, [], false);

        document.MarkCandidateFailed(candidateId);

        if (!document.AllCandidatesResolved)
            return (null, [], true);

        return ResolveAllCandidatesResolved(envelope, saga, document);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveHarvestingFailed(
        EventEnvelope envelope,
        DocumentProgress document)
    {
        SetFailureReasonFromPayload(envelope, document, "Harvesting failed.");

        return (DocumentState.Failed, [], false);
    }

    private static (DocumentState? targetState, IReadOnlyList<EventEnvelope> envelopes, bool forceProcessing) ResolveSeedingFailed(
        EventEnvelope envelope,
        DocumentProgress document)
    {
        SetFailureReasonFromPayload(envelope, document, "Seeding failed.");

        return (DocumentState.Failed, [], false);
    }

    private static readonly HashSet<DocumentState> TerminalStates =
    [
        DocumentState.Complete,
        DocumentState.Failed,
        DocumentState.Cancelled,
        DocumentState.NoCandidates
    ];

    private static bool IsAlreadyAtOrPast(DocumentState current, DocumentState? target)
    {
        if (!target.HasValue)
            return false;

        if (TerminalStates.Contains(current))
            return current == target.Value;

        return (int)current >= (int)target.Value;
    }

    private static List<string> ExtractCandidateIds(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue(PayloadKeys.CandidateIds, out var raw))
            throw new PermanentProcessingException("extraction.completed payload missing required 'candidate_ids' field.");

        if (raw is not JsonElement element)
            throw new PermanentProcessingException("extraction.completed 'candidate_ids' field has unexpected type.");

        return element.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
    }

    private static string ExtractJobId(Dictionary<string, object> payload, string fallback)
    {
        return ExtractPayloadStringOrDefault(payload, PayloadKeys.JobId, fallback);
    }

    private static string? TryExtractCandidateId(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue(PayloadKeys.CandidateId, out var raw))
            return null;

        var value = raw switch
        {
            string s => s,
            JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static List<string> ExtractSeededCandidateIds(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue(PayloadKeys.CandidateIds, out var raw))
            throw new PermanentProcessingException("ideation.completed payload missing required 'candidate_ids' field.");

        if (raw is not JsonElement element || element.ValueKind != JsonValueKind.Array)
            throw new PermanentProcessingException("ideation.completed 'candidate_ids' field has unexpected type.");

        return element.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
    }

    private static string ExtractPayloadString(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out var raw))
            throw new PermanentProcessingException($"Payload missing required '{key}' field.");

        var value = raw switch
        {
            string s => s,
            JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(value))
            throw new PermanentProcessingException($"Payload '{key}' field is empty or invalid.");

        return value;
    }

    private static string ExtractPayloadStringOrDefault(Dictionary<string, object> payload, string key, string fallback)
    {
        if (!payload.TryGetValue(key, out var raw))
            return fallback;

        var value = raw switch
        {
            string s => s,
            JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ExtractPayloadIntOrDefault(Dictionary<string, object> payload, string key, int fallback)
    {
        if (!payload.TryGetValue(key, out var raw))
            return fallback;

        return raw switch
        {
            int i => i,
            long l => (int)l,
            JsonElement e when e.ValueKind == JsonValueKind.Number => e.GetInt32(),
            _ => fallback
        };
    }

    private static IReadOnlyList<EventEnvelope> BuildEvidenceRequests(
        EventEnvelope source,
        List<string> candidateIds,
        string jobId)
    {
        return candidateIds.Select(candidateId =>
        {
            var payload = new Dictionary<string, object>
            {
                [PayloadKeys.CandidateId] = candidateId,
                [PayloadKeys.JobId] = jobId
            };

            return BuildEnvelope(
                SagaEventType.EvidenceRequested,
                source.BatchId,
                source.DocumentId!,
                ResolveCorrelationId(source),
                payload);
        }).ToList();
    }

    private static EventEnvelope[] BuildSimpleTransition(EventEnvelope source, string nextEventType)
    {
        return new[] { BuildSimpleEnvelope(source, nextEventType) };
    }

    private static EventEnvelope BuildSimpleEnvelope(EventEnvelope source, string eventType)
    {
        return BuildEnvelope(
            eventType,
            source.BatchId,
            source.DocumentId!,
            ResolveCorrelationId(source),
            new Dictionary<string, object>());
    }

    private static EventEnvelope BuildSeedingRequestedEnvelope(EventEnvelope source, BatchSaga saga)
    {
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.SeedingMode] = SeedingModes.Normalize(saga.Metadata?.SeedingMode)
        };

        return BuildEnvelope(
            SagaEventType.SeedingRequested,
            source.BatchId,
            source.DocumentId!,
            ResolveCorrelationId(source),
            payload);
    }

    private static string ResolveCorrelationId(EventEnvelope source)
    {
        return string.IsNullOrEmpty(source.CorrelationId) ? source.BatchId : source.CorrelationId;
    }

    private static EventEnvelope BuildEnvelope(
        string eventType,
        string batchId,
        string documentId,
        string correlationId,
        Dictionary<string, object> payload)
    {
        return new EventEnvelope
        {
            EventType = eventType,
            BatchId = batchId,
            DocumentId = documentId,
            CorrelationId = correlationId,
            Payload = payload
        };
    }

    private static void RecordEvidenceCoverageFromPayload(
        Dictionary<string, object> payload, BatchSaga saga, string candidateId)
    {
        if (!payload.TryGetValue("source_coverage", out var coverageObj))
            return;

        if (coverageObj is not JsonElement coverageElement)
            return;

        var hasPatentApi = ExtractSourceCoverageFlag(coverageElement, "has_patent_api");
        var hasCorpus = ExtractSourceCoverageFlag(coverageElement, "has_corpus");
        var hasLlm = ExtractSourceCoverageFlag(coverageElement, "has_llm");

        saga.TryRecordEvidenceCoverage(candidateId, hasPatentApi, hasCorpus, hasLlm);
    }

    private static bool ExtractSourceCoverageFlag(JsonElement coverage, string key)
    {
        if (!coverage.TryGetProperty(key, out var flag))
            return false;

        return flag.ValueKind == JsonValueKind.True;
    }
}
