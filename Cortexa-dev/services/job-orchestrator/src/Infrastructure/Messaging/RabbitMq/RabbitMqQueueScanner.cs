using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

// Inspects the ready messages of a queue without consuming them for good: messages the selector
// claims are acknowledged (removed); every other message is held, then returned with one
// multiple-nack, which (unlike a channel close) does not count toward the quorum delivery limit.
// This stands in for Service Bus peek and selective drain.
public sealed class RabbitMqQueueScanner
{
    private readonly RabbitMqConnectionProvider _connections;
    private readonly int _maxScanMessages;
    private readonly ILogger<RabbitMqQueueScanner> _logger;

    public RabbitMqQueueScanner(
        RabbitMqConnectionProvider connections,
        IOptions<RabbitMqSettings> settings,
        ILogger<RabbitMqQueueScanner> logger)
    {
        _connections = connections;
        _maxScanMessages = Math.Max(1, settings.Value.MaxScanMessages);
        _logger = logger;
    }

    public async Task<int> ScanAsync(string queue, Func<IReadOnlyBasicProperties, bool> shouldRemove, CancellationToken ct)
    {
        await using var channel = await _connections.CreateChannelAsync(ct);
        try
        {
            return await ScanChannelAsync(channel, queue, shouldRemove, ct);
        }
        catch (OperationInterruptedException ex)
        {
            _logger.LogWarning("RabbitMQ queue scan skipped. queue={Queue} reason={Reason}", queue, ex.Message);
            return 0;
        }
    }

    private async Task<int> ScanChannelAsync(
        IChannel channel,
        string queue,
        Func<IReadOnlyBasicProperties, bool> shouldRemove,
        CancellationToken ct)
    {
        var removed = 0;
        ulong? lastHeldTag = null;
        for (var scanned = 0; scanned < _maxScanMessages; scanned++)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: false, ct);
            if (result is null)
                break;

            if (!shouldRemove(result.BasicProperties))
            {
                lastHeldTag = result.DeliveryTag;
                continue;
            }

            await channel.BasicAckAsync(result.DeliveryTag, multiple: false, ct);
            removed++;
        }

        await ReturnHeldMessagesAsync(channel, lastHeldTag, ct);
        return removed;
    }

    private static async Task ReturnHeldMessagesAsync(IChannel channel, ulong? lastHeldTag, CancellationToken ct)
    {
        if (lastHeldTag is { } tag)
            await channel.BasicNackAsync(tag, multiple: true, requeue: true, ct);
    }
}
