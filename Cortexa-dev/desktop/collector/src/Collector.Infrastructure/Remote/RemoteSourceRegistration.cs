using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Secrets;
using Collector.Domain.Enums;
using Collector.Infrastructure.Cache;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote.AzureDevOps;
using Collector.Infrastructure.Remote.GitHub;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Infrastructure.Remote.Ssh;
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
        services.AddSingleton<RateLimitGates>();
        services.AddSingleton<IRateLimitMonitor>(sp => sp.GetRequiredService<RateLimitGates>());
        services.AddSingleton<GitHubRateHeaders>();
        services.AddSingleton<AzureDevOpsRateHeaders>();
        services.AddSingleton<GitHubErrorMapper>();
        services.AddSingleton<AzureDevOpsErrorMapper>();
        services.AddSingleton<IRemoteRepositoryClient, GitHubRepositoryClient>();
        services.AddSingleton<IRemoteRepositoryClient, AzureDevOpsRepositoryClient>();
        services.AddCollectorSshRemoteSource();
        services.AddSingleton<IRemoteRepositoryClients, RemoteRepositoryClients>();
        services.AddRemoteClient(new RemoteClientSpec(
            HttpClientNames.GitHub,
            SourceType.Github,
            PatScheme.Bearer,
            options => options.GitHub,
            sp => sp.GetRequiredService<GitHubRateHeaders>()));
        services.AddRemoteClient(new RemoteClientSpec(
            HttpClientNames.AzureDevOps,
            SourceType.AzureDevops,
            PatScheme.Basic,
            options => options.AzureDevOps,
            sp => sp.GetRequiredService<AzureDevOpsRateHeaders>()));
        return services;
    }

    private static void AddCollectorSshRemoteSource(this IServiceCollection services)
    {
        services.AddSingleton<SftpConnectionFactory>();
        services.AddSingleton<IRemoteRepositoryClient, SftpRepositoryClient>();
    }

    private static void AddRemoteClient(this IServiceCollection services, RemoteClientSpec spec) =>
        services.AddHttpClient(spec.Name)
            .ConfigureHttpClient((sp, client) => Configure(sp, client, spec))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
            .AddHttpMessageHandler(sp => CreatePatHandler(sp, spec))
            .AddHttpMessageHandler(sp => CreateRateLimitHandler(sp, spec))
            .RedactLoggedHeaders(name => RedactedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase));

    private static void Configure(IServiceProvider sp, HttpClient client, RemoteClientSpec spec)
    {
        var options = sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value;
        client.BaseAddress = RemoteUrl.BaseAddress(spec.Select(options).BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }

    private static PatAuthHandler CreatePatHandler(IServiceProvider sp, RemoteClientSpec spec)
    {
        var options = sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value;
        var slot = RemoteSourceSlots.For(spec.Provider)
            ?? throw new InvalidOperationException($"Source {spec.Provider} has no token slot.");
        var target = new PatTarget(spec.Provider, RemoteUrl.BaseAddress(spec.Select(options).BaseUrl));
        return new PatAuthHandler(sp.GetRequiredService<ISecretStore>(), slot, spec.Scheme, target);
    }

    private static RateLimitHandler CreateRateLimitHandler(IServiceProvider sp, RemoteClientSpec spec)
    {
        var options = sp.GetRequiredService<IOptions<RemoteSourceOptions>>().Value;
        var gate = sp.GetRequiredService<RateLimitGates>().For(spec.Provider);
        var settings = new RateLimitHandlerSettings(options.RateLimit, sp.GetRequiredService<TimeProvider>());
        return new RateLimitHandler(spec.Provider, gate, spec.Reader(sp), settings);
    }

    private sealed record RemoteClientSpec(
        string Name,
        SourceType Provider,
        PatScheme Scheme,
        Func<RemoteSourceOptions, RemoteProviderOptions> Select,
        Func<IServiceProvider, IRateHeaderReader> Reader);
}
