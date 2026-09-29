using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed class ServiceBusStuckScanner : IServiceBusStuckScanner
{
    private const int PeekMaxMessages = 100;

    private readonly ServiceBusClient _client;
    private readonly IReadOnlyList<DrainTarget> _deadLetterTargets;
    private readonly ILogger<ServiceBusStuckScanner> _logger;

    public ServiceBusStuckScanner(
        ServiceBusClient client,
        IOptions<ServiceBusSettings> settings,
        ILogger<ServiceBusStuckScanner> logger)
    {
        var s = settings.Value;
        _client = client;
        _deadLetterTargets = BuildDeadLetterTargets(s);
        _logger = logger;
    }

    private static IReadOnlyList<DrainTarget> BuildDeadLetterTargets(ServiceBusSettings settings) =>
        ServiceBusDrainTargetBuilder
            .BuildDrainTargets(
                settings.TopicNames,
                settings.SubscriptionName,
                settings.WorkerSubscriptions,
                includeDeadLetters: true)
            .Where(target => target.SubQueue == SubQueue.DeadLetter)
            .ToArray();

    public async Task<IReadOnlyCollection<string>> ListStuckBatchIdsAsync(CancellationToken ct)
    {
        var stuckIds = new HashSet<string>();

        foreach (var target in _deadLetterTargets)
            await PeekDeadLetterAsync(target, stuckIds, ct);

        return stuckIds;
    }

    private async Task PeekDeadLetterAsync(DrainTarget target, HashSet<string> stuckIds, CancellationToken ct)
    {
        try
        {
            await using var receiver = _client.CreateReceiver(
                target.Topic,
                target.Subscription,
                new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

            var messages = await receiver.PeekMessagesAsync(PeekMaxMessages, cancellationToken: ct);

            foreach (var message in messages)
            {
                var batchId = ResolveBatchId(message);
                if (!string.IsNullOrWhiteSpace(batchId))
                    stuckIds.Add(batchId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "DLQ peek failed for topic. topic={Topic} subscription={Subscription} reason={Reason}",
                target.Topic,
                target.Subscription,
                ex.Message);
        }
    }

    private static string? ResolveBatchId(ServiceBusReceivedMessage message)
    {
        if (message.ApplicationProperties.TryGetValue(SessionKeyResolver.BatchIdProperty, out var raw) &&
            raw is string { Length: > 0 } propertyValue)
            return propertyValue;

        if (string.IsNullOrWhiteSpace(message.SessionId))
            return null;

        var separatorIndex = message.SessionId.IndexOf(':');
        return separatorIndex >= 0 ? message.SessionId[..separatorIndex] : message.SessionId;
    }
}
