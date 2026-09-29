using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed class SagaEventConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSettings _settings;
    private readonly ILogger<SagaEventConsumer> _logger;
    private readonly List<ServiceBusProcessor> _processors = [];
    private readonly List<ServiceBusSessionProcessor> _sessionProcessors = [];

    private static readonly IReadOnlyList<string> ConsumedTopics =
    [
        SagaEventType.BatchCreated,
        SagaEventType.IngestionCompleted,
        SagaEventType.ExtractionCompleted,
        SagaEventType.ExtractionFailed,
        SagaEventType.EvidenceCompleted,
        SagaEventType.EvidenceFailed,
        SagaEventType.ScoringCompleted,
        SagaEventType.ScoringFailed,
        SagaEventType.HarvestingFailed,
        SagaEventType.SeedingFailed,
        SagaEventType.AssetEmbeddingCompleted,
        SagaEventType.DigestCompleted,
        SagaEventType.LandscapeCompleted,
        SagaEventType.IdeationCompleted,
        SagaEventType.EngineCompleted
    ];

    public SagaEventConsumer(
        IServiceScopeFactory scopeFactory,
        ServiceBusClient client,
        IOptions<ServiceBusSettings> settings,
        ILogger<SagaEventConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.UseSessions)
            await StartSessionProcessorsAsync(stoppingToken);
        else
            await StartProcessorsAsync(stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await StopAllProcessorsAsync();
    }

    private async Task StartSessionProcessorsAsync(CancellationToken stoppingToken)
    {
        foreach (var topic in ConsumedTopics)
        {
            var topicName = ResolveTopicName(topic);
            var processor = _client.CreateSessionProcessor(
                topicName,
                _settings.SubscriptionName,
                new ServiceBusSessionProcessorOptions
                {
                    MaxConcurrentSessions = _settings.MaxConcurrentSessions,
                    MaxConcurrentCallsPerSession = 1,
                    AutoCompleteMessages = false
                });

            processor.ProcessMessageAsync += args => ProcessSessionMessageAsync(args, stoppingToken);
            processor.ProcessErrorAsync += ProcessErrorAsync;

            await processor.StartProcessingAsync(stoppingToken);
            _sessionProcessors.Add(processor);
        }
    }

    private async Task StartProcessorsAsync(CancellationToken stoppingToken)
    {
        foreach (var topic in ConsumedTopics)
        {
            var topicName = ResolveTopicName(topic);
            var processor = _client.CreateProcessor(
                topicName,
                _settings.SubscriptionName,
                new ServiceBusProcessorOptions
                {
                    MaxConcurrentCalls = 1,
                    AutoCompleteMessages = false
                });

            processor.ProcessMessageAsync += args => ProcessMessageAsync(args, stoppingToken);
            processor.ProcessErrorAsync += ProcessErrorAsync;

            await processor.StartProcessingAsync(stoppingToken);
            _processors.Add(processor);
        }
    }

    private async Task StopAllProcessorsAsync()
    {
        foreach (var processor in _sessionProcessors)
            await processor.StopProcessingAsync();

        foreach (var processor in _processors)
            await processor.StopProcessingAsync();
    }

    private async Task ProcessSessionMessageAsync(ProcessSessionMessageEventArgs args, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SagaMessageProcessor>();
        var topicName = ResolveTopicFromEntityPath(args.EntityPath);
        _logger.LogInformation(
            "Service Bus message received on {EntityPath} for batch {BatchId}",
            args.EntityPath,
            args.SessionId);
        var actions = new SessionMessageActions(args, _client, topicName);
        await processor.ProcessAsync(args.Message.Body.ToString(), actions, ct);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SagaMessageProcessor>();
        var topicName = ResolveTopicFromEntityPath(args.EntityPath);
        _logger.LogInformation("Service Bus message received on {EntityPath}", args.EntityPath);
        var actions = new ReceiverMessageActions(args, _client, topicName);
        await processor.ProcessAsync(args.Message.Body.ToString(), actions, ct);
    }

    private static string ResolveTopicFromEntityPath(string entityPath) =>
        entityPath.Split('/')[0];

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus processor error on {EntityPath}.", args.EntityPath);
        return Task.CompletedTask;
    }

    private string ResolveTopicName(string eventType)
    {
        if (_settings.TopicNames.TryGetValue(eventType, out var name))
            return name;

        return eventType.Replace('.', '-');
    }
}
