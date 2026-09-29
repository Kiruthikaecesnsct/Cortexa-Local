using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqBatchDeleter : IBatchDeleter
{
    private readonly RabbitMqQueueScanner _scanner;
    private readonly ServiceBusSettings _settings;
    private readonly ILogger<RabbitMqBatchDeleter> _logger;

    public RabbitMqBatchDeleter(
        RabbitMqQueueScanner scanner,
        IOptions<ServiceBusSettings> settings,
        ILogger<RabbitMqBatchDeleter> logger)
    {
        _scanner = scanner;
        _settings = settings.Value;
        _logger = logger;
    }

    public string StoreName => "RabbitMq";

    public async Task<StoreDeletionResult> DeleteAsync(string batchId, DeleteBatchContext context, CancellationToken ct)
    {
        var total = 0;

        try
        {
            foreach (var queue in BuildQueues())
                total += await DrainQueueAsync(queue, batchId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new StoreDeletionResult(StoreName, total, false, ex.Message);
        }

        return new StoreDeletionResult(StoreName, total, true);
    }

    private IEnumerable<string> BuildQueues() =>
        ServiceBusDrainTargetBuilder
            .BuildDrainTargets(_settings.TopicNames, _settings.SubscriptionName, _settings.WorkerSubscriptions, _settings.DrainDeadLetters)
            .Select(RabbitMqNames.QueueFor);

    private async Task<int> DrainQueueAsync(string queue, string batchId, CancellationToken ct)
    {
        var count = await _scanner.ScanAsync(
            queue,
            properties => string.Equals(RabbitMqHeaders.ResolveBatchId(properties), batchId, StringComparison.Ordinal),
            ct);

        if (count > 0)
            _logger.LogInformation("Drained batch messages from RabbitMQ. queue={Queue} count={Count}", queue, count);

        return count;
    }
}
