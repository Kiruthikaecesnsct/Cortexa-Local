using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests.Messaging;

public sealed class ServiceBusBatchDeleterSessionDispatchTests
{
    private const string BatchId = "batch-42";
    private const string OtherBatchId = "batch-99";
    private const string OtherBatchSessionId = "batch-99:cand-x";
    private const string Subscription = "orchestrator";
    private const string CandidateScopedTopic = "evidence-requested-topic";
    private const string BatchScopedTopic = "ingestion-requested-topic";

    private static ServiceBusSettings BuildSettings() => new()
    {
        SubscriptionName = Subscription,
        DrainDeadLetters = false,
        TopicNames = new Dictionary<string, string>
        {
            [SagaEventType.EvidenceRequested] = CandidateScopedTopic,
            [SagaEventType.IngestionRequested] = BatchScopedTopic
        },
        WorkerSubscriptions = new Dictionary<string, string>()
    };

    private static DeleteBatchContext BuildContext() =>
        new(BatchId, new DeletionRequestOptions("operator", "corr-1", Force: false), BatchAccess.Unrestricted);

    [Fact]
    public async Task DeleteAsync_CandidateScopedTopic_RoutesThroughAcceptNextSessionAsync_NotAcceptSessionAsync()
    {
        var sessionReceiver = Substitute.For<ServiceBusSessionReceiver>();
        sessionReceiver
            .ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ServiceBusReceivedMessage>());

        var client = Substitute.For<ServiceBusClient>();
        client
            .AcceptNextSessionAsync(
                CandidateScopedTopic,
                Subscription,
                Arg.Any<ServiceBusSessionReceiverOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(sessionReceiver),
                _ => throw new ServiceBusException("no more sessions", ServiceBusFailureReason.ServiceTimeout));

        client
            .AcceptSessionAsync(
                BatchScopedTopic,
                Subscription,
                BatchId,
                Arg.Any<ServiceBusSessionReceiverOptions>(),
                Arg.Any<CancellationToken>())
            .Returns<ServiceBusSessionReceiver>(_ =>
                throw new ServiceBusException("no session", ServiceBusFailureReason.SessionCannotBeLocked));

        var deleter = new ServiceBusBatchDeleter(
            client,
            Options.Create(BuildSettings()),
            NullLogger<ServiceBusBatchDeleter>.Instance);

        var result = await deleter.DeleteAsync(BatchId, BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue();

        await client.Received(2).AcceptNextSessionAsync(
            CandidateScopedTopic,
            Subscription,
            Arg.Any<ServiceBusSessionReceiverOptions>(),
            Arg.Any<CancellationToken>());
        await client.DidNotReceive().AcceptSessionAsync(
            CandidateScopedTopic,
            Subscription,
            Arg.Any<string>(),
            Arg.Any<ServiceBusSessionReceiverOptions>(),
            Arg.Any<CancellationToken>());
        await sessionReceiver.Received(1).ReceiveMessagesAsync(
            Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_BatchScopedTopic_StillRoutesThroughAcceptSessionAsync_NotAcceptNextSessionAsync()
    {
        var sessionReceiver = Substitute.For<ServiceBusSessionReceiver>();
        sessionReceiver
            .ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ServiceBusReceivedMessage>());

        var client = Substitute.For<ServiceBusClient>();
        client
            .AcceptSessionAsync(
                BatchScopedTopic,
                Subscription,
                BatchId,
                Arg.Any<ServiceBusSessionReceiverOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(sessionReceiver));

        client
            .AcceptNextSessionAsync(
                CandidateScopedTopic,
                Subscription,
                Arg.Any<ServiceBusSessionReceiverOptions>(),
                Arg.Any<CancellationToken>())
            .Returns<ServiceBusSessionReceiver>(_ =>
                throw new ServiceBusException("no more sessions", ServiceBusFailureReason.ServiceTimeout));

        var deleter = new ServiceBusBatchDeleter(
            client,
            Options.Create(BuildSettings()),
            NullLogger<ServiceBusBatchDeleter>.Instance);

        var result = await deleter.DeleteAsync(BatchId, BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue();

        await client.Received(1).AcceptSessionAsync(
            BatchScopedTopic,
            Subscription,
            BatchId,
            Arg.Any<ServiceBusSessionReceiverOptions>(),
            Arg.Any<CancellationToken>());
        await client.DidNotReceive().AcceptNextSessionAsync(
            BatchScopedTopic,
            Subscription,
            Arg.Any<ServiceBusSessionReceiverOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_CandidateScopedTopic_RepeatedlyOffersSameOtherBatchSession_TerminatesWithoutPurgingIt()
    {
        var strayMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            sequenceNumber: 1L,
            properties: new Dictionary<string, object> { [SessionKeyResolver.BatchIdProperty] = OtherBatchId });

        var sessionReceiver = Substitute.For<ServiceBusSessionReceiver>();
        sessionReceiver.SessionId.Returns(OtherBatchSessionId);
        sessionReceiver
            .ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ServiceBusReceivedMessage> { strayMessage });

        var client = Substitute.For<ServiceBusClient>();
        client
            .AcceptNextSessionAsync(
                CandidateScopedTopic,
                Subscription,
                Arg.Any<ServiceBusSessionReceiverOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(sessionReceiver));

        var deleter = new ServiceBusBatchDeleter(
            client,
            Options.Create(BuildSettings()),
            NullLogger<ServiceBusBatchDeleter>.Instance);

        var result = await deleter.DeleteAsync(BatchId, BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(0);

        await client.Received(2).AcceptNextSessionAsync(
            CandidateScopedTopic,
            Subscription,
            Arg.Any<ServiceBusSessionReceiverOptions>(),
            Arg.Any<CancellationToken>());
        await sessionReceiver.Received(1).AbandonMessageAsync(strayMessage, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await sessionReceiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }
}
