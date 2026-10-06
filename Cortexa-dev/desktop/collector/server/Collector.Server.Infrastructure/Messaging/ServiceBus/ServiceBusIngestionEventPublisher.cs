using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Collector.Domain.Serialization;
using Collector.Server.Application.Events;
using Collector.Server.Application.Ports;
using Collector.Server.Infrastructure.Health;
using Collector.Server.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure.Messaging.ServiceBus;

public sealed class ServiceBusIngestionEventPublisher : IIngestionEventPublisher, IBrokerProbe, IAsyncDisposable
{
    public const string BatchIdProperty = "batch_id";
    private const string ContentType = "application/json";

    private readonly ServiceBusClient _client;
    private readonly string _topic;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusIngestionEventPublisher(ServiceBusClient client, IOptions<MessagingOptions> options)
    {
        _client = client;
        _topic = options.Value.Topics.IngestionCompleted;
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var sender = _senders.GetOrAdd(_topic, _client.CreateSender);
        await sender.SendMessageAsync(CreateMessage(envelope), cancellationToken);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        Task.FromResult(!_client.IsClosed);

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
    }

    private static ServiceBusMessage CreateMessage(EventEnvelope envelope)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, CollectorJson.Options);

        return new ServiceBusMessage(body)
        {
            ContentType = ContentType,
            MessageId = envelope.EventId,
            CorrelationId = envelope.CorrelationId,
            SessionId = envelope.BatchId,
            ApplicationProperties = { [BatchIdProperty] = envelope.BatchId }
        };
    }
}
