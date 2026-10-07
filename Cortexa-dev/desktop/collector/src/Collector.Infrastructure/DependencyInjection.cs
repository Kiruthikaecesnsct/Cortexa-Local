using Collector.Application.Auth;
using Collector.Application.Ports;
using Collector.Infrastructure.Auth;
using Collector.Infrastructure.Cache;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Secrets;
using Collector.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure;

public static class DependencyInjection
{
    private static readonly string[] RedactedHeaders = ["Authorization", "Cookie", "Set-Cookie"];

    public static IServiceCollection AddCollectorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCollectorOptions(configuration);
        services.AddCollectorHttpClients();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISecretStore, CredentialManagerStore>();
        services.AddSingleton<IAuthClient, GatewayAuthClient>();
        services.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<ILocalCacheInitializer, SqliteCacheInitializer>();
        services.AddHostedService<TokenRefreshWorker>();
        return services;
    }

    private static void AddCollectorOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<GatewayOptions, GatewayOptionsValidator>(configuration, GatewayOptions.SectionName);
        services.AddValidatedOptions<CollectorServerOptions, CollectorServerOptionsValidator>(
            configuration,
            CollectorServerOptions.SectionName);
        services.AddValidatedOptions<CacheOptions, CacheOptionsValidator>(configuration, CacheOptions.SectionName);
        services.AddValidatedOptions<SecretsOptions, SecretsOptionsValidator>(configuration, SecretsOptions.SectionName);
        services.AddValidatedOptions<UserSettingsOptions, UserSettingsOptionsValidator>(
            configuration,
            UserSettingsOptions.SectionName);
        services.AddValidatedOptions<AuthOptions, AuthOptionsValidator>(configuration, AuthOptions.SectionName);
    }

    private static void AddValidatedOptions<TOptions, TValidator>(
        this IServiceCollection services,
        IConfiguration configuration,
        string section)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.AddOptions<TOptions>().Bind(configuration.GetSection(section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
    }

    private static void AddCollectorHttpClients(this IServiceCollection services)
    {
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient(HttpClientNames.CortexaAuth)
            .ConfigureHttpClient((sp, client) =>
                client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<AuthOptions>>().Value.HttpTimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(CreateCookielessHandler)
            .RedactLoggedHeaders(name => RedactedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase));
        services.AddHttpClient(HttpClientNames.CollectorServer)
            .ConfigurePrimaryHttpMessageHandler(CreateCookielessHandler)
            .AddHttpMessageHandler<BearerTokenHandler>()
            .RedactLoggedHeaders(name => RedactedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    private static SocketsHttpHandler CreateCookielessHandler() => new() { UseCookies = false };
}
