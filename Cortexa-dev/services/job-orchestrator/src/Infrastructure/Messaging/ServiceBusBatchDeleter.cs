using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed class ServiceBusBatchDeleter : IBatchDeleter
{
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(1);
    private const int ReceiveBatchSize = 50;

    private readonly ServiceBusClient _client;
    private readonly ServiceBusSettings _settings;
    private readonly IReadOnlySet<string> _candidateScopedTopics;
    private readonly ILogger<ServiceBusBatchDeleter> _logger;

    public ServiceBusBatchDeleter(
        ServiceBusClient client,
        IOptions<ServiceBusSettings> settings,
        ILogger<ServiceBusBatchDeleter> logger)
    {
        _client = client;
        _settings = settings.Value;
        _candidateScopedTopics = BuildCandidateScopedTopics(_settings);
        _logger = logger;
    }

    private static IReadOnlySet<string> BuildCandidateScopedTopics(ServiceBusSettings settings) =>
        new HashSet<string>(
            settings.TopicNames
                .Where(kv => SessionKeyResolver.IsCandidateScoped(kv.Key))
                .Select(kv => kv.Value),
            StringComparer.OrdinalIgnoreCase);

    public string StoreName => "ServiceBus";

    public async Task<StoreDeletionResult> DeleteAsync(string batchId, DeleteBatchContext context, CancellationToken ct)
    {
        var targets = ServiceBusDrainTargetBuilder.BuildDrainTargets(
            _settings.TopicNames,
            _settings.SubscriptionName,
            _settings.WorkerSubscriptions,
            _settings.DrainDeadLetters);

        var total = 0;

        try
        {
            foreach (var target in targets)
                total += await DrainTargetAsync(target, batchId, ct);
        }
        catch (Exception ex)
        {
            return new StoreDeletionResult(StoreName, total, false, ex.Message);
        }

        return new StoreDeletionResult(StoreName, total, true);
    }

    private async Task<int> DrainTargetAsync(DrainTarget target, string batchId, CancellationToken ct)
    {
        try
        {
            var count = target.SubQueue == SubQueue.DeadLetter
                ? await DrainDeadLetterTargetAsync(target, batchId, ct)
                : await DrainSessionTargetAsync(target, batchId, ct);

            LogDrainResult(target, count);
            return count;
        }
        catch (ServiceBusException ex) when (IsExpected(ex))
        {
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }

    private Task<int> DrainSessionTargetAsync(DrainTarget target, string batchId, CancellationToken ct) =>
        _candidateScopedTopics.Contains(target.Topic)
            ? DrainCandidateScopedSessionsAsync(target, batchId, ct)
            : DrainBatchScopedSessionAsync(target, batchId, ct);

    private async Task<int> DrainBatchScopedSessionAsync(DrainTarget target, string batchId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var acceptTimeout = TimeSpan.FromSeconds(_settings.SessionAcceptTimeoutSeconds);
        cts.CancelAfter(acceptTimeout);

        await using var receiver = await _client.AcceptSessionAsync(
            target.Topic,
            target.Subscription,
            batchId,
            options: null,
            cts.Token);

        return await DrainSessionReceiverAsync(receiver, ct);
    }

    private async Task<int> DrainCandidateScopedSessionsAsync(DrainTarget target, string batchId, CancellationToken ct)
    {
        var total = 0;
        var visitedSessionIds = new HashSet<string>(StringComparer.Ordinal);
        var budget = TimeSpan.FromSeconds(_settings.CandidateSessionDrainBudgetSeconds);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < budget)
        {
            var receiver = await TryAcceptNextSessionAsync(target, ct);
            if (receiver is null)
                break;

            await using (receiver)
            {
                if (!visitedSessionIds.Add(receiver.SessionId))
                    break;

                total += await DrainDeadLetterReceiverAsync(receiver, batchId, ct);
            }
        }

        if (stopwatch.Elapsed >= budget)
        {
            _logger.LogWarning(
                "Batch delete Service Bus drain partial. batch_id={BatchId} topic={Topic} subscription={Subscription} " +
                "budget_seconds={BudgetSeconds} elapsed_seconds={ElapsedSeconds} sessions_visited={SessionsVisited} " +
                "reason=budget_expired",
                batchId,
                target.Topic,
                target.Subscription,
                _settings.CandidateSessionDrainBudgetSeconds,
                stopwatch.Elapsed.TotalSeconds,
                visitedSessionIds.Count);
        }

        return total;
    }

    private async Task<ServiceBusSessionReceiver?> TryAcceptNextSessionAsync(DrainTarget target, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var acceptTimeout = TimeSpan.FromSeconds(_settings.SessionAcceptTimeoutSeconds);
        cts.CancelAfter(acceptTimeout);

        try
        {
            return await _client.AcceptNextSessionAsync(target.Topic, target.Subscription, options: null, cts.Token);
        }
        catch (ServiceBusException ex) when (IsExpected(ex))
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool MatchesBatch(ServiceBusReceivedMessage message, string batchId) =>
        TryGetBatchId(message, out var messageBatchId) &&
        string.Equals(messageBatchId, batchId, StringComparison.Ordinal);

    private async Task<int> DrainDeadLetterTargetAsync(DrainTarget target, string batchId, CancellationToken ct)
    {
        await using var receiver = _client.CreateReceiver(
            target.Topic,
            target.Subscription,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        return await DrainDeadLetterReceiverAsync(receiver, batchId, ct);
    }

    private void LogDrainResult(DrainTarget target, int count)
    {
        if (count == 0)
            return;

        _logger.LogInformation(
            "Drained batch messages from Service Bus. topic={Topic} subscription={Subscription} sub_queue={SubQueue} count={Count}",
            target.Topic,
            target.Subscription,
            target.SubQueue,
            count);
    }

    private static async Task<int> DrainSessionReceiverAsync(ServiceBusSessionReceiver receiver, CancellationToken ct)
    {
        var count = 0;

        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(ReceiveBatchSize, ReceiveWait, ct);
            if (messages.Count == 0)
                break;

            foreach (var message in messages)
            {
                await receiver.CompleteMessageAsync(message, ct);
                count++;
            }
        }

        return count;
    }

    internal static async Task<int> DrainDeadLetterReceiverAsync(ServiceBusReceiver receiver, string batchId, CancellationToken ct)
    {
        var count = 0;
        var state = new DeadLetterDrainState(batchId, new HashSet<long>());

        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(ReceiveBatchSize, ReceiveWait, ct);
            if (messages.Count == 0)
                break;

            var (matched, sawUnseenMessage) = await ProcessDeadLetterBatchAsync(receiver, messages, state, ct);
            count += matched;

            if (!sawUnseenMessage)
                break;
        }

        return count;
    }

    internal static async Task<(int Matched, bool SawUnseenMessage)> ProcessDeadLetterBatchAsync(
        ServiceBusReceiver receiver,
        IReadOnlyList<ServiceBusReceivedMessage> messages,
        DeadLetterDrainState state,
        CancellationToken ct)
    {
        var matched = 0;
        var sawUnseenMessage = false;

        foreach (var message in messages)
        {
            if (!state.SeenSequenceNumbers.Add(message.SequenceNumber))
                continue;

            sawUnseenMessage = true;

            if (!MatchesBatch(message, state.BatchId))
            {
                await receiver.AbandonMessageAsync(message, cancellationToken: ct);
                continue;
            }

            await receiver.CompleteMessageAsync(message, ct);
            matched++;
        }

        return (matched, sawUnseenMessage);
    }

    private static bool TryGetBatchId(ServiceBusReceivedMessage message, out string batchId)
    {
        if (message.ApplicationProperties.TryGetValue(SessionKeyResolver.BatchIdProperty, out var raw) &&
            raw is string { Length: > 0 } value)
        {
            batchId = value;
            return true;
        }

        batchId = string.Empty;
        return false;
    }

    private static bool IsExpected(ServiceBusException ex) =>
        ex.Reason is ServiceBusFailureReason.ServiceTimeout
            or ServiceBusFailureReason.SessionCannotBeLocked
            or ServiceBusFailureReason.MessagingEntityNotFound;

    internal sealed record DeadLetterDrainState(string BatchId, HashSet<long> SeenSequenceNumbers);
}
