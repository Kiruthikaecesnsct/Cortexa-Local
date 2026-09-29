using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Infrastructure.BackgroundServices;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using Cortexa.JobOrchestrator.Infrastructure.Storage;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddJobOrchestratorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CosmosSettings>(configuration.GetSection("Cosmos"));
        services.Configure<ServiceBusSettings>(configuration.GetSection("ServiceBus"));
        services.Configure<OrchestratorSettings>(configuration.GetSection("Orchestrator"));
        services.Configure<BlobSettings>(configuration.GetSection("Blob"));
        services.Configure<RetentionPolicyOptions>(configuration.GetSection("Retention"));
        services.Configure<ReconciliationOptions>(configuration.GetSection("Reconciliation"));
        services.Configure<WatchdogOptions>(configuration.GetSection("Watchdog"));
        services.Configure<PatentApiSettings>(configuration.GetSection("PatentApis"));

        services.AddSingleton(sp =>
        {
            var settings = configuration.GetSection("Cosmos").Get<CosmosSettings>()
                ?? throw new InvalidOperationException("Cosmos configuration is required.");

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true
            };

            var cosmosOptions = new CosmosClientOptions { Serializer = new SystemTextJsonCosmosSerializer(jsonOptions) };

            // A configured key (local/emulator auth) takes precedence over managed identity.
            return string.IsNullOrWhiteSpace(settings.Key)
                ? new CosmosClient(settings.Uri, new DefaultAzureCredential(), cosmosOptions)
                : new CosmosClient(settings.Uri, settings.Key, cosmosOptions);
        });

        AddMessaging(services, configuration);

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<BlobSettings>>().Value;

            // A configured connection string (local/emulator auth, e.g. Azurite) takes precedence over managed identity.
            if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
                return new BlobServiceClient(settings.ConnectionString);

            if (string.IsNullOrWhiteSpace(settings.AccountUrl))
                return new BlobServiceClient(new Uri("https://placeholder.blob.core.windows.net"), new DefaultAzureCredential());

            return new BlobServiceClient(new Uri(settings.AccountUrl), new DefaultAzureCredential());
        });

        var vaultUri = configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            services.AddSingleton(new SecretClient(new Uri(vaultUri), new DefaultAzureCredential()));
        }

        services.AddScoped<IGitPatSecretStore>(sp => new KeyVaultGitPatStore(sp.GetService<SecretClient>()));
        services.AddScoped<IPatentSecretWriter>(sp => new KeyVaultPatentSecretWriter(sp.GetService<SecretClient>()));
        services.AddScoped<Application.Contracts.IPatentSourceConfigRepository, CosmosPatentSourceConfigRepository>();
        services.AddHttpClient<IPatentConnectionProbe, Infrastructure.PatentApis.PatentConnectionProbe>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<PatentApiSettings>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });
        services.AddScoped<IBatchDeleter, CosmosBatchDeleter>();
        services.AddScoped<IBatchDeleter, BlobBatchDeleter>();
        services.AddScoped<IBatchDeleter>(sp => new KeyVaultBatchDeleter(sp.GetService<SecretClient>()));
        services.AddHttpClient<IBatchDeleter, Infrastructure.Http.VectorMemoryBatchDeleter>((sp, client) =>
        {
            var baseUrl = configuration["VectorRouter:Url"];
            if (!string.IsNullOrWhiteSpace(baseUrl))
                client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<ISagaRepository, CosmosSagaRepository>();
        services.AddScoped<Application.Contracts.IConfigRepository, CosmosConfigRepository>();
        services.AddScoped<IDocumentRepository, CosmosDocumentRepository>();
        services.AddScoped<IResultsReadRepository, CosmosResultsReadRepository>();
        services.AddScoped<IBlobStorageWriter, BlobStorageWriter>();
        services.AddScoped<IViewableDocumentReader, ViewableDocumentReader>();
        services.AddScoped<Application.Services.ModelConfigValidator>();
        services.AddHttpClient<Application.Contracts.IModelCatalogClient, Infrastructure.Services.ModelCatalogClient>((sp, client) =>
        {
            var baseUrl = configuration["ModelRouter:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl))
                client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddScoped(sp =>
        {
            var orchestratorSettings = sp.GetRequiredService<IOptions<OrchestratorSettings>>().Value;
            return new AdvanceSagaHandler(
                sp.GetRequiredService<ISagaRepository>(),
                sp.GetRequiredService<IEventPublisher>(),
                sp.GetRequiredService<ILogger<AdvanceSagaHandler>>(),
                orchestratorSettings.MaxChunksPerExtractionUnit);
        });
        services.AddScoped<CreateBatchFanOutHandler>();
        services.AddScoped<CreateBatchHandler>();
        services.AddScoped<StartBatchHandler>();
        services.AddScoped<RetryDocumentHandler>();
        services.AddScoped<CancelBatchHandler>();
        services.AddScoped<DeleteBatchHandler>();
        services.AddScoped<GetBatchResultsHandler>();
        services.AddScoped<GetResultDetailHandler>();
        services.AddScoped<GetDocumentHandler>();
        services.AddScoped<ListBatchesHandler>();
        services.AddSingleton<IRetryPolicy, ExponentialBackoffRetryPolicy>();
        services.AddScoped<SagaMessageProcessor>();
        services.AddScoped<IBatchRetentionQuery, CosmosBatchRetentionQuery>();
        services.AddScoped<IRetentionSweeper, RetentionSweepHandler>();
        services.AddHostedService<RetentionSweepBackgroundService>();
        services.AddScoped<StaleBatchWatchdogHandler>();
        services.AddHostedService<StaleBatchWatchdogBackgroundService>();

        services.AddScoped<IBatchExistenceQuery, CosmosBatchExistenceQuery>();
        services.AddScoped<IOrphanScanner, CosmosPipelineOrphanScanner>();
        services.AddScoped<IOrphanScanner, BlobOrphanScanner>();
        services.AddScoped<IOrphanScanner>(sp => new KeyVaultOrphanScanner(
            sp.GetService<SecretClient>(),
            sp.GetRequiredService<ILogger<KeyVaultOrphanScanner>>()));
        services.AddScoped<ISagaReferenceScanner, CosmosSagaReferenceScanner>();
        services.AddScoped<ReconciliationScannerDependencies>(sp => new ReconciliationScannerDependencies(
            sp.GetRequiredService<IEnumerable<IOrphanScanner>>(),
            sp.GetRequiredService<IBatchExistenceQuery>(),
            sp.GetRequiredService<IServiceBusStuckScanner>(),
            sp.GetRequiredService<ISagaReferenceScanner>()));
        services.AddScoped<IReconciliationHandler, ReconciliationHandler>();

        return services;
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        var messaging = configuration.GetSection("Messaging").Get<MessagingSettings>() ?? new MessagingSettings();
        messaging.Validate();

        if (messaging.UsesRabbitMq)
            AddRabbitMqMessaging(services, configuration);
        else
            AddServiceBusMessaging(services, configuration);
    }

    private static void AddServiceBusMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            var settings = configuration.GetSection("ServiceBus").Get<ServiceBusSettings>()
                ?? throw new InvalidOperationException("ServiceBus configuration is required.");

            // A configured connection string (local/emulator auth) takes precedence over managed identity.
            return string.IsNullOrWhiteSpace(settings.ConnectionString)
                ? new ServiceBusClient(settings.NamespaceFqdn, new DefaultAzureCredential())
                : new ServiceBusClient(settings.ConnectionString);
        });

        services.AddScoped<IBatchDeleter, ServiceBusBatchDeleter>();
        services.AddScoped<IEventPublisher, ServiceBusEventPublisher>();
        services.AddScoped<IServiceBusStuckScanner, ServiceBusStuckScanner>();
        services.AddHostedService<SagaEventConsumer>();
    }

    private static void AddRabbitMqMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqSettings>(configuration.GetSection("RabbitMq"));
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<RabbitMqQueueScanner>();
        services.AddScoped<RabbitMqEventPublisher>();
        services.AddScoped<IEventPublisher>(sp => sp.GetRequiredService<RabbitMqEventPublisher>());
        services.AddScoped<IBatchDeleter, RabbitMqBatchDeleter>();
        services.AddScoped<IServiceBusStuckScanner, RabbitMqStuckScanner>();
        services.AddHostedService<RabbitMqSagaEventConsumer>();
    }
}
