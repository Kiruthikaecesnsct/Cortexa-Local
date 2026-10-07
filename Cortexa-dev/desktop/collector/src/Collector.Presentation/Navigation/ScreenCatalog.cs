using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Collector.Presentation.Navigation;

public static class ScreenCatalog
{
    public static IServiceCollection AddScreens(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddScreen(new ScreenRegistration
        {
            Key = ScreenKeys.SignIn,
            Title = ShellStrings.SignIn,
            ViewModelType = typeof(SignInViewModel),
            RequiresSignIn = false,
            Glyph = Glyphs.SignIn,
            Placement = NavPlacement.Main,
            Order = 0,
        });
        services.AddScreen(new ScreenRegistration
        {
            Key = ScreenKeys.Extract,
            Title = ShellStrings.Extract,
            ViewModelType = typeof(ExtractionViewModel),
            RequiresSignIn = false,
            Glyph = Glyphs.Document,
            Placement = NavPlacement.Main,
            Order = 1,
        });
        services.AddScreen(new ScreenRegistration
        {
            Key = ScreenKeys.Settings,
            Title = ShellStrings.Settings,
            ViewModelType = typeof(SettingsViewModel),
            RequiresSignIn = false,
            Glyph = Glyphs.Settings,
            Placement = NavPlacement.Footer,
            Order = 0,
        });
        return services;
    }

    public static IServiceCollection AddScreen(this IServiceCollection services, ScreenRegistration registration)
    {
        services.AddSingleton(registration.ViewModelType);
        services.AddSingleton(registration);
        return services;
    }
}
