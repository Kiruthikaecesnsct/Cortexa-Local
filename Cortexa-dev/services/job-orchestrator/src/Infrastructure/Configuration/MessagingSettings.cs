namespace Cortexa.JobOrchestrator.Infrastructure.Configuration;

public sealed class MessagingSettings
{
    public const string ServiceBusBackend = "servicebus";
    public const string RabbitMqBackend = "rabbitmq";

    // "servicebus" uses Azure Service Bus; "rabbitmq" uses a local RabbitMQ broker.
    public string Backend { get; set; } = ServiceBusBackend;

    public bool UsesRabbitMq => IsBackend(RabbitMqBackend);

    public void Validate()
    {
        if (!IsBackend(ServiceBusBackend) && !UsesRabbitMq)
            throw new InvalidOperationException(
                $"Messaging:Backend must be '{ServiceBusBackend}' or '{RabbitMqBackend}', got '{Backend}'.");
    }

    private bool IsBackend(string backend) =>
        string.Equals(Backend, backend, StringComparison.OrdinalIgnoreCase);
}
