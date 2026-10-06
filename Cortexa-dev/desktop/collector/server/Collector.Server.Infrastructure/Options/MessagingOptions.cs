namespace Collector.Server.Infrastructure.Options;

public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public MessagingProvider Provider { get; set; } = MessagingProvider.RabbitMq;

    public MessagingTopics Topics { get; set; } = new();

    public RabbitMqOptions RabbitMq { get; set; } = new();

    public ServiceBusOptions ServiceBus { get; set; } = new();
}
