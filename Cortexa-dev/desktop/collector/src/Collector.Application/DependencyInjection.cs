using Collector.Application.Auth;
using Collector.Application.Extraction;
using Collector.Application.Settings;
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
        services.AddCollectorExtraction();
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
}
