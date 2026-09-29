using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class AdvanceSagaHandlerSeededValidationTests
{
    private readonly ISagaRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly AdvanceSagaHandler _handler;

    public AdvanceSagaHandlerSeededValidationTests()
    {
        _repository = Substitute.For<ISagaRepository>();
        _publisher = Substitute.For<IEventPublisher>();
        _handler = new AdvanceSagaHandler(_repository, _publisher, NullLogger<AdvanceSagaHandler>.Instance);
    }

    private static BatchSaga CreateSeedingSaga(
        List<DocumentProgress> documents,
        string? primaryEvidenceModel = null,
        string? scoringModel = null,
        string? seedingModel = null)
    {
        var metadata = primaryEvidenceModel is null && scoringModel is null && seedingModel is null
            ? null
            : new BatchMetadata(
                PrimaryEvidenceModel: primaryEvidenceModel,
                ScoringModel: scoringModel,
                SeedingModel: seedingModel);

        return new BatchSaga(
            "batch-1",
            BatchState.InProgress,
            documents,
            wantsHarvesting: false,
            wantsSeeding: true,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1,
            metadata: metadata);
    }

    private static EventEnvelope CreateEnvelope(
        string eventType,
        Dictionary<string, object>? payload = null,
        string documentId = "doc-1")
    {
        return new EventEnvelope
        {
            EventType = eventType,
            BatchId = "batch-1",
            DocumentId = documentId,
            Payload = payload ?? []
        };
    }

    private static Dictionary<string, object> IdeationPayload(params string[] candidateIds)
    {
        var json = JsonSerializer.Serialize(candidateIds);
        return new Dictionary<string, object>
        {
            [PayloadKeys.DocumentId] = "doc-1",
            [PayloadKeys.CandidateIds] = JsonDocument.Parse(json).RootElement,
            [PayloadKeys.CandidateCount] = candidateIds.Length,
            [PayloadKeys.JobId] = "job-seed"
        };
    }

    private static DocumentProgress SeededDocument(DocumentState state, params string[] seededIds)
    {
        var doc = new DocumentProgress("doc-1", state);
        doc.TryRecordSeededCandidates(seededIds);
        return doc;
    }

    private List<EventEnvelope> CapturePublished()
    {
        var captured = new List<EventEnvelope>();
        _publisher.PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return captured;
    }

    [Fact]
    public async Task IdeationCompleted_RecordsSeededIdsAndFansOutEvidencePerCandidate()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSeedingSaga([doc], primaryEvidenceModel: "gpt-5.4-evidence");
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();
        var envelope = CreateEnvelope(SagaEventType.IdeationCompleted, IdeationPayload("seed-1", "seed-2", "seed-3"));

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.SeededCandidateIds.Should().BeEquivalentTo("seed-1", "seed-2", "seed-3");
        doc.State.Should().Be(DocumentState.Scored);

        var evidence = captured.Where(e => e.EventType == SagaEventType.EvidenceRequested).ToList();
        evidence.Should().HaveCount(3);
        evidence.Select(e => (string)e.Payload[PayloadKeys.CandidateId]).Should().BeEquivalentTo("seed-1", "seed-2", "seed-3");
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdeationCompleted_StampsEvidenceModelOnEveryFannedOutRequest()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSeedingSaga([doc], primaryEvidenceModel: "gpt-5.4-evidence");
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();
        var envelope = CreateEnvelope(SagaEventType.IdeationCompleted, IdeationPayload("seed-1", "seed-2"));

        await _handler.HandleAsync(envelope, CancellationToken.None);

        var evidence = captured.Where(e => e.EventType == SagaEventType.EvidenceRequested).ToList();
        evidence.Should().HaveCount(2);
        evidence.Should().OnlyContain(e => e.Payload.ContainsKey(PayloadKeys.AiModel)
            && e.Payload[PayloadKeys.AiModel].Equals("gpt-5.4-evidence"));
    }

    [Fact]
    public async Task IdeationCompleted_Redelivered_IsIdempotentAndDoesNotDoubleFanOut()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSeedingSaga([doc]);
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();
        var envelope = CreateEnvelope(SagaEventType.IdeationCompleted, IdeationPayload("seed-1", "seed-2"));

        await _handler.HandleAsync(envelope, CancellationToken.None);
        var second = await _handler.HandleAsync(envelope, CancellationToken.None);

        second.Should().Be(HandlerResult.AlreadyApplied);
        captured.Count(e => e.EventType == SagaEventType.EvidenceRequested).Should().Be(2);
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdeationCompleted_ZeroSeededCandidates_FailsDocument()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Scored);
        var saga = CreateSeedingSaga([doc]);
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IdeationCompleted, IdeationPayload());

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Contain("no seeded candidates");
        saga.State.Should().Be(BatchState.Failed);
        await _publisher.DidNotReceive().PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.EvidenceRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeededEvidenceCompleted_EmitsScoringRequestedWithoutTouchingExtractionCountersOrCoverage()
    {
        var doc = SeededDocument(DocumentState.Scored, "seed-1", "seed-2");
        var saga = CreateSeedingSaga([doc], scoringModel: "gpt-5.5-scoring");
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "seed-1",
            [PayloadKeys.EvidenceBundleId] = "bundle-1",
            [PayloadKeys.JobId] = "job-seed",
            ["source_coverage"] = JsonDocument.Parse(
                "{\"has_patent_api\": true, \"has_corpus\": true, \"has_llm\": true}").RootElement
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceCompleted, payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        var scoring = captured.Single(e => e.EventType == SagaEventType.ScoringRequested);
        scoring.Payload[PayloadKeys.CandidateId].Should().Be("seed-1");
        scoring.Payload[PayloadKeys.AiModel].Should().Be("gpt-5.5-scoring");
        doc.CompletedCandidateIds.Should().BeEmpty();
        saga.EvidenceCompletedCount.Should().Be(0);
        saga.EvidenceSourceLiveCounts.Should().BeEmpty();
    }

    [Fact]
    public async Task SeededScoringCompleted_LastCandidate_PublishesSeedingReportRequestedStamped()
    {
        var doc = SeededDocument(DocumentState.Scored, "seed-1", "seed-2");
        doc.MarkSeededScored("seed-1");
        var saga = CreateSeedingSaga([doc], seedingModel: "grok-4.3");
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();
        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "seed-2" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.CompletedSeededCandidateIds.Should().Contain("seed-2");
        doc.CompletedCandidateIds.Should().BeEmpty();

        var report = captured.Single(e => e.EventType == SagaEventType.SeedingReportRequested);
        report.Payload[PayloadKeys.DocumentId].Should().Be("doc-1");
        report.Payload[PayloadKeys.ReportTrigger].Should().Be("validation");
        report.Payload[PayloadKeys.AiModel].Should().Be("grok-4.3");

        await _publisher.DidNotReceive().PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.SeedingRequested
                || e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeededScoringCompleted_NotLastCandidate_DoesNotPublishReport()
    {
        var doc = SeededDocument(DocumentState.Scored, "seed-1", "seed-2");
        var saga = CreateSeedingSaga([doc]);
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();
        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "seed-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Scored);
        captured.Should().NotContain(e => e.EventType == SagaEventType.SeedingReportRequested);
    }

    [Fact]
    public async Task SeededScoringCompleted_LastCandidateRedelivered_PublishesReportOnce()
    {
        var doc = SeededDocument(DocumentState.Scored, "seed-1");
        var saga = CreateSeedingSaga([doc], seedingModel: "grok-4.3");
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();
        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "seed-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload);

        await _handler.HandleAsync(envelope, CancellationToken.None);
        var second = await _handler.HandleAsync(envelope, CancellationToken.None);

        second.Should().Be(HandlerResult.AlreadyApplied);
        captured.Count(e => e.EventType == SagaEventType.SeedingReportRequested).Should().Be(1);
    }

    [Fact]
    public async Task SeededEvidenceFailed_WithZeroExtractionCandidates_DoesNotFailWholeDocumentPrematurely()
    {
        var doc = SeededDocument(DocumentState.Scored, "seed-1", "seed-2");
        doc.ExpectedCandidateCount.Should().Be(0);
        var saga = CreateSeedingSaga([doc]);
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "seed-1",
            [PayloadKeys.JobId] = "job-seed"
        };
        var envelope = CreateEnvelope(SagaEventType.EvidenceFailed, payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Scored);
        doc.FailedSeededCandidateIds.Should().Contain("seed-1");
        doc.FailedCandidateIds.Should().BeEmpty();
    }

    [Fact]
    public async Task SeededValidation_AllFail_FailsDocumentWithSeedingSemantics()
    {
        var doc = SeededDocument(DocumentState.Scored, "seed-1");
        var saga = CreateSeedingSaga([doc]);
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "seed-1",
            ["reason"] = "Scoring rejected seeded candidate"
        };
        var envelope = CreateEnvelope(SagaEventType.ScoringFailed, payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.State.Should().Be(DocumentState.Failed);
        doc.FailureReason.Should().Contain("All seeded candidates failed");
        saga.State.Should().Be(BatchState.Failed);
        captured.Should().NotContain(e => e.EventType == SagaEventType.SeedingReportRequested);
    }

    [Fact]
    public async Task SeededValidation_OneSuccessOneFailure_PublishesReport()
    {
        var doc = SeededDocument(DocumentState.Scored, "seed-1", "seed-2");
        var saga = CreateSeedingSaga([doc], seedingModel: "grok-4.3");
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var captured = CapturePublished();

        var failPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "seed-1",
            [PayloadKeys.JobId] = "job-seed"
        };
        await _handler.HandleAsync(CreateEnvelope(SagaEventType.EvidenceFailed, failPayload), CancellationToken.None);

        var scorePayload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "seed-2" };
        await _handler.HandleAsync(CreateEnvelope(SagaEventType.ScoringCompleted, scorePayload), CancellationToken.None);

        doc.State.Should().Be(DocumentState.Scored);
        captured.Count(e => e.EventType == SagaEventType.SeedingReportRequested).Should().Be(1);
    }

    [Fact]
    public async Task RegressionHarvestingOnly_ScoringCompleted_StillPublishesHarvestingRequested()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = new BatchSaga(
            "batch-1",
            BatchState.InProgress,
            [doc],
            wantsHarvesting: true,
            wantsSeeding: false,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1);
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.CompletedCandidateIds.Should().Contain("cand-1");
        doc.CompletedSeededCandidateIds.Should().BeEmpty();
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegressionLegacySeeding_ScoringCompleted_StillPublishesSeedingRequested()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(1);
        var saga = CreateSeedingSaga([doc]);
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var envelope = CreateEnvelope(SagaEventType.ScoringCompleted, payload);

        var result = await _handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().Be(HandlerResult.Applied);
        doc.CompletedCandidateIds.Should().Contain("cand-1");
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.SeedingRequested),
            Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.SeedingReportRequested),
            Arg.Any<CancellationToken>());
    }
}
