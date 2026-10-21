using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Infrastructure.Auth;
using Collector.Infrastructure.Cache;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote.AzureDevOps;
using Collector.Infrastructure.Remote.Cortexa;
using Collector.Infrastructure.Remote.GitHub;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Infrastructure.Remote.Ssh;
using Collector.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote;

internal static class RemoteSourceRegistration
{
    private static readonly string[] RedactedHeaders = ["Authorization", "Cookie", "Set-Cookie"];

    public static IServiceCollection AddCollectorRemoteSources(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatedOptions<RemoteSourceOptions, RemoteSourceOptionsValidator>(
            configuration,
            RemoteSourceOptions.SectionName);
        services.AddOptions<RemoteFetchOptions>().Bind(configuration.GetSection(RemoteFetchOptions.SectionName));
        services.AddSingleton<IRemoteFileStore, SqliteRemoteFileStore>();
        services.AddSingleton<IRemoteFileCache, DiskRemoteFileCache>();
        services.AddSingleton<ISessionCredentials, InMemorySessionCredentials>();
        services.AddSingleton<RateLimitGates>();
        services.AddSingleton<IRateLimitMonitor>(sp => sp.GetRequiredService<RateLimitGates>());
        services.AddSingleton<GitHubRateHeaders>();
        services.AddSingleton<AzureDevOpsRateHeaders>();
        services.AddSingleton<GitHubErrorMapper>();
        services.AddSingleton<AzureDevOpsErrorMapper>();
        services.AddSingleton<IRemoteRepositoryClient, GitHubRepositoryClient>();
        services.AddSingleton<IRemoteRepositoryClient, AzureDevOpsRepositoryClient>();
        services.AddCollectorSshRemoteSource();
        services.AddCollectorCortexaRemoteSource();
        services.AddGitHubOAuth();
        services.AddAzureDevOpsEntra();
        services.AddSingleton<IRemoteRepositoryClients, RemoteRepositoryClients>();
        services.AddRemoteClient(new RemoteClientSpec(
            HttpClientNames.GitHub,
            SourceType.Github,
            PatScheme.Bearer,
            options => options.GitHub,
            sp => sp.GetRequiredService<GitHubRateHeaders>(),
            CreateTokenHandler));
        services.AddRemoteClient(new RemoteClientSpec(
            HttpClientNames.AzureDevOps,
            SourceType.AzureDevops,
            PatScheme.Basic,
            options => options.AzureDevOps,
            sp => sp.GetRequiredService<AzureDevOpsRateHeaders>(),
            CreateTokenHandler));
        return services;
    }

    private static void AddCollectorSshRemoteSource(this IServiceCollection services)
    {
        services.AddSingleton<SftpConnectionFactory>();
        services.AddSingleton<SftpRepositoryClient>();
        services.AddSingleton<IRemoteRepositoryClient>(sp => sp.GetRequiredService<SftpRepositoryClient>());
        services.AddSingleton<ISshConnectionCloser>(sp => sp.GetRequiredService<SftpRepositoryClient>());
    }

    private static void AddGitHubOAuth(this IServiceCollection services)
    {
        services.AddSingleton<GitHubApiTransport>();
        services.AddSingleton<GitHubInstallationLister>();
        services.AddSingleton<IDeviceAuthorizationClient, GitHubDeviceAuthorizationClient>();
        services.AddSingleton<IOAuthTokenVault, CredentialManagerOAuthVault>();
        services.AddSingleton(sp => CreateAvailability(sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value));
        services.AddSingleton(sp => new OAuthRefreshPolicy(TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value.GitHub.TokenRefreshSkewSeconds)));
        services.AddHttpClient(HttpClientNames.GitHubAuth)
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value;
                client.BaseAddress = RemoteUrl.BaseAddress(options.GitHub.AuthBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
            .RedactLoggedHeaders(name => RedactedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    private static void AddAzureDevOpsEntra(this IServiceCollection services)
    {
        services.AddSingleton<EntraTokenCacheStore>();
        services.AddSingleton<MsalCacheBinding>();
        services.AddSingleton<IEntraIdAuthClient, MsalEntraIdAuthClient>();
    }

    private static RemoteAuthAvailability CreateAvailability(RemoteSourceOptions options) => new(
        !string.IsNullOrWhiteSpace(options.GitHub.OAuthClientId),
        !string.IsNullOrWhiteSpace(options.GitHub.AppClientId),
        !string.IsNullOrWhiteSpace(options.AzureDevOps.EntraClientId));

    private static void AddCollectorCortexaRemoteSource(this IServiceCollection services)
    {
        services.AddSingleton<CortexaErrorMapper>();
        services.AddSingleton<CortexaGatewayHttp>();
        services.AddSingleton<CortexaArchiveStore>();
        services.AddSingleton<IRemoteRepositoryClient, CortexaRepositoryClient>();
        services.AddHttpClient(HttpClientNames.CortexaGateway)
            .ConfigureHttpClient((sp, client) =>
            {
                client.BaseAddress = RemoteUrl.BaseAddress(sp.GetRequiredService<IOptions<GatewayOptions>>().Value.BaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
            .AddHttpMessageHandler<BearerTokenHandler>()
            .RedactLoggedHeaders(name => RedactedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    private static void AddRemoteClient(this IServiceCollection services, RemoteClientSpec spec) =>
        services.AddHttpClient(spec.Name)
            .ConfigureHttpClient((sp, client) => Configure(sp, client, spec))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
            .AddHttpMessageHandler(sp => spec.AuthHandler(sp, spec))
            .AddHttpMessageHandler(sp => CreateRateLimitHandler(sp, spec))
            .RedactLoggedHeaders(name => RedactedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase));

    private static void Configure(IServiceProvider sp, HttpClient client, RemoteClientSpec spec)
    {
        var options = sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value;
        client.BaseAddress = RemoteUrl.BaseAddress(spec.Select(options).BaseUrl);
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    private static RemoteTokenAuthHandler CreateTokenHandler(IServiceProvider sp, RemoteClientSpec spec)
    {
        var options = sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value;
        var target = new PatTarget(spec.Provider, RemoteUrl.BaseAddress(spec.Select(options).BaseUrl));
        return new RemoteTokenAuthHandler(sp.GetRequiredService<IRemoteTokenResolver>(), spec.Scheme, target);
    }

    private static RateLimitHandler CreateRateLimitHandler(IServiceProvider sp, RemoteClientSpec spec)
    {
        var options = sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value;
        var gate = sp.GetRequiredService<RateLimitGates>().For(spec.Provider);
        var settings = new RateLimitHandlerSettings(
            options.RateLimit,
            sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(options.TimeoutSeconds),
            spec.Select(options).MaxRateLimitRetries ?? options.RateLimit.MaxRetries);
        return new RateLimitHandler(spec.Provider, gate, spec.Reader(sp), settings);
    }

    private sealed record RemoteClientSpec(
        string Name,
        SourceType Provider,
        PatScheme Scheme,
        Func<RemoteSourceOptions, RemoteProviderOptions> Select,
        Func<IServiceProvider, IRateHeaderReader> Reader,
        Func<IServiceProvider, RemoteClientSpec, DelegatingHandler> AuthHandler);
}
