using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

// Each subscription queue is single-active-consumer and drained with prefetch 1, which keeps the
// per-queue ordering that Service Bus sessions provide.
public sealed class RabbitMqSagaEventConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqConnectionProvider _connections;
    private readonly ServiceBusSettings _settings;
    private readonly ILogger<RabbitMqSagaEventConsumer> _logger;
    private readonly List<IChannel> _channels = [];

    public RabbitMqSagaEventConsumer(
        IServiceScopeFactory scopeFactory,
        RabbitMqConnectionProvider connections,
        IOptions<ServiceBusSettings> settings,
        ILogger<RabbitMqSagaEventConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _connections = connections;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var eventType in SagaEventConsumer.ConsumedTopics)
            await StartConsumerAsync(TopicNameResolver.Resolve(_settings, eventType), stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await CloseChannelsAsync();
    }

    private async Task StartConsumerAsync(string topic, CancellationToken ct)
    {
        var endpoint = new RabbitMqEndpoint(topic, RabbitMqNames.SubscriptionQueue(topic, _settings.SubscriptionName));
        var channel = await _connections.CreateChannelAsync(ct);
        _channels.Add(channel);
        await channel.BasicQosAsync(0, _connections.PrefetchCount, false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) => HandleDeliveryAsync(new RabbitMqDelivery(channel, args, endpoint), ct);

        try
        {
            await channel.BasicConsumeAsync(endpoint.Queue, autoAck: false, consumer, ct);
        }
        catch (OperationInterruptedException ex)
        {
            throw new InvalidOperationException(
                $"RabbitMQ queue '{endpoint.Queue}' is not available. Run deploy/local/native/rabbitmq-topology.ps1 first.",
                ex);
        }
    }

    private async Task HandleDeliveryAsync(RabbitMqDelivery delivery, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SagaMessageProcessor>();
        var publisher = scope.ServiceProvider.GetRequiredService<RabbitMqEventPublisher>();
        var actions = new RabbitMqMessageActions(delivery, publisher);

        _logger.LogInformation("RabbitMQ message received on {Queue}", delivery.Endpoint.Queue);

        try
        {
            await processor.ProcessAsync(delivery.BodyText, actions, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RabbitMQ message settlement failed on {Queue}. Returning it to the queue.", delivery.Endpoint.Queue);
            await actions.AbandonAsync(CancellationToken.None);
        }
    }

    private async Task CloseChannelsAsync()
    {
        foreach (var channel in _channels)
            await channel.DisposeAsync();

        _channels.Clear();
    }
}
