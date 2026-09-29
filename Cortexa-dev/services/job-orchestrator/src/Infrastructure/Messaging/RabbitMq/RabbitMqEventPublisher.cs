using System.Text;
using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

public sealed record DelayedPublication(
    string Topic,
    string Body,
    TimeSpan Delay,
    int RetryAttempt,
    string? SessionId,
    string? CorrelationId);

public sealed class RabbitMqEventPublisher : IEventPublisher
{
    private const string ContentType = "application/json";

    private readonly RabbitMqConnectionProvider _connections;
    private readonly ServiceBusSettings _settings;

    public RabbitMqEventPublisher(RabbitMqConnectionProvider connections, IOptions<ServiceBusSettings> settings)
    {
        _connections = connections;
        _settings = settings.Value;
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var sessionKey = SessionKeyResolver.Resolve(envelope);
        var properties = CreateProperties(sessionKey.SessionId, envelope.CorrelationId);
        properties.MessageId = envelope.EventId;
        foreach (var property in sessionKey.ApplicationProperties)
            properties.Headers![property.Key] = property.Value;

        var topic = TopicNameResolver.Resolve(_settings, envelope.EventType);
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope);

        await using var channel = await _connections.CreateChannelAsync(ct);
        await channel.BasicPublishAsync(topic, string.Empty, mandatory: true, properties, body, ct);
    }

    // RabbitMQ has no scheduled enqueue: the message waits in a per-delay TTL queue whose
    // dead-letter exchange is the topic exchange, so it reaches subscribers once the delay expires.
    public async Task PublishDelayedAsync(DelayedPublication publication, CancellationToken ct)
    {
        var properties = CreateProperties(publication.SessionId, publication.CorrelationId);
        properties.Headers![RabbitMqNames.RetryAttemptHeader] = publication.RetryAttempt;
        if (!string.IsNullOrEmpty(publication.SessionId))
            properties.Headers[SessionKeyResolver.BatchIdProperty] = SessionKeyResolver.BatchIdFromSessionId(publication.SessionId);

        var body = Encoding.UTF8.GetBytes(publication.Body);
        await using var channel = await _connections.CreateChannelAsync(ct);

        if (publication.Delay <= TimeSpan.Zero)
        {
            await channel.BasicPublishAsync(publication.Topic, string.Empty, mandatory: true, properties, body, ct);
            return;
        }

        var delayQueue = await DeclareDelayQueueAsync(channel, publication, ct);
        await channel.BasicPublishAsync(string.Empty, delayQueue, mandatory: true, properties, body, ct);
    }

    private static async Task<string> DeclareDelayQueueAsync(IChannel channel, DelayedPublication publication, CancellationToken ct)
    {
        var queue = RabbitMqNames.DelayQueue(publication.Topic, publication.Delay);
        var arguments = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = (long)publication.Delay.TotalMilliseconds,
            ["x-dead-letter-exchange"] = publication.Topic
        };

        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: ct);
        return queue;
    }

    private static BasicProperties CreateProperties(string? sessionId, string? correlationId)
    {
        var properties = new BasicProperties
        {
            ContentType = ContentType,
            Persistent = true,
            Headers = new Dictionary<string, object?>()
        };

        if (!string.IsNullOrEmpty(correlationId))
            properties.CorrelationId = correlationId;
        if (!string.IsNullOrEmpty(sessionId))
            properties.Headers[RabbitMqNames.SessionIdHeader] = sessionId;

        return properties;
    }
}
