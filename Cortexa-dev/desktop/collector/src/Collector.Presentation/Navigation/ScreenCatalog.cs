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
            Subtitle = ShellStrings.SignInSubtitle,
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
            Subtitle = ShellStrings.ExtractSubtitle,
            ViewModelType = typeof(ExtractionViewModel),
            RequiresSignIn = false,
            Glyph = Glyphs.Document,
            Placement = NavPlacement.Main,
            Order = 1,
        });
        services.AddScreen(new ScreenRegistration
        {
            Key = ScreenKeys.Review,
            Title = ShellStrings.Review,
            Subtitle = ShellStrings.ReviewSubtitle,
            ViewModelType = typeof(ReviewViewModel),
            RequiresSignIn = true,
            Glyph = Glyphs.Lightbulb,
            Placement = NavPlacement.Main,
            Order = 2,
        });
        services.AddScreen(new ScreenRegistration
        {
            Key = ScreenKeys.History,
            Title = ShellStrings.History,
            Subtitle = ShellStrings.HistorySubtitle,
            ViewModelType = typeof(HistoryViewModel),
            RequiresSignIn = true,
            Glyph = Glyphs.History,
            Placement = NavPlacement.Main,
            Order = 3,
        });
        services.AddScreen(new ScreenRegistration
        {
            Key = ScreenKeys.Settings,
            Title = ShellStrings.Settings,
            Subtitle = ShellStrings.SettingsSubtitle,
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
