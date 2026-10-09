using Collector.Application.Auth;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly ISessionState _session;
    private readonly ISignInService _signIn;
    private readonly SignInViewModel _signInScreen;

    public ShellViewModel(
        INavigationService navigation,
        ISessionState session,
        ISignInService signIn,
        SignInViewModel signInScreen)
    {
        _navigation = navigation;
        _session = session;
        _signIn = signIn;
        _signInScreen = signInScreen;
        MainItems = CreateItems(NavPlacement.Main);
        FooterItems = CreateItems(NavPlacement.Footer);
        Badge = SessionBadge.For(session.Current, session.UserEmail, signingOut: false);
        navigation.Navigated += (_, _) => OnNavigated();
        session.Changed += (_, _) => UiThread.Post(OnSessionChanged);
        signInScreen.SignedIn += (_, _) => NavigateHome();
        RefreshItems();
    }

    public IReadOnlyList<NavItemViewModel> MainItems { get; }

    public IReadOnlyList<NavItemViewModel> FooterItems { get; }

    [ObservableProperty]
    public partial object? CurrentScreen { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = ShellStrings.AppName;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRailCompact { get; set; }

    [ObservableProperty]
    public partial SessionBadge Badge { get; set; }

    [ObservableProperty]
    public partial bool IsSigningOut { get; set; }

    public void Start() => _navigation.NavigateTo(_session.Current == SessionState.SignedIn ? HomeKey() : ScreenKeys.SignIn);

    public void OnWindowClosing() => (_navigation.CurrentViewModel as INavigationAware)?.OnNavigatedFrom();

    partial void OnIsRailCompactChanged(bool value)
    {
        foreach (var item in MainItems.Concat(FooterItems))
        {
            item.IsCompact = value;
        }
    }

    [RelayCommand]
    private void Navigate(string key) => _navigation.NavigateTo(key);

    [RelayCommand]
    private void OpenSettings()
    {
        _navigation.NavigateTo(ScreenKeys.Settings);
        (_navigation.CurrentViewModel as SettingsViewModel)?.FocusSections();
    }

    [RelayCommand]
    private void OpenSignIn() => _navigation.NavigateTo(ScreenKeys.SignIn);

    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        SetSigningOut(true);
        try
        {
            await _signIn.SignOutAsync(cancellationToken);
        }
        finally
        {
            SetSigningOut(false);
        }

        _navigation.NavigateTo(ScreenKeys.SignIn);
    }

    private bool CanSignOut() => !IsSigningOut;

    private void SetSigningOut(bool value)
    {
        IsSigningOut = value;
        Badge = SessionBadge.For(_session.Current, _session.UserEmail, value);
        SignOutCommand.NotifyCanExecuteChanged();
    }

    private void OnNavigated()
    {
        CurrentScreen = _navigation.CurrentViewModel;
        Title = _navigation.CurrentScreen?.Title ?? ShellStrings.AppName;
        Subtitle = _navigation.CurrentScreen?.Subtitle ?? string.Empty;
        foreach (var item in MainItems.Concat(FooterItems))
        {
            item.IsSelected = item.Registration == _navigation.CurrentScreen;
        }
    }

    private void OnSessionChanged()
    {
        Badge = SessionBadge.For(_session.Current, _session.UserEmail, IsSigningOut);
        RefreshItems();
        if (_session.Current == SessionState.Expired)
        {
            _signInScreen.MarkExpired();
        }

        if (_session.Current != SessionState.SignedIn && _navigation.CurrentScreen?.RequiresSignIn == true)
        {
            _navigation.NavigateTo(ScreenKeys.SignIn);
        }
    }

    private void RefreshItems()
    {
        var signedIn = _session.Current == SessionState.SignedIn;
        foreach (var item in MainItems.Concat(FooterItems))
        {
            item.IsEnabled = signedIn || !item.Registration.RequiresSignIn;
            item.IsVisible = !(signedIn && item.Registration.Key == ScreenKeys.SignIn);
        }
    }

    private void NavigateHome() => _navigation.NavigateTo(HomeKey());

    private string HomeKey() =>
        _navigation.Screens
            .Where(screen => screen.RequiresSignIn)
            .OrderBy(screen => screen.Placement)
            .ThenBy(screen => screen.Order)
            .Select(screen => screen.Key)
            .FirstOrDefault() ?? ScreenKeys.Settings;

    private List<NavItemViewModel> CreateItems(NavPlacement placement) =>
        _navigation.Screens
            .Where(screen => screen.Placement == placement)
            .OrderBy(screen => screen.Order)
            .Select(screen => new NavItemViewModel(screen, Navigate))
            .ToList();
}
