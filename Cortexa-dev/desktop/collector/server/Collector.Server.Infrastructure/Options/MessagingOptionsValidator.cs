using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure.Options;

public sealed class MessagingOptionsValidator : IValidateOptions<MessagingOptions>
{
    private const string Prefix = MessagingOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        var failures = new OptionFailures();
        failures.RequireText(options.Topics.IngestionCompleted, $"{Prefix}:Topics:{nameof(MessagingTopics.IngestionCompleted)}");

        if (options.Provider == MessagingProvider.RabbitMq)
        {
            ValidateRabbitMq(options.RabbitMq, failures);
        }
        else
        {
            ValidateServiceBus(options.ServiceBus, failures);
        }

        return failures.ToResult();
    }

    private static void ValidateRabbitMq(RabbitMqOptions options, OptionFailures failures)
    {
        var prefix = $"{Prefix}:{nameof(MessagingOptions.RabbitMq)}";
        failures.RequireText(options.HostName, $"{prefix}:{nameof(RabbitMqOptions.HostName)}");
        failures.RequirePositive(options.Port, $"{prefix}:{nameof(RabbitMqOptions.Port)}");
        failures.RequireText(options.VirtualHost, $"{prefix}:{nameof(RabbitMqOptions.VirtualHost)}");
        failures.RequireText(options.UserName, $"{prefix}:{nameof(RabbitMqOptions.UserName)}");
        failures.RequireText(options.Password, $"{prefix}:{nameof(RabbitMqOptions.Password)}");
        failures.RequireText(options.ClientName, $"{prefix}:{nameof(RabbitMqOptions.ClientName)}");
    }

    private static void ValidateServiceBus(ServiceBusOptions options, OptionFailures failures)
    {
        var prefix = $"{Prefix}:{nameof(MessagingOptions.ServiceBus)}";
        failures.RequireEither(
            options.ConnectionString,
            options.FullyQualifiedNamespace,
            $"{prefix}:{nameof(ServiceBusOptions.ConnectionString)} or {nameof(ServiceBusOptions.FullyQualifiedNamespace)}");
    }
}
