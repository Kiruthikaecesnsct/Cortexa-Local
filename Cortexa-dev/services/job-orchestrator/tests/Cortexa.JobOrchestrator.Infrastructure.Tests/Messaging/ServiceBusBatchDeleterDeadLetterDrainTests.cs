using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests.Messaging;

public sealed class ServiceBusBatchDeleterDeadLetterDrainTests
{
    private const string BatchId = "batch-42";
    private const string OtherBatchId = "batch-99";

    private static ServiceBusReceivedMessage BuildMessage(long sequenceNumber, string batchId) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            sequenceNumber: sequenceNumber,
            properties: new Dictionary<string, object> { [SessionKeyResolver.BatchIdProperty] = batchId });

    private static ServiceBusReceiver BuildReceiver() => Substitute.For<ServiceBusReceiver>();

    [Fact]
    public async Task DrainDeadLetterReceiverAsync_OnlyStraySessionRedelivered_TerminatesAndReturnsZero()
    {
        const long StraySequenceNumber = 5001L;
        var strayMessage = BuildMessage(StraySequenceNumber, OtherBatchId);
        var strayBatch = new List<ServiceBusReceivedMessage> { strayMessage };
        var receiver = BuildReceiver();
        receiver
            .ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => strayBatch,
                _ => strayBatch,
                _ => throw new InvalidOperationException(
                    "DrainDeadLetterReceiverAsync did not terminate after the stray message was redelivered."));

        var count = await ServiceBusBatchDeleter.DrainDeadLetterReceiverAsync(receiver, BatchId, CancellationToken.None);

        count.Should().Be(0);
        await receiver.Received(2).ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(strayMessage, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDeadLetterBatchAsync_MixOfMatchingAndStrayMessages_CompletesMatchingAndAbandonsStray()
    {
        const long MatchingSequenceNumber = 6001L;
        const long StraySequenceNumber = 6002L;
        var matchingMessage = BuildMessage(MatchingSequenceNumber, BatchId);
        var strayMessage = BuildMessage(StraySequenceNumber, OtherBatchId);
        var messages = new List<ServiceBusReceivedMessage> { matchingMessage, strayMessage };
        var receiver = BuildReceiver();
        var state = new ServiceBusBatchDeleter.DeadLetterDrainState(BatchId, new HashSet<long>());

        var (matched, sawUnseenMessage) = await ServiceBusBatchDeleter.ProcessDeadLetterBatchAsync(
            receiver, messages, state, CancellationToken.None);

        matched.Should().Be(1);
        sawUnseenMessage.Should().BeTrue();
        await receiver.Received(1).CompleteMessageAsync(matchingMessage, Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(strayMessage, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(strayMessage, Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().AbandonMessageAsync(matchingMessage, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainDeadLetterReceiverAsync_MultipleBatchesUntilEmpty_ReturnsTotalMatchedCount()
    {
        const long FirstBatchSeqA = 7001L;
        const long FirstBatchSeqB = 7002L;
        const long SecondBatchSeq = 7003L;
        const int ExpectedTotalMatched = 3;
        var firstBatch = new List<ServiceBusReceivedMessage>
        {
            BuildMessage(FirstBatchSeqA, BatchId),
            BuildMessage(FirstBatchSeqB, BatchId),
        };
        var secondBatch = new List<ServiceBusReceivedMessage> { BuildMessage(SecondBatchSeq, BatchId) };
        var emptyBatch = Array.Empty<ServiceBusReceivedMessage>();
        var receiver = BuildReceiver();
        receiver
            .ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ => firstBatch, _ => secondBatch, _ => emptyBatch);

        var count = await ServiceBusBatchDeleter.DrainDeadLetterReceiverAsync(receiver, BatchId, CancellationToken.None);

        count.Should().Be(ExpectedTotalMatched);
        await receiver.Received(3).ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.Received(ExpectedTotalMatched).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainDeadLetterReceiverAsync_SeenStrayMixedWithUnseenMatch_ContinuesToNextReceive()
    {
        const long StraySequenceNumber = 8001L;
        const long MatchingSequenceNumber = 8002L;
        var strayMessage = BuildMessage(StraySequenceNumber, OtherBatchId);
        var matchingMessage = BuildMessage(MatchingSequenceNumber, BatchId);
        var firstBatch = new List<ServiceBusReceivedMessage> { strayMessage };
        var secondBatch = new List<ServiceBusReceivedMessage> { strayMessage, matchingMessage };
        var emptyBatch = Array.Empty<ServiceBusReceivedMessage>();
        var receiver = BuildReceiver();
        receiver
            .ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ => firstBatch, _ => secondBatch, _ => emptyBatch);

        var count = await ServiceBusBatchDeleter.DrainDeadLetterReceiverAsync(receiver, BatchId, CancellationToken.None);

        count.Should().Be(1);
        await receiver.Received(3).ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(strayMessage, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).CompleteMessageAsync(matchingMessage, Arg.Any<CancellationToken>());
    }
}
