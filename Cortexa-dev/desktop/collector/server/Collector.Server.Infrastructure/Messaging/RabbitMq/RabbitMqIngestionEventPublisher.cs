using System.Text.Json;
using Collector.Domain.Serialization;
using Collector.Server.Application.Events;
using Collector.Server.Application.Ports;
using Collector.Server.Infrastructure.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Collector.Server.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqIngestionEventPublisher(
    RabbitMqConnectionProvider connections,
    IOptions<MessagingOptions> options) : IIngestionEventPublisher
{
    public const string SessionIdHeader = "session_id";
    public const string BatchIdHeader = "batch_id";
    private const string ContentType = "application/json";

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var topic = options.Value.Topics.IngestionCompleted;
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, CollectorJson.Options);

        await using var channel = await connections.CreateChannelAsync(cancellationToken);
        await channel.BasicPublishAsync(
            topic,
            string.Empty,
            mandatory: true,
            CreateProperties(envelope),
            body,
            cancellationToken);
    }

    private static BasicProperties CreateProperties(EventEnvelope envelope) => new()
    {
        Persistent = true,
        ContentType = ContentType,
        MessageId = envelope.EventId,
        CorrelationId = envelope.CorrelationId,
        Headers = new Dictionary<string, object?>
        {
            [SessionIdHeader] = envelope.BatchId,
            [BatchIdHeader] = envelope.BatchId
        }
    };
}
