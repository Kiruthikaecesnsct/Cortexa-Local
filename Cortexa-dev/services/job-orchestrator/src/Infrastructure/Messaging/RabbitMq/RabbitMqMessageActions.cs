using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

public sealed record RabbitMqEndpoint(string Topic, string Queue);

public sealed class RabbitMqDelivery
{
    public RabbitMqDelivery(IChannel channel, BasicDeliverEventArgs args, RabbitMqEndpoint endpoint)
    {
        Channel = channel;
        DeliveryTag = args.DeliveryTag;
        Properties = args.BasicProperties;
        // The delivery body buffer is only valid inside the consumer callback, so keep a copy.
        Body = args.Body.ToArray();
        Endpoint = endpoint;
    }

    public IChannel Channel { get; }
    public ulong DeliveryTag { get; }
    public IReadOnlyBasicProperties Properties { get; }
    public byte[] Body { get; }
    public RabbitMqEndpoint Endpoint { get; }

    public string BodyText => Encoding.UTF8.GetString(Body);
}

public sealed class RabbitMqMessageActions : IMessageActions
{
    private readonly RabbitMqDelivery _delivery;
    private readonly RabbitMqEventPublisher _publisher;

    public RabbitMqMessageActions(RabbitMqDelivery delivery, RabbitMqEventPublisher publisher)
    {
        _delivery = delivery;
        _publisher = publisher;
    }

    public int RetryAttempt => RabbitMqHeaders.GetInt(_delivery.Properties.Headers, RabbitMqNames.RetryAttemptHeader);

    public string? CorrelationId => _delivery.Properties.CorrelationId;

    public async Task CompleteAsync(CancellationToken ct) =>
        await _delivery.Channel.BasicAckAsync(_delivery.DeliveryTag, multiple: false, ct);

    // Reject (not nack) so the quorum queue counts the attempt toward x-delivery-limit.
    public async Task AbandonAsync(CancellationToken ct) =>
        await _delivery.Channel.BasicRejectAsync(_delivery.DeliveryTag, requeue: true, ct);

    public async Task DeadLetterAsync(string reason, string description, CancellationToken ct)
    {
        var properties = BuildDeadLetterProperties(reason, description);
        var deadLetterQueue = RabbitMqNames.DeadLetterQueue(_delivery.Endpoint.Queue);

        await _delivery.Channel.BasicPublishAsync(string.Empty, deadLetterQueue, mandatory: true, properties, _delivery.Body, ct);
        await CompleteAsync(ct);
    }

    public Task ScheduleRetryAsync(string messageBody, int nextAttempt, TimeSpan delay, CancellationToken ct)
    {
        var sessionId = RabbitMqHeaders.GetString(_delivery.Properties.Headers, RabbitMqNames.SessionIdHeader);
        var publication = new DelayedPublication(_delivery.Endpoint.Topic, messageBody, delay, nextAttempt, sessionId, CorrelationId);
        return _publisher.PublishDelayedAsync(publication, ct);
    }

    private BasicProperties BuildDeadLetterProperties(string reason, string description)
    {
        var headers = RabbitMqHeaders.Copy(_delivery.Properties.Headers);
        headers[RabbitMqNames.DeadLetterReasonHeader] = reason;
        headers[RabbitMqNames.DeadLetterDescriptionHeader] = description;
        if (!string.IsNullOrEmpty(CorrelationId))
            headers[RabbitMqNames.CorrelationIdHeader] = CorrelationId;

        return new BasicProperties
        {
            ContentType = _delivery.Properties.ContentType,
            CorrelationId = CorrelationId,
            MessageId = _delivery.Properties.MessageId,
            Persistent = true,
            Headers = headers
        };
    }
}
