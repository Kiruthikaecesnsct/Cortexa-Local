using Collector.Application.Auth;
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
        return services;
    }
}
