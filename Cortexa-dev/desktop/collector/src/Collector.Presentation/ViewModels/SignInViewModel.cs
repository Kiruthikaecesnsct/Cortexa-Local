using System.Windows.Threading;
using Collector.Application.Auth;
using Collector.Application.Settings;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class SignInViewModel : FocusableViewModel, INavigationAware, IPasswordHost, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly ISignInService _signIn;
    private readonly ISessionState _session;
    private readonly SettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;
    private DateTimeOffset _deadline;
    private CountdownKind _countdownKind;
    private bool _expiredPending;
    private bool _isShown;

    public SignInViewModel(
        ISignInService signIn,
        ISessionState session,
        SettingsService settings,
        INavigationService navigation,
        TimeProvider time)
    {
        _signIn = signIn;
        _session = session;
        _settings = settings;
        _navigation = navigation;
        _time = time;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick();
        GatewayBanner = BuildGatewayBanner();
    }

    public event EventHandler? SignedIn;

    public IPasswordSource? PasswordSource { get; set; }

    public bool HasGateway => GatewayHost is not null;

    public string GatewayLine => SignInStrings.GatewayLine(GatewayHost ?? string.Empty);

    public string SignInLabel => IsBusy ? SignInStrings.SignInBusy : SignInStrings.SignInButton;

    public string SignInAutomationName => IsBusy ? SignInStrings.SignInBusyName : SignInStrings.SignInName;

    public bool AreFieldsEnabled => !IsBusy;

    public BannerViewModel GatewayBanner { get; }

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? EmailError { get; set; }

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignInLabel), nameof(SignInAutomationName), nameof(AreFieldsEnabled))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial bool IsCountingDown { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? Banner { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGateway), nameof(GatewayLine))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string? GatewayHost { get; set; }

    public void OnNavigatedTo()
    {
        _isShown = true;
        GatewayHost = EndpointDisplay.HostOf(_settings.GetEndpoints().GatewayUrl);
        var prefilled = _expiredPending && ShowExpired();
        ResumeCountdown();
        RequestFocus(prefilled ? SignInFocusKeys.Password : SignInFocusKeys.Email);
    }

    public void OnNavigatedFrom()
    {
        _isShown = false;
        _timer.Stop();
        if (SignInCancelCommand.CanExecute(null))
        {
            SignInCancelCommand.Execute(null);
        }
    }

    public void MarkExpired()
    {
        if (IsBusy || IsCountingDown)
        {
            return;
        }

        _expiredPending = true;
        if (_isShown && ShowExpired())
        {
            RequestFocus(SignInFocusKeys.Password);
        }
    }

    public void OnPasswordEdited() => PasswordError = null;

    public void Dispose() => _timer.Stop();

    partial void OnEmailChanged(string value) => EmailError = null;

    [RelayCommand(CanExecute = nameof(CanSignIn), IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        BeginAttempt();
        var email = Email;
        var password = PasswordSource?.GetPassword() ?? string.Empty;
        var outcome = await RunAttemptAsync(email, password, cancellationToken);
        if (outcome is null)
        {
            PasswordSource?.Clear();
            RequestFocus(SignInFocusKeys.Password);
            return;
        }

        Apply(SignInOutcomePresenter.Present(outcome, new SignInContext(email, password, GatewayHost)));
    }

    private bool CanSignIn() => HasGateway && !IsCountingDown;

    [RelayCommand]
    private void OpenSettings() => _navigation.NavigateTo(ScreenKeys.Settings);

    [RelayCommand]
    private void DismissBanner()
    {
        Banner = null;
        RequestFocus(SignInFocusKeys.Email);
    }

    private void BeginAttempt()
    {
        Banner = null;
        EmailError = null;
        PasswordError = null;
        _expiredPending = false;
        IsBusy = true;
        RequestFocus(SignInFocusKeys.Cancel);
    }

    private async Task<SignInOutcome?> RunAttemptAsync(string email, string password, CancellationToken cancellationToken)
    {
        try
        {
            return await _signIn.SignInAsync(email, password, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(SignInPresentation result)
    {
        if (result.ClearPassword)
        {
            PasswordSource?.Clear();
        }

        if (result.Succeeded)
        {
            SignedIn?.Invoke(this, EventArgs.Empty);
            return;
        }

        EmailError = result.EmailError;
        PasswordError = result.PasswordError;
        Banner = result.Banner is null ? null : new BannerViewModel(WithAction(result.Banner, result.OffersSettings));
        if (result is { Countdown: { } kind, RetryAfter: { } wait })
        {
            StartCountdown(kind, wait);
        }

        RequestFocus(result.FocusKey);
    }

    private BannerContent WithAction(BannerContent content, bool offersSettings) =>
        offersSettings
            ? content with { ActionText = SignInStrings.OpenSettings, ActionCommand = OpenSettingsCommand }
            : content;

    private bool ShowExpired()
    {
        _expiredPending = false;
        var prefill = string.IsNullOrWhiteSpace(Email) && _session.UserEmail is not null;
        if (prefill)
        {
            Email = _session.UserEmail!;
        }

        Banner = new BannerViewModel(new BannerContent
        {
            Severity = BannerSeverity.Warning,
            Title = SignInStrings.ExpiredTitle,
            Message = SignInStrings.ExpiredMessage,
            DismissCommand = DismissBannerCommand,
        });
        return prefill;
    }

    private BannerViewModel BuildGatewayBanner() =>
        new(new BannerContent
        {
            Severity = BannerSeverity.Warning,
            Title = SignInStrings.GatewayNotSetTitle,
            Message = SignInStrings.GatewayNotSetMessage,
            ActionText = SignInStrings.OpenSettings,
            ActionCommand = OpenSettingsCommand,
        });

    private void StartCountdown(CountdownKind kind, TimeSpan wait)
    {
        _countdownKind = kind;
        _deadline = _time.GetUtcNow() + wait;
        IsCountingDown = true;
        if (_isShown)
        {
            _timer.Start();
        }
    }

    private void ResumeCountdown()
    {
        if (!IsCountingDown)
        {
            return;
        }

        Tick();
        if (IsCountingDown)
        {
            _timer.Start();
        }
    }

    private void Tick()
    {
        var remaining = _deadline - _time.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            Banner?.Message = SignInOutcomePresenter.CountdownMessage(_countdownKind, remaining);
            return;
        }

        _timer.Stop();
        IsCountingDown = false;
        Banner = new BannerViewModel(new BannerContent
        {
            Severity = BannerSeverity.Info,
            Title = SignInStrings.RetryReadyTitle,
            DismissCommand = DismissBannerCommand,
        });
    }
}
