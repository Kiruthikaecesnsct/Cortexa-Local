using Collector.Application;
using Collector.Application.Auth;
using Collector.Application.Extraction;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Remote.Selection;
using Collector.Application.Settings;
using Collector.Application.Upload;
using Collector.Domain.Enums;
using Collector.Infrastructure;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Auth;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote;
using Collector.Infrastructure.Remote.AzureDevOps;
using Collector.Infrastructure.Remote.Cortexa;
using Collector.Infrastructure.Remote.GitHub;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Infrastructure.Remote.Ssh;
using Collector.Presentation.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Tests;

public class DependencyInjectionTests
{
    private static ServiceProvider Build(
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Gateway:BaseUrl"] = "https://gateway.example",
            ["CollectorServer:BaseUrl"] = "https://server.example",
        };
        foreach (var pair in extraSettings ?? new Dictionary<string, string?>())
        {
            settings[pair.Key] = pair.Value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCollectorApplication();
        services.AddCollectorInfrastructure(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static List<HttpMessageHandler> Handlers(IHttpMessageHandlerFactory factory, string name)
    {
        var handlers = new List<HttpMessageHandler>();
        HttpMessageHandler? handler = factory.CreateHandler(name);
        while (handler is not null)
        {
            handlers.Add(handler);
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        return handlers;
    }

    private static List<Type> Chain(IHttpMessageHandlerFactory factory, string name) =>
        [.. Handlers(factory, name).Select(handler => handler.GetType())];

    [Fact]
    public void RemoteRepositoryClients_Always_ResolvesEveryProvider()
    {
        using var provider = Build();
        var clients = provider.GetRequiredService<IRemoteRepositoryClients>();

        Assert.IsType<GitHubRepositoryClient>(clients.For(SourceType.Github));
        Assert.IsType<AzureDevOpsRepositoryClient>(clients.For(SourceType.AzureDevops));
        Assert.IsType<SftpRepositoryClient>(clients.For(SourceType.Ssh));
        Assert.IsType<CortexaRepositoryClient>(clients.For(SourceType.CortexaRepo));
    }

    [Fact]
    public void RemoteRepositoryClients_Cortexa_ReleasesArchivesThroughFetchCompletion()
    {
        using var provider = Build();
        var clients = provider.GetRequiredService<IRemoteRepositoryClients>();

        Assert.IsAssignableFrom<IRemoteFetchCompletion>(clients.For(SourceType.CortexaRepo));
    }

    [Fact]
    public void RemoteRepositoryClients_UnsupportedProvider_Throws()
    {
        using var provider = Build();
        var clients = provider.GetRequiredService<IRemoteRepositoryClients>();

        Assert.Throws<NotSupportedException>(() => clients.For(SourceType.Local));
    }

    [Fact]
    public void RemoteServices_Always_Resolve()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<RemoteFetchService>());
        Assert.NotNull(provider.GetRequiredService<IRemoteFileStore>());
        Assert.NotNull(provider.GetRequiredService<IRemoteFileCache>());
    }

    [Fact]
    public void RateLimitMonitor_Always_SharesTheGateCollectionAndStartsRunning()
    {
        using var provider = Build();

        var monitor = provider.GetRequiredService<IRateLimitMonitor>();

        Assert.Same(provider.GetRequiredService<RateLimitGates>(), monitor);
        Assert.False(monitor.GetStatus(SourceType.Github).IsPaused);
        Assert.False(monitor.GetStatus(SourceType.AzureDevops).IsPaused);
    }

    [Theory]
    [InlineData(HttpClientNames.GitHub)]
    [InlineData(HttpClientNames.AzureDevOps)]
    public void RemoteClients_Always_AuthenticateBeforeRateLimiting(string name)
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var chain = Chain(factory, name);

        Assert.Contains(typeof(PatAuthHandler), chain);
        Assert.Contains(typeof(RateLimitHandler), chain);
        Assert.True(chain.IndexOf(typeof(PatAuthHandler)) < chain.IndexOf(typeof(RateLimitHandler)));
    }

    [Theory]
    [InlineData(HttpClientNames.GitHub)]
    [InlineData(HttpClientNames.AzureDevOps)]
    public void RemoteClients_Always_UseNonRedirectingCookielessPrimaryHandler(string name)
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var primary = Assert.IsType<SocketsHttpHandler>(Handlers(factory, name).Last());

        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);
    }

    [Theory]
    [InlineData(HttpClientNames.CortexaAuth)]
    [InlineData(HttpClientNames.CollectorServer)]
    public void ExistingClients_Always_DoNotCarryRemoteSourceHandlers(string name)
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var chain = Chain(factory, name);

        Assert.DoesNotContain(typeof(PatAuthHandler), chain);
        Assert.DoesNotContain(typeof(RateLimitHandler), chain);
    }

    [Fact]
    public void RemoteSourceOptions_InvalidBaseUrl_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string?> { ["RemoteSources:GitHub:BaseUrl"] = "http://insecure.example" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RemoteSourceOptions>>().Value);
    }

    [Fact]
    public void Graph_resolves_and_session_interfaces_share_one_instance()
    {
        using var provider = Build();

        var signIn = provider.GetRequiredService<ISignInService>();

        Assert.Same(signIn, provider.GetRequiredService<ISessionState>());
        Assert.Same(signIn, provider.GetRequiredService<IAccessTokenProvider>());
        Assert.NotNull(provider.GetRequiredService<SettingsService>());
    }

    [Fact]
    public void Bearer_handler_is_only_on_the_collector_server_client()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        Assert.DoesNotContain(typeof(BearerTokenHandler), Chain(factory, HttpClientNames.CortexaAuth));
        Assert.Contains(typeof(BearerTokenHandler), Chain(factory, HttpClientNames.CollectorServer));
    }

    [Fact]
    public void Auth_client_disables_cookies()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var primary = Chain(factory, HttpClientNames.CortexaAuth).Last();

        Assert.Equal(typeof(SocketsHttpHandler), primary);
    }

    [Fact]
    public void Graph_Always_ResolvesKnowledgeAndUploadServices()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<ExtractKnowledgeHandler>());
        Assert.NotNull(provider.GetRequiredService<UploadKnowledgeHandler>());
        Assert.Equal("knowledge.v1", provider.GetRequiredService<KnowledgePrompt>().Version);
    }

    [Fact]
    public void KnowledgeExtractionOptions_MirrorsConfiguredProvider()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Ai:Provider"] = "Gemini" });

        var options = provider.GetRequiredService<IOptions<KnowledgeExtractionOptions>>();

        Assert.Equal(CollectorProvider.Gemini, options.Value.Provider);
    }

    [Fact]
    public void AiProvider_NoProviderSetting_ResolvesClaude()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IAiProviderFactory>();
        var options = provider.GetRequiredService<IOptions<KnowledgeExtractionOptions>>();

        Assert.Equal(CollectorProvider.Claude, options.Value.Provider);
        Assert.IsType<ClaudeDirectProvider>(factory.Resolve(options.Value.Provider));
    }

    [Theory]
    [InlineData("Claude", typeof(ClaudeDirectProvider), CollectorProvider.Claude)]
    [InlineData("Gemini", typeof(GeminiDirectProvider), CollectorProvider.Gemini)]
    [InlineData("Bedrock", typeof(BedrockDirectProvider), CollectorProvider.Bedrock)]
    public void AiProvider_ProviderSetting_ResolvesMatchingImplementation(string setting, Type expected, CollectorProvider wire)
    {
        using var provider = Build(new Dictionary<string, string?> { ["Ai:Provider"] = setting });
        var factory = provider.GetRequiredService<IAiProviderFactory>();

        Assert.IsType(expected, factory.Resolve(wire));
        Assert.Equal(wire, provider.GetRequiredService<IOptions<KnowledgeExtractionOptions>>().Value.Provider);
    }

    [Fact]
    public void AiProvider_BothImplementations_AreBuildable()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<ClaudeDirectProvider>());
        Assert.NotNull(provider.GetRequiredService<GeminiDirectProvider>());
    }

    [Fact]
    public void AiProviderFactory_Bedrock_ResolvesStubProvider()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IAiProviderFactory>();

        Assert.IsType<BedrockDirectProvider>(factory.Resolve(CollectorProvider.Bedrock));
    }

    [Fact]
    public void AiProviderFactory_ConcurrencyFor_ReadsPerProviderSections()
    {
        var extra = new Dictionary<string, string?>
        {
            ["Ai:Gemini:Concurrency"] = "3",
            ["Ai:Claude:Concurrency"] = "9",
            ["Ai:Bedrock:Concurrency"] = "6",
            ["Ai:Bedrock:SsoStartUrl"] = "https://example.awsapps.com/start",
            ["Ai:Bedrock:SsoRegion"] = "us-east-1",
            ["Ai:Bedrock:AccountId"] = "123456789012",
            ["Ai:Bedrock:SsoRoleName"] = "CortexaBedrockRole",
            ["Ai:Bedrock:Region"] = "us-east-1",
        };
        using var provider = Build(extra);
        var factory = provider.GetRequiredService<IAiProviderFactory>();

        Assert.Equal(3, factory.ConcurrencyFor(CollectorProvider.Gemini));
        Assert.Equal(9, factory.ConcurrencyFor(CollectorProvider.Claude));
        Assert.Equal(6, factory.ConcurrencyFor(CollectorProvider.Bedrock));
    }

    [Fact]
    public void AiOptions_BedrockProvider_PassesValidation()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Ai:Provider"] = "Bedrock" });

        Assert.Equal(CollectorProvider.Bedrock, provider.GetRequiredService<IOptions<AiOptions>>().Value.Provider);
    }

    [Fact]
    public void GeminiOptions_InvalidThinking_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Ai:Gemini:Thinking"] = "minimal" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GeminiProviderOptions>>().Value);
    }

    [Fact]
    public void ExtractionOptions_NoSetting_DefaultsToFourParallelSplits()
    {
        using var provider = Build();

        Assert.Equal(4, provider.GetRequiredService<IOptions<ExtractionOptions>>().Value.MaxParallelSplits);
    }

    [Fact]
    public void ExtractionOptions_ConfiguredSection_BindsMaxParallelSplits()
    {
        const int Configured = 7;
        using var provider = Build(new Dictionary<string, string?> { ["Extraction:MaxParallelSplits"] = Configured.ToString() });

        Assert.Equal(Configured, provider.GetRequiredService<IOptions<ExtractionOptions>>().Value.MaxParallelSplits);
    }

    [Fact]
    public void ParallelSplitter_Always_ResolvesAsASingleton()
    {
        using var provider = Build();

        var splitter = provider.GetRequiredService<ParallelSplitter>();

        Assert.Same(splitter, provider.GetRequiredService<ParallelSplitter>());
    }

    [Fact]
    public void FileTreeBuilder_Always_ResolvesAsASingleton()
    {
        using var provider = Build();

        var builder = provider.GetRequiredService<FileTreeBuilder>();

        Assert.Same(builder, provider.GetRequiredService<FileTreeBuilder>());
    }

    [Fact]
    public void IntakeProgressViewModel_RegisteredAsASingleton_ResolvesFromTheSharedGraph()
    {
        using var provider = Build(configure: services => services.AddSingleton<IntakeProgressViewModel>());

        var intake = provider.GetRequiredService<IntakeProgressViewModel>();

        Assert.Same(intake, provider.GetRequiredService<IntakeProgressViewModel>());
    }

    [Fact]
    public void Retry_handler_resolves_without_extraction_or_ai_dependencies()
    {
        using var provider = Build();

        var handler = provider.GetRequiredService<RetryFailedUploadHandler>();

        Assert.Same(handler, provider.GetRequiredService<RetryFailedUploadHandler>());
        Assert.Empty(ForbiddenDependencies(typeof(RetryFailedUploadHandler)));
    }

    private static List<Type> ForbiddenDependencies(Type root)
    {
        var forbidden = new List<Type>();
        var pending = new Queue<Type>([root]);
        var seen = new HashSet<Type>();
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!seen.Add(current) || current.Namespace?.StartsWith("Collector.Application", StringComparison.Ordinal) != true)
            {
                continue;
            }

            if (IsForbidden(current))
            {
                forbidden.Add(current);
            }

            foreach (var parameter in current.GetConstructors().SelectMany(ctor => ctor.GetParameters()))
            {
                pending.Enqueue(parameter.ParameterType);
            }
        }

        return forbidden;
    }

    private static bool IsForbidden(Type type) =>
        type == typeof(UploadKnowledgeHandler)
        || type == typeof(ExtractKnowledgeHandler)
        || type == typeof(IAiProvider)
        || type == typeof(IAiProviderFactory)
        || type.Namespace is "Collector.Application.Knowledge" or "Collector.Application.Extraction";

    [Theory]
    [InlineData(HttpClientNames.GitHub)]
    [InlineData(HttpClientNames.AzureDevOps)]
    public void RemoteHttpClients_Always_HaveInfiniteClientTimeout(string name)
    {
        using var provider = Build();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
}
