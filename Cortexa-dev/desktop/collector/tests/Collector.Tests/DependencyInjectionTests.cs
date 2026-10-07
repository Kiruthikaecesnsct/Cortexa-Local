using Collector.Application;
using Collector.Application.Auth;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Application.Upload;
using Collector.Domain.Enums;
using Collector.Infrastructure;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Auth;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Tests;

public class DependencyInjectionTests
{
    private static ServiceProvider Build(IReadOnlyDictionary<string, string?>? extraSettings = null)
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
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static List<Type> Chain(IHttpMessageHandlerFactory factory, string name)
    {
        var types = new List<Type>();
        HttpMessageHandler? handler = factory.CreateHandler(name);
        while (handler is not null)
        {
            types.Add(handler.GetType());
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        return types;
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
    public void KnowledgeExtractionOptions_ConfiguredAiConcurrency_IsTakenFromAiClaudeSection()
    {
        const int ConfiguredConcurrency = 7;
        var extra = new Dictionary<string, string?> { ["Ai:Claude:Concurrency"] = ConfiguredConcurrency.ToString() };
        using var provider = Build(extra);

        var options = provider.GetRequiredService<IOptions<KnowledgeExtractionOptions>>();

        Assert.Equal(ConfiguredConcurrency, options.Value.Concurrency);
    }

    [Fact]
    public void AiProvider_NoProviderSetting_ResolvesClaude()
    {
        using var provider = Build();

        Assert.IsType<ClaudeDirectProvider>(provider.GetRequiredService<IAiProvider>());
        Assert.Equal(CollectorProvider.Claude, provider.GetRequiredService<IOptions<KnowledgeExtractionOptions>>().Value.Provider);
    }

    [Theory]
    [InlineData("Claude", typeof(ClaudeDirectProvider), CollectorProvider.Claude)]
    [InlineData("Gemini", typeof(GeminiDirectProvider), CollectorProvider.Gemini)]
    public void AiProvider_ProviderSetting_ResolvesMatchingImplementation(string setting, Type expected, CollectorProvider wire)
    {
        using var provider = Build(new Dictionary<string, string?> { ["Ai:Provider"] = setting });

        Assert.IsType(expected, provider.GetRequiredService<IAiProvider>());
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
    public void KnowledgeExtractionOptions_GeminiSelected_TakesConcurrencyFromAiGeminiSection()
    {
        const int GeminiConcurrency = 3;
        var extra = new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "Gemini",
            ["Ai:Gemini:Concurrency"] = GeminiConcurrency.ToString(),
            ["Ai:Claude:Concurrency"] = "9",
        };
        using var provider = Build(extra);

        Assert.Equal(GeminiConcurrency, provider.GetRequiredService<IOptions<KnowledgeExtractionOptions>>().Value.Concurrency);
    }

    [Fact]
    public void AiOptions_UnsupportedProvider_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Ai:Provider"] = "Bedrock" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AiOptions>>().Value);
    }

    [Fact]
    public void GeminiOptions_InvalidThinking_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Ai:Gemini:Thinking"] = "minimal" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GeminiProviderOptions>>().Value);
    }
}
