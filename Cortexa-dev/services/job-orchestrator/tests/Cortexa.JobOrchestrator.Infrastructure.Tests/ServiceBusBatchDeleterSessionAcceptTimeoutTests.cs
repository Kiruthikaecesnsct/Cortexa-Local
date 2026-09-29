using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests;

public sealed class ServiceBusBatchDeleterSessionAcceptTimeoutTests
{
    private readonly ServiceBusClient _client = Substitute.For<ServiceBusClient>();
    private readonly ILogger<ServiceBusBatchDeleter> _logger = Substitute.For<ILogger<ServiceBusBatchDeleter>>();

    private const string BatchId = "batch-123";
    private const int SessionAcceptTimeoutSeconds = 2;
    private const int CandidateSessionDrainBudgetSeconds = 8;

    private ServiceBusSettings BuildSettings(int? acceptTimeoutSeconds = null, int? drainBudgetSeconds = null) =>
        new()
        {
            NamespaceFqdn = "cortexa-dev-sb.servicebus.windows.net",
            SubscriptionName = "orchestrator",
            TopicNames = new Dictionary<string, string>
            {
                { "ingestion.requested", "ingestion-requested" },
                { "evidence.candidate-scoped", "evidence-candidate-scoped" }
            },
            WorkerSubscriptions = new Dictionary<string, string>(),
            DrainDeadLetters = true,
            SessionAcceptTimeoutSeconds = acceptTimeoutSeconds ?? SessionAcceptTimeoutSeconds,
            CandidateSessionDrainBudgetSeconds = drainBudgetSeconds ?? CandidateSessionDrainBudgetSeconds
        };

    private ServiceBusBatchDeleter BuildDeleter(ServiceBusSettings? settings = null) =>
        new(_client, Options.Create(settings ?? BuildSettings()), _logger);

    [Fact]
    public async Task DeleteAsync_ReadsSessionAcceptTimeoutFromSettings()
    {
        const int CustomAcceptTimeoutSeconds = 5;
        var settings = BuildSettings(acceptTimeoutSeconds: CustomAcceptTimeoutSeconds);

        var receiver = Substitute.For<ServiceBusSessionReceiver>();
        receiver.SessionId.Returns(BatchId);

        var acceptCts = new CancellationTokenSource();
        var acceptDeadline = TimeSpan.Zero;
        var startTime = DateTime.UtcNow;

        _client.AcceptSessionAsync(
            "ingestion-requested",
            "orchestrator",
            BatchId,
            null,
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(CustomAcceptTimeoutSeconds + 2), ct);
                }
                catch (OperationCanceledException)
                {
                    acceptDeadline = DateTime.UtcNow - startTime;
                    throw;
                }

                return receiver;
            });

        var context = new DeleteBatchContext(
            BatchId,
            new DeletionRequestOptions("operator-1", "corr-1", false),
            BatchAccess.Unrestricted);

        var deleter = BuildDeleter(settings);

        var result = await deleter.DeleteAsync(BatchId, context, CancellationToken.None);

        acceptDeadline.TotalSeconds.Should().BeInRange(CustomAcceptTimeoutSeconds - 1, CustomAcceptTimeoutSeconds + 2);
    }

    [Fact]
    public async Task DeleteAsync_CandidateSessionDrainBudgetSeconds_ReadFromSettings()
    {
        const int CustomDrainBudgetSeconds = 12;
        var settings = BuildSettings(drainBudgetSeconds: CustomDrainBudgetSeconds);

        var context = new DeleteBatchContext(
            BatchId,
            new DeletionRequestOptions("operator-1", "corr-1", false),
            BatchAccess.Unrestricted);

        var deleter = BuildDeleter(settings);

        var result = await deleter.DeleteAsync(BatchId, context, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_BatchScopedSession_UsesSessionAcceptTimeoutSeconds()
    {
        var settings = BuildSettings();

        var receiver = Substitute.For<ServiceBusSessionReceiver>();
        receiver.SessionId.Returns(BatchId);
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ServiceBusReceivedMessage>());

        var acceptInvoked = false;
        _client.AcceptSessionAsync(
            "ingestion-requested",
            "orchestrator",
            BatchId,
            null,
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                acceptInvoked = true;
                return Task.FromResult(receiver);
            });

        var context = new DeleteBatchContext(
            BatchId,
            new DeletionRequestOptions("operator-1", "corr-1", false),
            BatchAccess.Unrestricted);

        var deleter = BuildDeleter(settings);

        await deleter.DeleteAsync(BatchId, context, CancellationToken.None);

        acceptInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_CandidateScopedSession_UsesSessionAcceptTimeoutSeconds()
    {
        var settings = BuildSettings();

        _client.AcceptNextSessionAsync(
            "evidence-candidate-scoped",
            "orchestrator",
            null,
            Arg.Any<CancellationToken>())
            .Returns<ServiceBusSessionReceiver>(_ => throw new ServiceBusException(
                "Timeout",
                ServiceBusFailureReason.ServiceTimeout));

        var context = new DeleteBatchContext(
            BatchId,
            new DeletionRequestOptions("operator-1", "corr-1", false),
            BatchAccess.Unrestricted);

        var deleter = BuildDeleter(settings);

        var result = await deleter.DeleteAsync(BatchId, context, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DrainDeadLetterReceiverAsync_DedupStillWorks()
    {
        var settings = BuildSettings();

        var messages = new List<ServiceBusReceivedMessage>
        {
            BuildMessage(BatchId, sequenceNumber: 1),
            BuildMessage(BatchId, sequenceNumber: 2),
            BuildMessage(BatchId, sequenceNumber: 2),
            BuildMessage(BatchId, sequenceNumber: 3)
        };

        var receiver = Substitute.For<ServiceBusReceiver>();
        var callCount = 0;
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return callCount == 1 ? messages : new List<ServiceBusReceivedMessage>();
            });

        receiver.CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var count = await ServiceBusBatchDeleter.DrainDeadLetterReceiverAsync(receiver, BatchId, CancellationToken.None);

        count.Should().Be(3);
        await receiver.Received(3).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDeadLetterBatchAsync_BatchIdMismatch_Abandons()
    {
        var messages = new List<ServiceBusReceivedMessage>
        {
            BuildMessage("other-batch", sequenceNumber: 1),
            BuildMessage(BatchId, sequenceNumber: 2)
        };

        var receiver = Substitute.For<ServiceBusReceiver>();
        receiver.CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        receiver.AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var state = new ServiceBusBatchDeleter.DeadLetterDrainState(BatchId, new HashSet<long>());

        var (matched, sawUnseenMessage) = await ServiceBusBatchDeleter.ProcessDeadLetterBatchAsync(
            receiver,
            messages,
            state,
            CancellationToken.None);

        matched.Should().Be(1);
        sawUnseenMessage.Should().BeTrue();
        await receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
    }

    private static ServiceBusReceivedMessage BuildMessage(string batchId, long sequenceNumber)
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString(),
            sequenceNumber: sequenceNumber,
            properties: new Dictionary<string, object>
            {
                { "batch_id", batchId }
            });

        return message;
    }
}
