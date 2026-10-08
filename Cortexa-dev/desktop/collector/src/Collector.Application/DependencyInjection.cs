using Collector.Application.Auth;
using Collector.Application.Extraction;
using Collector.Application.History;
using Collector.Application.Knowledge;
using Collector.Application.Settings;
using Collector.Application.Upload;
using Microsoft.Extensions.DependencyInjection;

namespace Collector.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCollectorApplication(this IServiceCollection services)
    {
        services.AddSingleton<TokenRefreshPolicy>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<ISignInService>(sp => sp.GetRequiredService<SessionService>());
        services.AddSingleton<IAccessTokenProvider>(sp => sp.GetRequiredService<SessionService>());
        services.AddSingleton<ISessionState>(sp => sp.GetRequiredService<SessionService>());
        services.AddSingleton<SettingsService>();
        services.AddSingleton<LocalSourceResolver>();
        services.AddCollectorExtraction();
        services.AddCollectorKnowledge();
        return services;
    }

    private static void AddCollectorExtraction(this IServiceCollection services)
    {
        services.AddSingleton<TextNormalizer>();
        services.AddSingleton<HeadingDetector>();
        services.AddSingleton<CodeSplitter>();
        services.AddSingleton<FileContentGuard>();
        services.AddSingleton<UnitBuilder>();
        services.AddSingleton<DocumentStatusRules>();
        services.AddSingleton<ExtractionService>();
    }

    private static void AddCollectorKnowledge(this IServiceCollection services)
    {
        services.AddOptions<KnowledgeExtractionOptions>();
        services.AddSingleton(_ => KnowledgePromptLoader.Load());
        services.AddSingleton<UntrustedSourceGuard>();
        services.AddSingleton<KnowledgePromptBuilder>();
        services.AddSingleton<KnowledgeParser>();
        services.AddSingleton<AnchorLocator>();
        services.AddSingleton<ExcerptCutter>();
        services.AddSingleton<IdentifierEchoDetector>();
        services.AddSingleton<KnowledgeMerger>();
        services.AddSingleton<UnitSplitter>();
        services.AddSingleton<UnitItemAssembler>();
        services.AddSingleton<UnitExtractionRunner>();
        services.AddSingleton<ExtractKnowledgeHandler>();
        services.AddSingleton<UploadBatchPlanner>();
        services.AddSingleton<UploadKnowledgeHandler>();
    }
}
