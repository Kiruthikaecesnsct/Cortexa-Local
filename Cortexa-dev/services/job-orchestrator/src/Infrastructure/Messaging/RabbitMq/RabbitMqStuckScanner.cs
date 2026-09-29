using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqStuckScanner : IServiceBusStuckScanner
{
    private readonly RabbitMqQueueScanner _scanner;
    private readonly IReadOnlyList<string> _deadLetterQueues;

    public RabbitMqStuckScanner(RabbitMqQueueScanner scanner, IOptions<ServiceBusSettings> settings)
    {
        _scanner = scanner;
        _deadLetterQueues = BuildDeadLetterQueues(settings.Value);
    }

    public async Task<IReadOnlyCollection<string>> ListStuckBatchIdsAsync(CancellationToken ct)
    {
        var stuckIds = new HashSet<string>();

        foreach (var queue in _deadLetterQueues)
            await _scanner.ScanAsync(queue, properties => CollectBatchId(properties, stuckIds), ct);

        return stuckIds;
    }

    private static bool CollectBatchId(RabbitMQ.Client.IReadOnlyBasicProperties properties, HashSet<string> stuckIds)
    {
        var batchId = RabbitMqHeaders.ResolveBatchId(properties);
        if (!string.IsNullOrWhiteSpace(batchId))
            stuckIds.Add(batchId);

        return false;
    }

    private static IReadOnlyList<string> BuildDeadLetterQueues(ServiceBusSettings settings) =>
        ServiceBusDrainTargetBuilder
            .BuildDrainTargets(settings.TopicNames, settings.SubscriptionName, settings.WorkerSubscriptions, includeDeadLetters: true)
            .Where(target => target.SubQueue == SubQueue.DeadLetter)
            .Select(RabbitMqNames.QueueFor)
            .ToArray();
}
