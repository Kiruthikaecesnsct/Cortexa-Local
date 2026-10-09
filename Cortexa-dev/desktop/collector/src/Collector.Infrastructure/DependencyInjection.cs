using Collector.Application.Auth;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Auth;
using Collector.Infrastructure.Cache;
using Collector.Infrastructure.Export;
using Collector.Infrastructure.Extraction;
using Collector.Infrastructure.History;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote;
using Collector.Infrastructure.Secrets;
using Collector.Infrastructure.Settings;
using Collector.Infrastructure.Upload;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure;

public static class DependencyInjection
{
    private static readonly string[] RedactedHeaders = ["Authorization", "Cookie", "Set-Cookie", "x-goog-api-key"];

    public static IServiceCollection AddCollectorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCollectorOptions(configuration);
        services.AddCollectorHttpClients();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISecretStore, CredentialManagerStore>();
        services.AddSingleton<IGeminiKeyStore, GeminiKeyStore>();
        services.AddSingleton<IGeminiKeyStatusStore, GeminiKeyStatusStore>();
        services.AddSingleton<IAuthClient, GatewayAuthClient>();
        services.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<ILocalCacheInitializer, SqliteCacheInitializer>();
        services.AddSingleton<IDocumentStore, SqliteDocumentStore>();
        services.AddSingleton<IUnitStore, SqliteUnitStore>();
        services.AddSingleton<IBatchStore, SqliteBatchStore>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<IDocxTextExtractor, OpenXmlTextExtractor>();
        services.AddSingleton<ITokenCounter, MlTokenizerCounter>();
        services.AddAiProviders();
        services.AddSingleton<IKnowledgeUploadClient, CollectorUploadClient>();
        services.AddSingleton<IKnowledgePdfExporter, QuestPdfKnowledgeExporter>();
        services.AddSingleton<HistoryRetryPolicy>();
        services.AddSingleton<IBatchHistoryClient, CollectorHistoryClient>();
        services.AddCollectorRemoteSources(configuration);
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
        services.AddValidatedOptions<HistoryOptions, HistoryOptionsValidator>(configuration, HistoryOptions.SectionName);
        services.AddValidatedOptions<SecretsOptions, SecretsOptionsValidator>(configuration, SecretsOptions.SectionName);
        services.AddValidatedOptions<UserSettingsOptions, UserSettingsOptionsValidator>(
            configuration,
            UserSettingsOptions.SectionName);
        services.AddValidatedOptions<AuthOptions, AuthOptionsValidator>(configuration, AuthOptions.SectionName);
        services.AddValidatedOptions<AiProviderOptions, AiProviderOptionsValidator>(
            configuration,
            AiProviderOptions.SectionName);
        services.AddValidatedOptions<GeminiProviderOptions, GeminiProviderOptionsValidator>(
            configuration,
            GeminiProviderOptions.SectionName);
        services.AddValidatedOptions<BedrockProviderOptions, BedrockProviderOptionsValidator>(
            configuration,
            BedrockProviderOptions.SectionName);
        services.AddValidatedOptions<AiOptions, AiOptionsValidator>(configuration, AiOptions.SectionName);
        services.AddOptions<AiModelChoiceOptions>().Bind(configuration.GetSection(AiModelChoiceOptions.SectionName));
        services.AddOptions<GeminiRotationOptions>().Bind(configuration.GetSection(GeminiRotationOptions.SectionName));
        services.AddSingleton<IProviderModelCatalog, ProviderModelCatalogAdapter>();
        services.AddOptions<ProviderModelCatalog>().Bind(configuration.GetSection(ProviderModelCatalog.SectionName));
        services.AddOptions<KnowledgeExtractionOptions>()
            .Configure<IOptions<AiOptions>>((extraction, ai) => AiProviderSelection.Apply(extraction, ai.Value));
    }

    private static void AddAiProviders(this IServiceCollection services)
    {
        services.AddSingleton<AnthropicClientFactory>();
        services.AddSingleton<ClaudeDirectProvider>();
        services.AddSingleton<GeminiClientFactory>();
        services.AddSingleton<GeminiDirectProvider>();
        services.AddSingleton<IBedrockSsoClientFactory, BedrockSsoClientFactory>();
        services.AddSingleton(sp => new BedrockSsoCredentialsDependencies(
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<BedrockSsoCredentials>>(),
            sp.GetRequiredService<IBedrockSsoClientFactory>()));
        services.AddSingleton<BedrockSsoCredentials>();
        services.AddSingleton<IBedrockSsoCredentials>(sp => sp.GetRequiredService<BedrockSsoCredentials>());
        services.AddSingleton<BedrockClientFactory>();
        services.AddSingleton<BedrockDirectProvider>();
        services.AddSingleton<IAiProviderFactory, AiProviderSelection>();
    }

    internal static void AddValidatedOptions<TOptions, TValidator>(
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
        services.AddHttpClient(HttpClientNames.Gemini)
            .ConfigureHttpClient((sp, client) =>
                client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<GeminiProviderOptions>>().Value.TimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(CreateCookielessHandler)
            .RedactLoggedHeaders(name => RedactedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    private static SocketsHttpHandler CreateCookielessHandler() => new() { UseCookies = false };
}
