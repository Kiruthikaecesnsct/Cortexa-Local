using Azure.Messaging.ServiceBus;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed class SessionMessageActions : IMessageActions
{
    private readonly ProcessSessionMessageEventArgs _args;
    private readonly ServiceBusClient _client;
    private readonly string _topicName;

    public SessionMessageActions(ProcessSessionMessageEventArgs args, ServiceBusClient client, string topicName)
    {
        _args = args;
        _client = client;
        _topicName = topicName;
    }

    public int RetryAttempt =>
        _args.Message.ApplicationProperties.TryGetValue("x-retry-attempt", out var v) && v is int i
            ? i
            : 0;

    public string? CorrelationId => _args.Message.CorrelationId;

    public Task CompleteAsync(CancellationToken ct) =>
        _args.CompleteMessageAsync(_args.Message, ct);

    public Task AbandonAsync(CancellationToken ct) =>
        _args.AbandonMessageAsync(_args.Message, cancellationToken: ct);

    public Task DeadLetterAsync(string reason, string description, CancellationToken ct)
    {
        var props = BuildDlqProperties();
        return _args.DeadLetterMessageAsync(_args.Message, props, reason, description, ct);
    }

    public async Task ScheduleRetryAsync(string messageBody, int nextAttempt, TimeSpan delay, CancellationToken ct)
    {
        var sender = _client.CreateSender(_topicName);
        await using (sender.ConfigureAwait(false))
        {
            var msg = new ServiceBusMessage(messageBody)
            {
                ScheduledEnqueueTime = DateTimeOffset.UtcNow.Add(delay),
                SessionId = _args.Message.SessionId
            };
            msg.ApplicationProperties["x-retry-attempt"] = nextAttempt;
            if (!string.IsNullOrEmpty(CorrelationId))
                msg.CorrelationId = CorrelationId;
            await sender.SendMessageAsync(msg, ct);
        }
    }

    private Dictionary<string, object> BuildDlqProperties()
    {
        var props = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(CorrelationId))
            props["correlation_id"] = CorrelationId;
        return props;
    }
}
