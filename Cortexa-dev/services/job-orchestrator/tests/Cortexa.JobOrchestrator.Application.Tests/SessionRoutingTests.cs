using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class SessionRoutingTests
{
    private static IOptions<ServiceBusSettings> BuildSettings(string subscriptionName = "orchestrator") =>
        Options.Create(new ServiceBusSettings
        {
            NamespaceFqdn = "test.servicebus.windows.net",
            SubscriptionName = subscriptionName,
            TopicNames = new Dictionary<string, string>
            {
                [SagaEventType.ExtractionRequested] = "extraction-requested"
            }
        });

    private static EventEnvelope CreateEnvelope(
        string eventType,
        string batchId = "batch-abc",
        string documentId = "doc-1",
        string? correlationId = null,
        Dictionary<string, object>? payload = null) =>
        new()
        {
            EventType = eventType,
            BatchId = batchId,
            DocumentId = documentId,
            CorrelationId = correlationId,
            Payload = payload ?? []
        };

    private static BatchSaga CreateSaga(
        string batchId = "batch-abc",
        List<DocumentProgress>? documents = null) =>
        new(
            batchId,
            BatchState.InProgress,
            documents ?? [],
            wantsHarvesting: true,
            wantsSeeding: false,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1);

    [Fact]
    public async Task Publisher_SetsSessionId_EqualToBatchId()
    {
        var capturedMessages = new List<ServiceBusMessage>();

        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Do<ServiceBusMessage>(m => capturedMessages.Add(m)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);

        var publisher = new ServiceBusEventPublisher(client, BuildSettings());
        var envelope = CreateEnvelope(SagaEventType.ExtractionRequested, batchId: "batch-xyz");

        await publisher.PublishAsync(envelope, CancellationToken.None);

        capturedMessages.Should().ContainSingle();
        capturedMessages[0].SessionId.Should().Be("batch-xyz");
    }

    [Fact]
    public async Task Publisher_CandidateScopedEventType_SetsSessionIdAndApplicationProperties()
    {
        const string BatchId = "batch-xyz";
        const string CandidateId = "cand-1";
        var capturedMessages = new List<ServiceBusMessage>();

        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Do<ServiceBusMessage>(m => capturedMessages.Add(m)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);

        var publisher = new ServiceBusEventPublisher(client, BuildSettings());
        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = CandidateId };
        var envelope = CreateEnvelope(SagaEventType.EvidenceRequested, batchId: BatchId, payload: payload);

        await publisher.PublishAsync(envelope, CancellationToken.None);

        capturedMessages.Should().ContainSingle();
        capturedMessages[0].SessionId.Should().Be($"{BatchId}:{CandidateId}");
        capturedMessages[0].ApplicationProperties[SessionKeyResolver.BatchIdProperty].Should().Be(BatchId);
        capturedMessages[0].ApplicationProperties[SessionKeyResolver.CandidateIdProperty].Should().Be(CandidateId);
    }

    [Fact]
    public async Task Publisher_BatchScopedEventType_SetsOnlyBatchIdApplicationProperty()
    {
        const string BatchId = "batch-xyz";
        var capturedMessages = new List<ServiceBusMessage>();

        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Do<ServiceBusMessage>(m => capturedMessages.Add(m)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);

        var publisher = new ServiceBusEventPublisher(client, BuildSettings());
        var envelope = CreateEnvelope(SagaEventType.ExtractionRequested, batchId: BatchId);

        await publisher.PublishAsync(envelope, CancellationToken.None);

        capturedMessages.Should().ContainSingle();
        capturedMessages[0].SessionId.Should().Be(BatchId);
        capturedMessages[0].ApplicationProperties[SessionKeyResolver.BatchIdProperty].Should().Be(BatchId);
        capturedMessages[0].ApplicationProperties.Should().NotContainKey(SessionKeyResolver.CandidateIdProperty);
    }

    [Fact]
    public async Task PublishScheduledAsync_CandidateScopedSessionId_StampsBatchIdFromSessionIdPrefix()
    {
        const string BatchId = "batch-xyz";
        const string CandidateId = "cand-1";
        var capturedMessages = new List<ServiceBusMessage>();

        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Do<ServiceBusMessage>(m => capturedMessages.Add(m)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);

        var publisher = new ServiceBusEventPublisher(client, BuildSettings());

        await publisher.PublishScheduledAsync(
            "extraction-requested",
            "{}",
            DateTimeOffset.UtcNow,
            retryAttempt: 1,
            sessionId: $"{BatchId}:{CandidateId}",
            CancellationToken.None);

        capturedMessages.Should().ContainSingle();
        capturedMessages[0].SessionId.Should().Be($"{BatchId}:{CandidateId}");
        capturedMessages[0].ApplicationProperties[SessionKeyResolver.BatchIdProperty].Should().Be(BatchId);
    }

    [Fact]
    public async Task PublishScheduledAsync_NullSessionId_DoesNotSetSessionIdOrBatchIdProperty()
    {
        var capturedMessages = new List<ServiceBusMessage>();

        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Do<ServiceBusMessage>(m => capturedMessages.Add(m)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);

        var publisher = new ServiceBusEventPublisher(client, BuildSettings());

        await publisher.PublishScheduledAsync(
            "extraction-requested",
            "{}",
            DateTimeOffset.UtcNow,
            retryAttempt: 1,
            sessionId: null,
            CancellationToken.None);

        capturedMessages.Should().ContainSingle();
        capturedMessages[0].SessionId.Should().BeNull();
        capturedMessages[0].ApplicationProperties.Should().NotContainKey(SessionKeyResolver.BatchIdProperty);
    }

    [Fact]
    public async Task BuildNextEnvelope_PreservesNonNullCorrelationId()
    {
        var repository = Substitute.For<ISagaRepository>();
        var publisher = Substitute.For<IEventPublisher>();
        var handler = new AdvanceSagaHandler(repository, publisher, NullLogger<AdvanceSagaHandler>.Instance);

        var captured = new List<EventEnvelope>();
        publisher
            .PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);
        repository.GetAsync("batch-abc", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, correlationId: "corr-original");

        await handler.HandleAsync(envelope, CancellationToken.None);

        captured.Should().ContainSingle();
        captured[0].CorrelationId.Should().Be("corr-original");
    }

    [Fact]
    public async Task BuildNextEnvelope_DefaultsCorrelationIdToBatchId_WhenSourceCorrelationIdIsNull()
    {
        var repository = Substitute.For<ISagaRepository>();
        var publisher = Substitute.For<IEventPublisher>();
        var handler = new AdvanceSagaHandler(repository, publisher, NullLogger<AdvanceSagaHandler>.Instance);

        var captured = new List<EventEnvelope>();
        publisher
            .PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);
        repository.GetAsync("batch-abc", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, correlationId: null);

        await handler.HandleAsync(envelope, CancellationToken.None);

        captured.Should().ContainSingle();
        captured[0].CorrelationId.Should().Be("batch-abc");
    }

    [Fact]
    public async Task BuildNextEnvelope_DefaultsCorrelationIdToBatchId_WhenSourceCorrelationIdIsEmpty()
    {
        var repository = Substitute.For<ISagaRepository>();
        var publisher = Substitute.For<IEventPublisher>();
        var handler = new AdvanceSagaHandler(repository, publisher, NullLogger<AdvanceSagaHandler>.Instance);

        var captured = new List<EventEnvelope>();
        publisher
            .PublishAsync(Arg.Do<EventEnvelope>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = CreateSaga(documents: [doc]);
        repository.GetAsync("batch-abc", Arg.Any<CancellationToken>()).Returns(saga);

        var envelope = CreateEnvelope(SagaEventType.IngestionCompleted, correlationId: string.Empty);

        await handler.HandleAsync(envelope, CancellationToken.None);

        captured.Should().ContainSingle();
        captured[0].CorrelationId.Should().Be("batch-abc");
    }

    [Fact]
    public async Task FullDriveSequence_FiveEvents_RoutesToCorrectTopicsInOrder()
    {
        var repository = Substitute.For<ISagaRepository>();
        var publisher = Substitute.For<IEventPublisher>();
        var handler = new AdvanceSagaHandler(repository, publisher, NullLogger<AdvanceSagaHandler>.Instance);

        var publishedEvents = new List<string>();
        publisher
            .PublishAsync(Arg.Do<EventEnvelope>(e => publishedEvents.Add(e.EventType)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var batchId = "batch-seq";

        repository
            .GetAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var doc = new DocumentProgress("doc-1", DocumentState.Queued);
                return CreateSaga(batchId, [doc]);
            });

        var ingestionCompleted = CreateEnvelope(SagaEventType.IngestionCompleted, batchId);
        await handler.HandleAsync(ingestionCompleted, CancellationToken.None);
        publishedEvents.Last().Should().Be(SagaEventType.ExtractionRequested);

        repository
            .GetAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
                return CreateSaga(batchId, [doc]);
            });

        var extractionPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateIds] = System.Text.Json.JsonDocument.Parse("[\"cand-1\"]").RootElement,
            [PayloadKeys.JobId] = "job-123"
        };
        var extractionCompleted = CreateEnvelope(SagaEventType.ExtractionCompleted, batchId, payload: extractionPayload);
        await handler.HandleAsync(extractionCompleted, CancellationToken.None);
        publishedEvents.Last().Should().Be(SagaEventType.EvidenceRequested);

        repository
            .GetAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
                doc.SetExpectedCandidates(1);
                return CreateSaga(batchId, [doc]);
            });

        var evidencePayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1",
            [PayloadKeys.EvidenceBundleId] = "eb-456",
            [PayloadKeys.JobId] = "job-123"
        };
        var evidenceCompleted = CreateEnvelope(SagaEventType.EvidenceCompleted, batchId, payload: evidencePayload);
        await handler.HandleAsync(evidenceCompleted, CancellationToken.None);
        publishedEvents.Last().Should().Be(SagaEventType.ScoringRequested);

        repository
            .GetAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
                doc.SetExpectedCandidates(1);
                return CreateSaga(batchId, [doc]);
            });

        var scoringPayload = new Dictionary<string, object>
        {
            [PayloadKeys.CandidateId] = "cand-1"
        };
        var scoringCompleted = CreateEnvelope(SagaEventType.ScoringCompleted, batchId, payload: scoringPayload);
        await handler.HandleAsync(scoringCompleted, CancellationToken.None);
        publishedEvents.Last().Should().Be(SagaEventType.HarvestingRequested);

        repository
            .GetAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var doc = new DocumentProgress("doc-1", DocumentState.Scored);
                return CreateSaga(batchId, [doc]);
            });

        var engineCompleted = CreateEnvelope(
            SagaEventType.EngineCompleted,
            batchId,
            payload: new Dictionary<string, object> { ["engine"] = "harvesting" });
        await handler.HandleAsync(engineCompleted, CancellationToken.None);

        publishedEvents.Should().Equal(
            SagaEventType.ExtractionRequested,
            SagaEventType.EvidenceRequested,
            SagaEventType.ScoringRequested,
            SagaEventType.HarvestingRequested);
    }
}
