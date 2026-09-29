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

public sealed class ServiceBusBatchDeleterTests
{
    private const string BatchId = "batch-xyz789";

    private static ServiceBusBatchDeleter BuildDeleter(ServiceBusSettings settings, out ServiceBusClient client)
    {
        client = Substitute.For<ServiceBusClient>();
        var logger = Substitute.For<ILogger<ServiceBusBatchDeleter>>();
        return new ServiceBusBatchDeleter(client, Options.Create(settings), logger);
    }

    private static ServiceBusSettings BuildSettings() => new()
    {
        NamespaceFqdn = "test.servicebus.windows.net",
        SubscriptionName = "orchestrator",
        WorkerSubscriptions = new Dictionary<string, string>
        {
            ["worker-1"] = "worker-1",
            ["worker-2"] = "worker-2"
        },
        DrainDeadLetters = true,
        CandidateSessionDrainBudgetSeconds = 60,
        TopicNames = new Dictionary<string, string>
        {
            ["ingestion.requested"] = "ingestion-requested",
            ["extraction.requested"] = "extraction-requested",
            ["evidence.requested"] = "evidence-requested",
            ["scoring.requested"] = "scoring-requested"
        }
    };

    private static DeleteBatchContext BuildContext() => new(
        BatchId,
        new DeletionRequestOptions("operator-1", "corr-1", Force: false),
        new BatchAccess("org-1", IsSuperAdmin: false));

    [Fact]
    public void StoreName_ReturnsServiceBusLiteral()
    {
        var deleter = BuildDeleter(BuildSettings(), out _);

        deleter.StoreName.Should().Be("ServiceBus");
    }

    [Fact]
    public async Task DeleteAsync_ReturnsSuccessWhenAllTargetsDrained()
    {
        var settings = BuildSettings();
        var deleter = BuildDeleter(settings, out var client);

        var result = await deleter.DeleteAsync(BatchId, BuildContext(), CancellationToken.None);

        result.StoreName.Should().Be("ServiceBus");
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_DrainsCandidateScopedSessions_RespectsWallClockBudget()
    {
        var settings = BuildSettings();
        settings.CandidateSessionDrainBudgetSeconds = 1;
        settings.TopicNames["evidence.requested"] = "evidence-requested";

        var deleter = BuildDeleter(settings, out var client);

        var sessionAcceptCount = 0;
        client.AcceptNextSessionAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            options: Arg.Any<ServiceBusSessionReceiverOptions>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                sessionAcceptCount++;
                await Task.Delay(200);
                var receiver = Substitute.For<ServiceBusSessionReceiver>();
                receiver.SessionId.Returns($"session-{sessionAcceptCount}");
                receiver.ReceiveMessagesAsync(
                    Arg.Any<int>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(Array.Empty<ServiceBusReceivedMessage>()));
                return receiver;
            });

        var result = await deleter.DeleteAsync(BatchId, BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        sessionAcceptCount.Should().BeLessThan(500);
    }

    [Fact]
    public async Task DeleteAsync_DrainsDLQForEveryTopicAndSubscription()
    {
        var settings = BuildSettings();
        settings.DrainDeadLetters = true;

        var deleter = BuildDeleter(settings, out var client);

        var dlqReceiverCount = 0;
        client.CreateReceiver(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<ServiceBusReceiverOptions>(o => o.SubQueue == SubQueue.DeadLetter))
            .Returns(_ =>
            {
                dlqReceiverCount++;
                var receiver = Substitute.For<ServiceBusReceiver>();
                receiver.ReceiveMessagesAsync(
                    Arg.Any<int>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(Array.Empty<ServiceBusReceivedMessage>()));
                return receiver;
            });

        await deleter.DeleteAsync(BatchId, BuildContext(), CancellationToken.None);

        dlqReceiverCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DrainDeadLetterReceiverAsync_MatchesBatchId_CompletesMessage()
    {
        var receiver = Substitute.For<ServiceBusReceiver>();
        var message = BuildMessage(BatchId, "doc-1");

        receiver.ReceiveMessagesAsync(
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(new[] { message }),
                Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(Array.Empty<ServiceBusReceivedMessage>()));

        var count = await ServiceBusBatchDeleter.DrainDeadLetterReceiverAsync(receiver, BatchId, CancellationToken.None);

        count.Should().Be(1);
        await receiver.Received(1).CompleteMessageAsync(message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainDeadLetterReceiverAsync_DoesNotMatchBatchId_AbandonsMessage()
    {
        var receiver = Substitute.For<ServiceBusReceiver>();
        var message = BuildMessage("other-batch", "doc-1");

        receiver.ReceiveMessagesAsync(
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(new[] { message }),
                Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(Array.Empty<ServiceBusReceivedMessage>()));

        var count = await ServiceBusBatchDeleter.DrainDeadLetterReceiverAsync(receiver, BatchId, CancellationToken.None);

        count.Should().Be(0);
        await receiver.Received(1).AbandonMessageAsync(message, cancellationToken: Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDeadLetterBatchAsync_DuplicateSequenceNumber_SkipsMessage()
    {
        var receiver = Substitute.For<ServiceBusReceiver>();
        var message1 = BuildMessage(BatchId, "doc-1", sequenceNumber: 100);
        var message2 = BuildMessage(BatchId, "doc-2", sequenceNumber: 100);

        var state = new ServiceBusBatchDeleter.DeadLetterDrainState(BatchId, new HashSet<long>());
        var messages = new[] { message1, message2 };

        var (matched, sawUnseen) = await ServiceBusBatchDeleter.ProcessDeadLetterBatchAsync(
            receiver,
            messages,
            state,
            CancellationToken.None);

        matched.Should().Be(1);
        sawUnseen.Should().BeTrue();
        state.SeenSequenceNumbers.Should().HaveCount(1);
        await receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDeadLetterBatchAsync_AllMessagesSeenBefore_ReturnsFalseForSawUnseenMessage()
    {
        var receiver = Substitute.For<ServiceBusReceiver>();
        var message = BuildMessage(BatchId, "doc-1", sequenceNumber: 200);

        var state = new ServiceBusBatchDeleter.DeadLetterDrainState(BatchId, new HashSet<long> { 200 });
        var messages = new[] { message };

        var (matched, sawUnseen) = await ServiceBusBatchDeleter.ProcessDeadLetterBatchAsync(
            receiver,
            messages,
            state,
            CancellationToken.None);

        matched.Should().Be(0);
        sawUnseen.Should().BeFalse();
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainCandidateScopedSessionsAsync_NoSessionsAvailable_ReturnsZero()
    {
        var settings = BuildSettings();
        settings.TopicNames["evidence.requested"] = "evidence-requested";

        var deleter = BuildDeleter(settings, out var client);

        client.AcceptNextSessionAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            options: Arg.Any<ServiceBusSessionReceiverOptions>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns<ServiceBusSessionReceiver>(_ =>
                throw new ServiceBusException("timeout", ServiceBusFailureReason.ServiceTimeout));

        var result = await deleter.DeleteAsync(BatchId, BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(0);
    }

    private static ServiceBusReceivedMessage BuildMessage(
        string batchId,
        string documentId,
        long sequenceNumber = 1)
    {
        var messageBody = BinaryData.FromString("{}");
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: messageBody,
            messageId: Guid.NewGuid().ToString(),
            sequenceNumber: sequenceNumber,
            properties: new Dictionary<string, object>
            {
                ["batch_id"] = batchId,
                ["document_id"] = documentId
            });

        return message;
    }
}
