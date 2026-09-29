using Cortexa.ModelRouter.Application.Configuration;
using Cortexa.ModelRouter.Application.Interfaces;
using Cortexa.ModelRouter.Application.Services;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Anthropic;
using Cortexa.ModelRouter.Infrastructure.Providers.Foundry;
using Cortexa.ModelRouter.Infrastructure.Security;
using Cortexa.ModelRouter.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Cortexa.ModelRouter.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddModelRouterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FoundrySettings>(configuration.GetSection("Foundry"));
        services.Configure<AnthropicSettings>(configuration.GetSection("Anthropic"));
        services.Configure<KeyResolverSettings>(configuration.GetSection("KeyResolver"));
        services.Configure<ModelCatalogSettings>(configuration.GetSection("ModelCatalog"));

        var routerSettings = new RouterSettings();
        configuration.GetSection("Router").Bind(routerSettings);
        var modeOverride = configuration["MODEL_MODE"];
        if (!string.IsNullOrWhiteSpace(modeOverride))
            routerSettings.Mode = modeOverride;

        services.AddSingleton(routerSettings);
        services.AddSingleton<IProviderKeyResolver, KeyVaultProviderKeyResolver>();
        services.AddSingleton<IFoundryConcurrencyLimiter, FoundryConcurrencyLimiter>();
        services.AddHttpClient<FoundryProvider>()
            .ConfigureHttpClient((sp, client) =>
            {
                var foundrySettings = sp.GetRequiredService<IOptions<FoundrySettings>>().Value;
                client.Timeout = TimeSpan.FromSeconds(foundrySettings.TimeoutSeconds);
            })
            .AddResilienceHandler("foundry-retry", (builder, context) =>
            {
                var foundrySettings = context.ServiceProvider.GetRequiredService<IOptions<FoundrySettings>>().Value;
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = foundrySettings.MaxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(foundrySettings.RetryBaseDelayMs),
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(response =>
                        {
                            var statusCode = (int)response.StatusCode;
                            return statusCode >= 500 || statusCode == 429;
                        })
                });
            });
        services.AddHttpClient<AnthropicProvider>();
        services.AddTransient<IGroundingValidator, GroundingValidator>();
        services.AddTransient<IModelCatalog>(sp =>
        {
            var catalogSettings = new ModelCatalogSettings();
            configuration.GetSection("ModelCatalog").Bind(catalogSettings);
            return new ModelCatalog(catalogSettings);
        });
        services.AddTransient<IProviderRouter>(sp =>
        {
            var foundry = sp.GetRequiredService<FoundryProvider>();
            var anthropic = sp.GetRequiredService<AnthropicProvider>();
            var catalog = sp.GetRequiredService<IModelCatalog>();
            var grounding = sp.GetRequiredService<IGroundingValidator>();
            var settings = sp.GetRequiredService<RouterSettings>();
            var logger = sp.GetRequiredService<ILogger<ProviderRouter>>();
            return new ProviderRouter(foundry, anthropic, catalog, grounding, settings, logger);
        });

        return services;
    }
}
