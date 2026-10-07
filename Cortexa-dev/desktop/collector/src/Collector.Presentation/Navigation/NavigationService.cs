using Collector.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Collector.Presentation.Navigation;

public sealed class NavigationService : INavigationService
{
    private readonly List<ScreenRegistration> _screens;
    private readonly IServiceProvider _services;
    private readonly ISessionState _session;

    public NavigationService(
        IEnumerable<ScreenRegistration> screens,
        IServiceProvider services,
        ISessionState session)
    {
        _screens = [.. screens];
        _services = services;
        _session = session;
    }

    public event EventHandler? Navigated;

    public IReadOnlyList<ScreenRegistration> Screens => _screens;

    public ScreenRegistration? CurrentScreen { get; private set; }

    public object? CurrentViewModel { get; private set; }

    public void NavigateTo(string key)
    {
        var target = Resolve(key);
        if (target == CurrentScreen)
        {
            return;
        }

        (CurrentViewModel as INavigationAware)?.OnNavigatedFrom();
        CurrentScreen = target;
        CurrentViewModel = _services.GetRequiredService(target.ViewModelType);
        (CurrentViewModel as INavigationAware)?.OnNavigatedTo();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    private ScreenRegistration Resolve(string key)
    {
        var requested = _screens.FirstOrDefault(s => s.Key == key)
            ?? throw new ArgumentException($"Unknown screen '{key}'.", nameof(key));
        var blocked = requested.RequiresSignIn && _session.Current != SessionState.SignedIn;
        return blocked ? _screens.First(s => s.Key == ScreenKeys.SignIn) : requested;
    }
}
