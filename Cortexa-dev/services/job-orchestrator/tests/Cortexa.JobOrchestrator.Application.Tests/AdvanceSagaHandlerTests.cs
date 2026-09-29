using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class AdvanceSagaHandlerTests
{
    private readonly ISagaRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly AdvanceSagaHandler _handler;

    public AdvanceSagaHandlerTests()
    {
        _repository = Substitute.For<ISagaRepository>();
        _publisher = Substitute.For<IEventPublisher>();
        _handler = new AdvanceSagaHandler(_repository, _publisher, NullLogger<AdvanceSagaHandler>.Instance);
    }

    private static AdvanceSagaHandler CreateHandlerWithUnitCap(
        ISagaRepository repository, IEventPublisher publisher, int maxChunksPerExtractionUnit) =>
        new(repository, publisher, NullLogger<AdvanceSagaHandler>.Instance, maxChunksPerExtractionUnit);

    private static BatchSaga CreateSaga(
        string batchId = "batch-1",
        bool wantsHarvesting = true,
        bool wantsSeeding = false,
        List<DocumentProgress>? documents = null,
        string? seedingMode = null)
    {
        var metadata = seedingMode is null
            ? null
            : new BatchMetadata(BatchName: "Test", CreatedAt: DateTimeOffset.UtcNow, SeedingMode: seedingMode);

        return new BatchSaga(
            batchId,
            BatchState.InProgress,
            documents ?? [],
            wantsHarvesting,
            wantsSeeding,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1,
            metadata: metadata);
    }

    private static EventEnvelope CreateEnvelope(
        string eventType,
        string batchId = "batch-1",
        string documentId = "doc-1",
        Dictionary<string, object>? payload = null)
    {
        return new EventEnvelope
        {
            EventType = eventType,
            BatchId = batchId,
            DocumentId = documentId,
            Payload = payload ?? []
        };
    }

    [Fact]
    public async Task HandleAsync_DuplicateEvent_ReturnsAlreadyApplied()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidTransition_UpdatesStateAndPublishesNextEvent()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Ingested);
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.ExtractionRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_InvalidTransition_ThrowsInvalidTransitionException()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: payload);

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTransitionException>();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ConcurrencyConflict_PropagatesException()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);
        _repository.UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("batch-1"));

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted);

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task HandleAsync_ScoringCompletedWantsHarvesting_PublishesHarvestingRequested()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.SeedingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ScoringCompletedWantsBoth_PublishesBothFanOut()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: true, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.SeedingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_IngestionCompletedLegacySeeding_DoesNotFanOutAssetEmbedding()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: true, documents: [doc], seedingMode: SeedingModes.Legacy);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        await _publisher.DidNotReceive().PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.AssetEmbeddingRequested),
            Arg.Any<CancellationToken>());
        saga.ExpectedAssetEmbeddingUnits.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_IngestionCompletedDeepSeeding_FansOutAssetEmbeddingAndExpectsUnits()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: true, documents: [doc], seedingMode: SeedingModes.Deep);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.AssetEmbeddingRequested),
            Arg.Any<CancellationToken>());
        saga.ExpectedAssetEmbeddingUnits.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HandleAsync_ScoringCompletedSeeding_StampsSeedingModeIntoPayload()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: true, documents: [doc], seedingMode: SeedingModes.Deep);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.SeedingRequested
                && e.Payload.ContainsKey(PayloadKeys.SeedingMode)
                && (string)e.Payload[PayloadKeys.SeedingMode] == SeedingModes.Deep),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_VersionIncrementedOnApply()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);
        var initialVersion = saga.Version;

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        saga.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public async Task HandleAsync_DocumentNotInSaga_CreatesDocumentAndAdvances()
    {
        var saga = CreateSaga(documents: []);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, documentId: "new-doc");

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        saga.Documents.Should().ContainSingle(d => d.DocumentId == "new-doc" && d.State == DocumentState.Ingested);
    }

    [Fact]
    public async Task HandleAsync_DuplicateEvidenceCompleted_ReturnsAlreadyAppliedWithoutCosmosWrite()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope("unknown.event.type");

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CompleteTargetEventForFailedDocument_ThrowsInvalidTransitionException()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Failed);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: payload);

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTransitionException>();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NullDocumentId_ThrowsPermanentProcessingException()
    {
        var saga = CreateSaga(documents: []);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.IngestionCompleted,
            BatchId = "batch-1",
            DocumentId = null,
            Payload = []
        };

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<PermanentProcessingException>()
            .WithMessage("*document_id*");
    }

    [Theory]
    [InlineData(BatchState.Completed)]
    [InlineData(BatchState.Failed)]
    [InlineData(BatchState.Cancelled)]
    public async Task HandleAsync_TerminalBatchState_ReturnsAlreadyAppliedWithoutWriteOrPublish(BatchState terminalState)
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = new BatchSaga(
            "batch-1",
            terminalState,
            [doc],
            wantsHarvesting: false,
            wantsSeeding: false,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-engine")]
    [InlineData("HARVESTING_TYPO")]
    public async Task HandleAsync_EngineCompleted_UnrecognisedEngineValue_ThrowsPermanentProcessingException(string engineValue)
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["engine"] = engineValue };
        var envelope = CreateEnvelope(SagaEventType.EngineCompleted, payload: payload);

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<PermanentProcessingException>()
            .WithMessage("*engine.completed*");
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EngineCompleted_MissingEngineKey_ThrowsPermanentProcessingException()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.EngineCompleted, payload: []);

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<PermanentProcessingException>()
            .WithMessage("*engine.completed*");
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionFailed_TransitionsIngestedToFailedAndSetsFailureReason()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            ["reason"] = "Unsupported document format",
            ["document_id"] = "doc-1",
            ["job_id"] = "job-123",
            ["trigger_type"] = "harvesting"
        };
        var envelope = CreateEnvelope(SagaEventType.ExtractionFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Unsupported document format");
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionFailed_MissingReasonKey_UsesDefaultFailureReason()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.ExtractionFailed, payload: []);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Extraction failed.");
    }

    [Fact]
    public async Task HandleAsync_ExtractionFailed_MarksDocumentTerminalAndReleasesNextQueuedDocument()
    {
        var failingDoc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var queuedDoc = new DocumentProgress("doc-2", DocumentState.Queued);
        var saga = new BatchSaga(
            "batch-1",
            BatchState.InProgress,
            [failingDoc, queuedDoc],
            wantsHarvesting: true,
            wantsSeeding: false,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1,
            activeDocumentIds: ["doc-1"],
            queuedDocumentIds: new Queue<string>(["doc-2"]));

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Timeout during extraction" };
        var envelope = CreateEnvelope(SagaEventType.ExtractionFailed, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.IngestionRequested && e.DocumentId == "doc-2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionFailed_AllDocumentsFailed_BatchDerivesToFailed()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Corrupt file" };
        var envelope = CreateEnvelope(SagaEventType.ExtractionFailed, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        saga.State.Should().Be(BatchState.Failed);
    }

    [Fact]
    public async Task HandleAsync_ExtractionFailed_OneFailedOneComplete_BatchDerivesToCompleted()
    {
        var failedDoc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var completedDoc = new DocumentProgress("doc-2", DocumentState.Complete);
        var saga = CreateSaga(documents: [failedDoc, completedDoc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Corrupt file" };
        var envelope = CreateEnvelope(SagaEventType.ExtractionFailed, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        saga.State.Should().Be(BatchState.Completed);
    }

    [Fact]
    public async Task HandleAsync_DuplicateExtractionFailed_ReturnsAlreadyApplied()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Failed) { FailureReason = "Corrupt file" };
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Corrupt file" };
        var envelope = CreateEnvelope(SagaEventType.ExtractionFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EvidenceFailed_TransitionsExtractedToFailedAndSetsFailureReason()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            ["reason"] = "Evidence API unavailable",
            ["document_id"] = "doc-1",
            ["job_id"] = "job-123",
            ["candidate_id"] = "cand-456",
            ["trigger_type"] = "harvesting"
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Evidence API unavailable");
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EvidenceFailed_MissingReasonKey_UsesDefaultFailureReason()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: []);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Evidence failed.");
    }

    [Fact]
    public async Task HandleAsync_EvidenceFailed_MarksDocumentTerminalAndReleasesNextQueuedDocument()
    {
        var failingDoc = new DocumentProgress("doc-1", DocumentState.Extracted);
        var queuedDoc = new DocumentProgress("doc-2", DocumentState.Queued);
        var saga = new BatchSaga(
            "batch-1",
            BatchState.InProgress,
            [failingDoc, queuedDoc],
            wantsHarvesting: true,
            wantsSeeding: false,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1,
            activeDocumentIds: ["doc-1"],
            queuedDocumentIds: new Queue<string>(["doc-2"]));

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Timeout during evidence collection" };
        var envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.IngestionRequested && e.DocumentId == "doc-2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EvidenceFailed_AllDocumentsFailed_BatchDerivesToFailed()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Patent API down" };
        var envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        saga.State.Should().Be(BatchState.Failed);
    }

    [Fact]
    public async Task HandleAsync_EvidenceFailed_OneFailedOneComplete_BatchDerivesToCompleted()
    {
        var failedDoc = new DocumentProgress("doc-1", DocumentState.Extracted);
        var completedDoc = new DocumentProgress("doc-2", DocumentState.Complete);
        var saga = CreateSaga(documents: [failedDoc, completedDoc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Patent API down" };
        var envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        saga.State.Should().Be(BatchState.Completed);
    }

    [Fact]
    public async Task HandleAsync_DuplicateEvidenceFailed_ReturnsAlreadyApplied()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Failed) { FailureReason = "Patent API down" };
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Patent API down" };
        var envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionCompleted_ThreeCandidates_EmitsThreeEvidenceRequested()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[\"cand-1\", \"cand-2\", \"cand-3\"]").RootElement,
            [PayloadKeys.JobId] = "job-123"
        };
        var envelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Extracted);
        doc.ExpectedCandidateCount.Should().Be(3);

        await _publisher.Received(3).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.EvidenceRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionCompleted_EachEvidenceRequestCarriesCandidateId()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[\"cand-1\", \"cand-2\"]").RootElement,
            [PayloadKeys.JobId] = "job-123"
        };
        var envelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: payload);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        captured.Should().HaveCount(2);
        captured[0].Payload[PayloadKeys.CandidateId].Should().Be("cand-1");
        captured[0].Payload[PayloadKeys.JobId].Should().Be("job-123");
        captured[1].Payload[PayloadKeys.CandidateId].Should().Be("cand-2");
        captured[1].Payload[PayloadKeys.JobId].Should().Be("job-123");
    }

    [Fact]
    public async Task HandleAsync_EvidenceCompleted_EmitsScoringRequestedWithCandidateAndEvidenceBundleId()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.EvidenceBundleId] = "eb-456",
            [PayloadKeys.JobId] = "job-123"
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceCompleted, payload: payload);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        captured.Should().ContainSingle();
        captured[0].EventType.Should().Be(SagaEventType.ScoringRequested);
        captured[0].Payload[PayloadKeys.CandidateId].Should().Be("cand-1");
        captured[0].Payload[PayloadKeys.EvidenceBundleId].Should().Be("eb-456");
        captured[0].Payload[PayloadKeys.JobId].Should().Be("job-123");
    }

    [Fact]
    public async Task HandleAsync_ScoringCompleted_TwoCandidates_FirstCompleted_DocumentStaysExtracted()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Extracted);
        doc.CompletedCandidateIds.Should().Contain("cand-1");
        await _publisher.DidNotReceive().PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ScoringCompleted_TwoCandidates_SecondCompleted_DocumentAdvancesToScoredAndEmitsHarvesting()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        doc.MarkCandidateScored("cand-1");
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-2" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Scored);
        doc.CompletedCandidateIds.Should().Contain("cand-2");
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionCompleted_MissingCandidateIds_ThrowsPermanentProcessingException()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: []);

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<PermanentProcessingException>()
            .WithMessage("*candidate_ids*");
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionCompleted_EmptyCandidateIds_TransitionsToNoCandidatesWithoutFanOutOrException()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[]").RootElement
        };
        var envelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.NoCandidates);
        doc.ExpectedCandidateCount.Should().Be(0);
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.EvidenceRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionCompleted_ZeroCandidates_BatchDerivesToCompleted()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[]").RootElement
        };
        var envelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        saga.State.Should().Be(BatchState.Completed);
    }

    [Fact]
    public async Task HandleAsync_EvidenceCompleted_MissingCandidateId_ThrowsPermanentProcessingException()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.EvidenceBundleId] = "eb-456",
            [PayloadKeys.JobId] = "job-123"
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceCompleted, payload: payload);

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<PermanentProcessingException>()
            .WithMessage("*candidate_id*");
    }

    [Fact]
    public async Task HandleAsync_TwoCandidates_OneEvidenceFailsOneScores_DocumentReachesScored()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var evidenceFailedPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.JobId] = "job-123"
        };
        var evidenceFailedEnvelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: evidenceFailedPayload);

        var result1 = await _handler.HandleAsync(evidenceFailedEnvelope, CancellationToken.None);

        result1.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Extracted);
        doc.FailedCandidateIds.Should().Contain("cand-1");

        var scoringPayload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-2" };
        var scoringEnvelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: scoringPayload);

        var result2 = await _handler.HandleAsync(scoringEnvelope, CancellationToken.None);

        result2.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Scored);
        doc.CompletedCandidateIds.Should().Contain("cand-2");
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TwoCandidates_BothEvidenceFail_DocumentFails()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var evidenceFailed1Payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.JobId] = "job-123"
        };
        var evidenceFailed1Envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: evidenceFailed1Payload);

        var result1 = await _handler.HandleAsync(evidenceFailed1Envelope, CancellationToken.None);

        result1.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Extracted);
        doc.FailedCandidateIds.Should().Contain("cand-1");

        var evidenceFailed2Payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-2",
            [PayloadKeys.JobId] = "job-123"
        };
        var evidenceFailed2Envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: evidenceFailed2Payload);

        var result2 = await _handler.HandleAsync(evidenceFailed2Envelope, CancellationToken.None);

        result2.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailedCandidateIds.Should().Contain("cand-2");
        doc.FailureReason.Should().Contain("All candidates failed");
    }

    [Fact]
    public async Task HandleAsync_SingleCandidateEvidenceFailed_DocumentFails()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.JobId] = "job-123"
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailedCandidateIds.Should().Contain("cand-1");
        doc.FailureReason.Should().Contain("All candidates failed");
    }

    [Fact]
    public async Task HandleAsync_ScoringFailed_LastUnresolvedCandidate_DocumentAdvancesToScoredAndEmitsHarvesting()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        doc.MarkCandidateScored("cand-1");
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-2",
            ["reason"] = "Scoring engine timeout"
        };
        var envelope = CreateEnvelope(SagaEventType.ScoringFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.FailedCandidateIds.Should().Contain("cand-2");
        doc.State.Should().Be(DocumentState.Scored);
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ScoringFailed_LastUnresolvedCandidateNoEnginesWanted_DocumentReachesComplete()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        doc.SetExpectedCandidates(2);
        doc.MarkCandidateScored("cand-1");
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-2",
            ["reason"] = "Scoring engine timeout"
        };
        var envelope = CreateEnvelope(SagaEventType.ScoringFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.FailedCandidateIds.Should().Contain("cand-2");
        doc.State.Should().Be(DocumentState.Complete);
        saga.State.Should().Be(BatchState.Completed);
    }

    [Fact]
    public async Task HandleAsync_ScoringFailed_AllCandidatesFail_DocumentFailsWithDefaultReason()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Contain("All candidates failed");
    }

    [Fact]
    public async Task HandleAsync_ScoringFailed_MissingCandidateId_FailsWholeDocument()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Scoring service unreachable" };
        var envelope = CreateEnvelope(SagaEventType.ScoringFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Scoring service unreachable");
    }

    [Fact]
    public async Task HandleAsync_HarvestingFailed_TransitionsDocumentToFailedWithReasonFromPayload()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Harvesting engine crashed" };
        var envelope = CreateEnvelope(SagaEventType.HarvestingFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Harvesting engine crashed");
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HarvestingFailed_MissingReasonKey_UsesDefaultFailureReason()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.HarvestingFailed, payload: []);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Harvesting failed.");
    }

    [Fact]
    public async Task HandleAsync_SeedingFailed_TransitionsDocumentToFailedWithReasonFromPayload()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: true, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Seeding lattice generation failed" };
        var envelope = CreateEnvelope(SagaEventType.SeedingFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Seeding lattice generation failed");
    }

    [Fact]
    public async Task HandleAsync_SeedingFailed_MissingReasonKey_UsesDefaultFailureReason()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: true, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.SeedingFailed, payload: []);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Be("Seeding failed.");
    }

    [Fact]
    public async Task HandleAsync_DuplicateHarvestingFailed_ReturnsAlreadyApplied()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Failed) { FailureReason = "Harvesting engine crashed" };
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { ["reason"] = "Harvesting engine crashed" };
        var envelope = CreateEnvelope(SagaEventType.HarvestingFailed, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BatchState.Completed)]
    [InlineData(BatchState.Failed)]
    [InlineData(BatchState.Cancelled)]
    public async Task HandleAsync_NewFailedEventTypes_TerminalBatchState_ReturnsAlreadyAppliedWithoutWriteOrPublish(
        BatchState terminalState)
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = new BatchSaga(
            "batch-1",
            terminalState,
            [doc],
            wantsHarvesting: true,
            wantsSeeding: true,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        foreach (var eventType in new[]
                 {
                     SagaEventType.ScoringFailed,
                     SagaEventType.HarvestingFailed,
                     SagaEventType.SeedingFailed
                 })
        {
            var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
            var envelope = CreateEnvelope(eventType, payload: payload);

            var result = await _handler.HandleAsync(envelope, CancellationToken.None);

            result.Should().Be(HandlerResult.AlreadyApplied);
        }

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TwoCandidates_OneScoresOneEvidenceFails_DocumentReachesScored()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var scoringPayload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var scoringEnvelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: scoringPayload);

        var result1 = await _handler.HandleAsync(scoringEnvelope, CancellationToken.None);

        result1.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Extracted);
        doc.CompletedCandidateIds.Should().Contain("cand-1");

        var evidenceFailedPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-2",
            [PayloadKeys.JobId] = "job-123"
        };
        var evidenceFailedEnvelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: evidenceFailedPayload);

        var result2 = await _handler.HandleAsync(evidenceFailedEnvelope, CancellationToken.None);

        result2.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Scored);
        doc.FailedCandidateIds.Should().Contain("cand-2");
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EvidenceCompletedWithCoverage_RecordsSourceCoverage()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.EvidenceBundleId] = "bundle-1",
            [PayloadKeys.JobId] = "job-1",
            ["source_coverage"] = System.Text.Json.JsonDocument.Parse(
                "{\"has_patent_api\": true, \"has_corpus\": false, \"has_llm\": true}").RootElement
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        saga.EvidenceCompletedCount.Should().Be(1);
        saga.EvidenceSourceLiveCounts["PatentApi"].Should().Be(1);
        saga.EvidenceSourceLiveCounts["LlmResearch"].Should().Be(1);
        saga.EvidenceSourceLiveCounts.Should().NotContainKey("SeedCorpus");
    }

    [Fact]
    public async Task HandleAsync_EvidenceCompletedWithMissingCoverage_HandledSafely()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.EvidenceBundleId] = "bundle-1",
            [PayloadKeys.JobId] = "job-1"
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        saga.EvidenceCompletedCount.Should().Be(0);
        saga.EvidenceSourceLiveCounts.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_LateEvidenceFailedAfterScoringCompletedSameCandidate_IsNoOp()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var scoringPayload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var scoringEnvelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: scoringPayload);

        var scoringResult = await _handler.HandleAsync(scoringEnvelope, CancellationToken.None);
        scoringResult.Should().Be(HandlerResult.Applied);
        doc.CompletedCandidateIds.Should().Contain("cand-1");

        _repository.ClearReceivedCalls();
        _publisher.ClearReceivedCalls();

        var lateEvidenceFailedPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.JobId] = "job-123"
        };
        var lateEvidenceFailedEnvelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: lateEvidenceFailedPayload);

        var result = await _handler.HandleAsync(lateEvidenceFailedEnvelope, CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        doc.CompletedCandidateIds.Should().Contain("cand-1");
        doc.FailedCandidateIds.Should().NotContain("cand-1");
        doc.State.Should().Be(DocumentState.Extracted);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_LateEvidenceCompletedAfterEvidenceFailedSameCandidate_IsNoOp()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var evidenceFailedPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.JobId] = "job-123"
        };
        var evidenceFailedEnvelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: evidenceFailedPayload);

        var failResult = await _handler.HandleAsync(evidenceFailedEnvelope, CancellationToken.None);
        failResult.Should().Be(HandlerResult.Applied);
        doc.FailedCandidateIds.Should().Contain("cand-1");

        _repository.ClearReceivedCalls();
        _publisher.ClearReceivedCalls();

        var lateScoringPayload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var lateScoringEnvelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: lateScoringPayload);

        var result = await _handler.HandleAsync(lateScoringEnvelope, CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        doc.FailedCandidateIds.Should().Contain("cand-1");
        doc.CompletedCandidateIds.Should().NotContain("cand-1");
        doc.State.Should().Be(DocumentState.Extracted);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MixedCandidateOutcomes_BatchWithOneSuccessDoesNotHardFail()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var evidenceFailedPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.JobId] = "job-123"
        };
        var evidenceFailedEnvelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload: evidenceFailedPayload);
        await _handler.HandleAsync(evidenceFailedEnvelope, CancellationToken.None);

        var scoringPayload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-2" };
        var scoringEnvelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload: scoringPayload);
        await _handler.HandleAsync(scoringEnvelope, CancellationToken.None);

        doc.State.Should().Be(DocumentState.Complete);
        saga.State.Should().NotBe(BatchState.Failed);
        saga.State.Should().Be(BatchState.Completed);
    }

    [Fact]
    public async Task HandleAsync_EvidenceCompletedRedelivered_CountsCandidateOnce()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(2);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.EvidenceBundleId] = "bundle-1",
            [PayloadKeys.JobId] = "job-1",
            ["source_coverage"] = System.Text.Json.JsonDocument.Parse(
                "{\"has_patent_api\": true, \"has_corpus\": true, \"has_llm\": false}").RootElement
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceCompleted, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);
        await _handler.HandleAsync(envelope, CancellationToken.None);

        saga.EvidenceCompletedCount.Should().Be(1);
        saga.EvidenceSourceLiveCounts["PatentApi"].Should().Be(1);
        saga.EvidenceSourceLiveCounts["SeedCorpus"].Should().Be(1);
        saga.EvidenceSourceLiveCounts.Should().NotContainKey("LlmResearch");
    }

    [Fact]
    public async Task HandleAsync_IngestionCompleted_SmallChunkCount_FansOutSingleExtractionRequestWithFullRange()
    {
        var handler = CreateHandlerWithUnitCap(_repository, _publisher, maxChunksPerExtractionUnit: 25);
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.ChunkCount] = 12 };
        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, payload: payload);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        captured.Should().ContainSingle();
        captured[0].EventType.Should().Be(SagaEventType.ExtractionRequested);
        captured[0].Payload[PayloadKeys.ChunkStart].Should().Be(0);
        captured[0].Payload[PayloadKeys.ChunkEnd].Should().Be(12);
        captured[0].Payload[PayloadKeys.UnitIndex].Should().Be(0);
        captured[0].Payload[PayloadKeys.UnitCount].Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_IngestionCompleted_LargeChunkCount_FansOutBoundedExtractionUnits()
    {
        var handler = CreateHandlerWithUnitCap(_repository, _publisher, maxChunksPerExtractionUnit: 25);
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.ChunkCount] = 485 };
        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, payload: payload);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        captured.Should().HaveCount(20);
        captured.Should().OnlyContain(e => e.EventType == SagaEventType.ExtractionRequested);
        captured[0].Payload[PayloadKeys.ChunkStart].Should().Be(0);
        captured[0].Payload[PayloadKeys.ChunkEnd].Should().Be(25);
        captured[0].Payload[PayloadKeys.UnitCount].Should().Be(20);
        captured[19].Payload[PayloadKeys.UnitIndex].Should().Be(19);
        captured[19].Payload[PayloadKeys.ChunkStart].Should().Be(475);
        captured[19].Payload[PayloadKeys.ChunkEnd].Should().Be(485);
    }

    [Fact]
    public async Task HandleAsync_IngestionCompleted_MissingChunkCount_FansOutSingleExtractionRequest()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, payload: []);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        captured.Should().ContainSingle();
        captured[0].EventType.Should().Be(SagaEventType.ExtractionRequested);
        captured[0].Payload[PayloadKeys.UnitIndex].Should().Be(0);
        captured[0].Payload[PayloadKeys.UnitCount].Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_ExtractionCompleted_TwoUnits_DocumentStaysExtractedOnlyAfterBothUnitsArrive()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var firstUnitPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[\"cand-1\"]").RootElement,
            [PayloadKeys.UnitIndex] = 0,
            [PayloadKeys.UnitCount] = 2,
            [PayloadKeys.JobId] = "job-123"
        };
        var firstUnitEnvelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: firstUnitPayload);

        var firstResult = await _handler.HandleAsync(firstUnitEnvelope, CancellationToken.None);

        firstResult.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Ingested);
        doc.ExpectedCandidateCount.Should().Be(1);
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.EvidenceRequested
                && e.Payload[PayloadKeys.CandidateId].Equals("cand-1")),
            Arg.Any<CancellationToken>());

        var secondUnitPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[\"cand-2\"]").RootElement,
            [PayloadKeys.UnitIndex] = 1,
            [PayloadKeys.UnitCount] = 2,
            [PayloadKeys.JobId] = "job-123"
        };
        var secondUnitEnvelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: secondUnitPayload);

        var secondResult = await _handler.HandleAsync(secondUnitEnvelope, CancellationToken.None);

        secondResult.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Extracted);
        doc.ExpectedCandidateCount.Should().Be(2);
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.EvidenceRequested
                && e.Payload[PayloadKeys.CandidateId].Equals("cand-2")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionCompleted_DuplicateUnitIndex_IsNoOpAndDoesNotDoubleCount()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[\"cand-1\"]").RootElement,
            [PayloadKeys.UnitIndex] = 0,
            [PayloadKeys.UnitCount] = 2,
            [PayloadKeys.JobId] = "job-123"
        };
        var envelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: payload);

        var firstResult = await _handler.HandleAsync(envelope, CancellationToken.None);
        firstResult.Should().Be(HandlerResult.Applied);
        doc.ExpectedCandidateCount.Should().Be(1);

        _repository.ClearReceivedCalls();
        _publisher.ClearReceivedCalls();

        var redeliveredResult = await _handler.HandleAsync(envelope, CancellationToken.None);

        redeliveredResult.Should().Be(HandlerResult.AlreadyApplied);
        doc.ExpectedCandidateCount.Should().Be(1);
        doc.State.Should().Be(DocumentState.Ingested);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExtractionCompleted_ThreeUnitsAllZeroCandidates_ReachesNoCandidatesOnlyAfterAllUnits()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        for (var unitIndex = 0; unitIndex < 3; unitIndex++)
        {
            var payload = new Dictionary<string, object>
            {
                [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[]").RootElement,
                [PayloadKeys.UnitIndex] = unitIndex,
                [PayloadKeys.UnitCount] = 3
            };
            var envelope = CreateEnvelope(SagaEventType.ExtractionCompleted, payload: payload);

            var result = await _handler.HandleAsync(envelope, CancellationToken.None);

            result.Should().Be(HandlerResult.Applied);

            if (unitIndex < 2)
                doc.State.Should().Be(DocumentState.Ingested);
            else
                doc.State.Should().Be(DocumentState.NoCandidates);
        }

        doc.ExpectedCandidateCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_IngestionCompleted_HarvestingOnly_PublishesNoAssetEmbeddingEnvelopes()
    {
        var handler = CreateHandlerWithUnitCap(_repository, _publisher, maxChunksPerExtractionUnit: 25);
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(wantsHarvesting: true, wantsSeeding: false, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.ChunkCount] = 60 };
        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, payload: payload);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        captured.Should().OnlyContain(e => e.EventType == SagaEventType.ExtractionRequested);
        captured.Should().NotContain(e => e.EventType == SagaEventType.AssetEmbeddingRequested);
        saga.ExpectedAssetEmbeddingUnits.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_IngestionCompleted_SeedingRequested_FansOutBoundedAssetEmbeddingUnits()
    {
        var handler = CreateHandlerWithUnitCap(_repository, _publisher, maxChunksPerExtractionUnit: 25);
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: true, documents: [doc], seedingMode: SeedingModes.Deep);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.ChunkCount] = 60 };
        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, payload: payload);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);

        var embeddingEnvelopes = captured
            .Where(e => e.EventType == SagaEventType.AssetEmbeddingRequested)
            .ToList();

        embeddingEnvelopes.Should().HaveCount(3);
        embeddingEnvelopes[0].Payload[PayloadKeys.DocumentId].Should().Be("doc-1");
        embeddingEnvelopes[0].Payload[PayloadKeys.ChunkStart].Should().Be(0);
        embeddingEnvelopes[0].Payload[PayloadKeys.ChunkEnd].Should().Be(25);
        embeddingEnvelopes[0].Payload[PayloadKeys.UnitIndex].Should().Be(0);
        embeddingEnvelopes[0].Payload[PayloadKeys.UnitCount].Should().Be(3);
        embeddingEnvelopes[2].Payload[PayloadKeys.ChunkStart].Should().Be(50);
        embeddingEnvelopes[2].Payload[PayloadKeys.ChunkEnd].Should().Be(60);
        embeddingEnvelopes[2].Payload[PayloadKeys.UnitIndex].Should().Be(2);

        captured.Should().Contain(e => e.EventType == SagaEventType.ExtractionRequested);
        saga.ExpectedAssetEmbeddingUnits.Should().Be(3);
    }

    [Fact]
    public async Task HandleAsync_IngestionCompleted_SeedingRequested_DoesNotStampModelOnAssetEmbeddingEnvelopes()
    {
        var handler = CreateHandlerWithUnitCap(_repository, _publisher, maxChunksPerExtractionUnit: 25);
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(wantsHarvesting: false, wantsSeeding: true, documents: [doc], seedingMode: SeedingModes.Deep);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.ChunkCount] = 10 };
        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, payload: payload);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await handler.HandleAsync(envelope, CancellationToken.None);

        captured
            .Where(e => e.EventType == SagaEventType.AssetEmbeddingRequested)
            .Should().OnlyContain(e => !e.Payload.ContainsKey(PayloadKeys.AiModel));
    }

    [Fact]
    public async Task HandleAsync_AssetEmbeddingCompleted_RecordsUnitWithoutStateTransitionOrPublish()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(wantsSeeding: true, documents: [doc]);
        saga.AddExpectedAssetEmbeddingUnits(2);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.UnitIndex] = 0,
            [PayloadKeys.UnitCount] = 2
        };
        var envelope = CreateEnvelope(SagaEventType.AssetEmbeddingCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Ingested);
        saga.State.Should().Be(BatchState.InProgress);
        saga.CompletedAssetEmbeddingUnits.Should().Be(1);
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AssetEmbeddingCompleted_DuplicateUnit_IsAlreadyAppliedAndDoesNotDoubleCount()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(wantsSeeding: true, documents: [doc]);
        saga.AddExpectedAssetEmbeddingUnits(2);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.UnitIndex] = 0 };
        var first = CreateEnvelope(SagaEventType.AssetEmbeddingCompleted, payload: payload);
        var second = CreateEnvelope(SagaEventType.AssetEmbeddingCompleted, payload: payload);

        await _handler.HandleAsync(first, CancellationToken.None);
        var secondResult = await _handler.HandleAsync(second, CancellationToken.None);

        secondResult.Should().Be(HandlerResult.AlreadyApplied);
        saga.CompletedAssetEmbeddingUnits.Should().Be(1);
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AssetEmbeddingCompleted_LastUnit_FansOutSingleDigestRequest()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(wantsSeeding: true, documents: [doc]);
        saga.AddExpectedAssetEmbeddingUnits(2);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(CreateAssetEmbeddingCompleted(0, 2), CancellationToken.None);
        captured.Should().NotContain(e => e.EventType == SagaEventType.DigestRequested);

        await _handler.HandleAsync(CreateAssetEmbeddingCompleted(1, 2), CancellationToken.None);

        var digestEnvelopes = captured
            .Where(e => e.EventType == SagaEventType.DigestRequested)
            .ToList();

        digestEnvelopes.Should().HaveCount(1);
        digestEnvelopes[0].DocumentId.Should().Be("doc-1");
        digestEnvelopes[0].Payload[PayloadKeys.DocumentId].Should().Be("doc-1");
        digestEnvelopes[0].Payload[PayloadKeys.UnitIndex].Should().Be(0);
        digestEnvelopes[0].Payload[PayloadKeys.UnitCount].Should().Be(1);
        saga.RecordedDigestRequestedDocumentIds.Should().Contain("doc-1");
    }

    [Fact]
    public async Task HandleAsync_AssetEmbeddingCompleted_LastUnitRedelivered_DoesNotFanOutSecondDigest()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSaga(wantsSeeding: true, documents: [doc]);
        saga.AddExpectedAssetEmbeddingUnits(2);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(CreateAssetEmbeddingCompleted(0, 2), CancellationToken.None);
        await _handler.HandleAsync(CreateAssetEmbeddingCompleted(1, 2), CancellationToken.None);
        var redelivered = await _handler.HandleAsync(CreateAssetEmbeddingCompleted(1, 2), CancellationToken.None);

        redelivered.Should().Be(HandlerResult.AlreadyApplied);
        captured.Count(e => e.EventType == SagaEventType.DigestRequested).Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_AssetEmbeddingCompleted_StampsDigestRequestWithSeedingModel()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = CreateSagaWithSeedingModel("grok-4.3");
        saga.AddDocument(doc);
        saga.AddExpectedAssetEmbeddingUnits(1);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(CreateAssetEmbeddingCompleted(0, 1), CancellationToken.None);

        var digest = captured.Single(e => e.EventType == SagaEventType.DigestRequested);
        digest.Payload[PayloadKeys.AiModel].Should().Be("grok-4.3");
    }

    [Fact]
    public async Task HandleAsync_DigestCompleted_RecordsAndPublishesLandscapeRequest()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Seeded);
        var saga = CreateSaga(wantsSeeding: true, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.DocumentId] = "doc-1",
            ["empty"] = false,
            ["section_count"] = 4,
            [PayloadKeys.BriefId] = "brief-1"
        };
        var envelope = CreateEnvelope(SagaEventType.DigestCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Seeded);
        saga.State.Should().Be(BatchState.InProgress);
        saga.RecordedDigestCompletedDocumentIds.Should().Contain("doc-1");
        saga.RecordedLandscapeRequestedDocumentIds.Should().Contain("doc-1");
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());

        var landscape = captured.Single(e => e.EventType == SagaEventType.LandscapeRequested);
        landscape.DocumentId.Should().Be("doc-1");
        landscape.SchemaVersion.Should().Be("1.1");
        landscape.Payload[PayloadKeys.DocumentId].Should().Be("doc-1");
        landscape.Payload[PayloadKeys.BriefId].Should().Be("brief-1");
    }

    [Fact]
    public async Task HandleAsync_DigestCompleted_DoesNotStampModelOnLandscapeRequest()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Seeded);
        var saga = CreateSagaWithSeedingModel("grok-4.3");
        saga.AddDocument(doc);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.DocumentId] = "doc-1",
            [PayloadKeys.BriefId] = "brief-1"
        };
        var envelope = CreateEnvelope(SagaEventType.DigestCompleted, payload: payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);

        var landscape = captured.Single(e => e.EventType == SagaEventType.LandscapeRequested);
        landscape.Payload.Should().NotContainKey(PayloadKeys.AiModel);
    }

    [Fact]
    public async Task HandleAsync_DigestCompleted_Duplicate_IsAlreadyAppliedAndPublishesLandscapeOnce()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Seeded);
        var saga = CreateSaga(wantsSeeding: true, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var payload = new Dictionary<string, object> { [PayloadKeys.DocumentId] = "doc-1" };
        var first = CreateEnvelope(SagaEventType.DigestCompleted, payload: payload);
        var second = CreateEnvelope(SagaEventType.DigestCompleted, payload: payload);

        await _handler.HandleAsync(first, CancellationToken.None);
        var secondResult = await _handler.HandleAsync(second, CancellationToken.None);

        secondResult.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
        captured.Count(e => e.EventType == SagaEventType.LandscapeRequested).Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_LandscapeCompleted_RecordsWithoutStateTransitionOrPublish()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Seeded);
        var saga = CreateSaga(wantsSeeding: true, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.DocumentId] = "doc-1",
            [PayloadKeys.BriefId] = "brief-1"
        };
        var envelope = CreateEnvelope(SagaEventType.LandscapeCompleted, payload: payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Seeded);
        saga.State.Should().Be(BatchState.InProgress);
        saga.RecordedLandscapeCompletedDocumentIds.Should().Contain("doc-1");
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_LandscapeCompleted_Duplicate_IsAlreadyApplied()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Seeded);
        var saga = CreateSaga(wantsSeeding: true, documents: [doc]);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.DocumentId] = "doc-1" };
        var first = CreateEnvelope(SagaEventType.LandscapeCompleted, payload: payload);
        var second = CreateEnvelope(SagaEventType.LandscapeCompleted, payload: payload);

        await _handler.HandleAsync(first, CancellationToken.None);
        var secondResult = await _handler.HandleAsync(second, CancellationToken.None);

        secondResult.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    private static EventEnvelope CreateAssetEmbeddingCompleted(int unitIndex, int unitCount)
    {
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.UnitIndex] = unitIndex,
            [PayloadKeys.UnitCount] = unitCount
        };

        return CreateEnvelope(SagaEventType.AssetEmbeddingCompleted, payload: payload);
    }

    private static BatchSaga CreateSagaWithSeedingModel(string seedingModel)
    {
        return new BatchSaga(
            "batch-1",
            BatchState.InProgress,
            [],
            wantsHarvesting: false,
            wantsSeeding: true,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1,
            metadata: new BatchMetadata(SeedingModel: seedingModel));
    }
}
