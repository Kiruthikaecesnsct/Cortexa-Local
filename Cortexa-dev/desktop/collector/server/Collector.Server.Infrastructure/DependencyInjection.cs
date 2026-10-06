using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Collector.Server.Application.Ports;
using Collector.Server.Infrastructure.Cosmos;
using Collector.Server.Infrastructure.Health;
using Collector.Server.Infrastructure.Identity;
using Collector.Server.Infrastructure.Messaging.RabbitMq;
using Collector.Server.Infrastructure.Messaging.ServiceBus;
using Collector.Server.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCollectorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        AddValidatedOptions(services, configuration);
        AddCosmos(services);
        AddIdentity(services);
        AddMessaging(services, configuration);
        AddHealth(services);
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }

    private static void AddValidatedOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CosmosOptions>()
            .Bind(configuration.GetSection(CosmosOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CosmosOptions>, CosmosOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        services.AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IdentityOptions>, IdentityOptionsValidator>());
    }

    private static void AddCosmos(IServiceCollection services)
    {
        services.AddSingleton<CosmosClient>(provider =>
            CosmosClientFactory.Create(provider.GetRequiredService<IOptions<CosmosOptions>>().Value));
        services.AddSingleton<IPipelineRowStore, CosmosPipelineRowStore>();
        services.AddSingleton<IModelConfigReader, CosmosModelConfigReader>();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient<IUserStatusReader, HttpUserStatusReader>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.StatusTimeoutSeconds);
        });
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        var messaging = configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new MessagingOptions();

        if (messaging.Provider == MessagingProvider.ServiceBus)
        {
            AddServiceBus(services);
            return;
        }

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IBrokerProbe>(provider => provider.GetRequiredService<RabbitMqConnectionProvider>());
        services.AddSingleton<IIngestionEventPublisher, RabbitMqIngestionEventPublisher>();
    }

    private static void AddServiceBus(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value.ServiceBus;
            return string.IsNullOrWhiteSpace(options.ConnectionString)
                ? new ServiceBusClient(options.FullyQualifiedNamespace, new DefaultAzureCredential())
                : new ServiceBusClient(options.ConnectionString);
        });
        services.AddSingleton<ServiceBusIngestionEventPublisher>();
        services.AddSingleton<IIngestionEventPublisher>(provider => provider.GetRequiredService<ServiceBusIngestionEventPublisher>());
        services.AddSingleton<IBrokerProbe>(provider => provider.GetRequiredService<ServiceBusIngestionEventPublisher>());
    }

    private static void AddHealth(IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<CosmosHealthCheck>("cosmos", tags: [HealthTags.Ready])
            .AddCheck<BrokerHealthCheck>("broker", tags: [HealthTags.Ready]);
    }
}
