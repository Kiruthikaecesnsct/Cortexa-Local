using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed class ServiceBusEventPublisher : IEventPublisher
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSettings _settings;

    public ServiceBusEventPublisher(ServiceBusClient client, IOptions<ServiceBusSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var topicName = TopicNameResolver.Resolve(_settings, envelope.EventType);
        var sender = _client.CreateSender(topicName);

        await using (sender.ConfigureAwait(false))
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(envelope);
            var sessionKey = SessionKeyResolver.Resolve(envelope);
            var message = new ServiceBusMessage(body)
            {
                ContentType = "application/json",
                CorrelationId = envelope.CorrelationId,
                MessageId = envelope.EventId,
                SessionId = sessionKey.SessionId
            };

            StampApplicationProperties(message, sessionKey.ApplicationProperties);

            await sender.SendMessageAsync(message, ct);
        }
    }

    public async Task PublishScheduledAsync(string topicName, string messageBody, DateTimeOffset scheduledEnqueueTime, int retryAttempt, string? sessionId, CancellationToken ct)
    {
        var sender = _client.CreateSender(topicName);
        await using (sender.ConfigureAwait(false))
        {
            var message = new ServiceBusMessage(messageBody)
            {
                ScheduledEnqueueTime = scheduledEnqueueTime,
                ApplicationProperties = { ["x-retry-attempt"] = retryAttempt }
            };
            if (!string.IsNullOrEmpty(sessionId))
            {
                message.SessionId = sessionId;
                message.ApplicationProperties[SessionKeyResolver.BatchIdProperty] = SessionKeyResolver.BatchIdFromSessionId(sessionId);
            }
            await sender.SendMessageAsync(message, ct);
        }
    }

    private static void StampApplicationProperties(ServiceBusMessage message, IReadOnlyDictionary<string, object> properties)
    {
        foreach (var property in properties)
            message.ApplicationProperties[property.Key] = property.Value;
    }
}
