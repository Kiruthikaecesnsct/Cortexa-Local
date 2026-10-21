using System.Windows.Input;
using Collector.Application.Remote.Auth;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public enum EntraSignInState
{
    Idle,
    SigningIn,
    DeviceCode,
    SignedIn,
    Expired,
    Failed,
}

public enum EntraOutcomeKind
{
    Error,
    Expired,
    UserCanceled,
}

public sealed record EntraSignInServices(IEntraIdSignIn SignIn, IClipboard Clipboard, IExternalLinkLauncher Links, ILogger Logger);

public sealed record EntraSignInActions(ICommand Start, ICommand StartWithCode, ICommand Connect, ICommand SignOut);

public sealed partial class EntraSignInViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CopiedFeedbackDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExpiringSoon = TimeSpan.FromSeconds(60);
    private static readonly int[] AnnounceAtSeconds = [300, 60, 30];

    private readonly EntraSignInServices _services;
    private readonly EntraSignInActions _actions;
    private readonly TimeProvider _time;
    private readonly Action<string> _requestFocus;
    private readonly RateLimitCountdown _countdown;
    private readonly HashSet<int> _announced = [];
    private CancellationTokenSource? _cts;
    private ITimer? _copyTimer;
    private bool _expiredByClock;
    private bool _useCode;
    private TimeSpan _expiresIn;

    public EntraSignInViewModel(
        EntraSignInServices services,
        EntraSignInActions actions,
        TimeProvider time,
        Action<string> requestFocus)
    {
        _services = services;
        _actions = actions;
        _time = time;
        _requestFocus = requestFocus;
        _countdown = new RateLimitCountdown(time);
        State = services.SignIn.IsSignedIn ? EntraSignInState.SignedIn : EntraSignInState.Idle;
    }

    public event EventHandler? UserCanceled;

    public event EventHandler? UsePatRequested;

    public ICommand StartEntraSignInCommand => _actions.Start;

    public ICommand StartEntraCodeSignInCommand => _actions.StartWithCode;

    public ICommand ConnectCommand => _actions.Connect;

    public ICommand SignOutCommand => _actions.SignOut;

    public bool IsActive => State is EntraSignInState.SigningIn or EntraSignInState.DeviceCode;

    public bool IsIdle => State == EntraSignInState.Idle;

    public bool IsSigningIn => State == EntraSignInState.SigningIn;

    public bool IsDeviceCode => State == EntraSignInState.DeviceCode;

    public bool IsSignedIn => State == EntraSignInState.SignedIn;

    public bool ShowStartActions => IsIdle;

    public bool ShowOutcome => State is EntraSignInState.Expired or EntraSignInState.Failed;

    public bool IsExpiredOutcome => State == EntraSignInState.Expired;

    public bool ShowCodeFallback => State == EntraSignInState.Failed && OutcomeKind == EntraOutcomeKind.Error;

    public bool HasRetry => RetryLabel is not null;

    public bool HasNotice => Notice is not null;

    public bool IsExpiringSoon => IsDeviceCode && RemainingTime < ExpiringSoon;

    public string VerificationUrl => VerificationUri?.AbsoluteUri ?? string.Empty;

    public string LinkText => VerificationUri is { } uri ? uri.Host + uri.AbsolutePath.TrimEnd('/') : string.Empty;

    public string UserCodeName => GitHubAuthStrings.UserCodeName(DeviceCodeSpeller.Spell(UserCode));

    public string CodeStepOpenText => EntraAuthStrings.CodeStepOpen(LinkText);

    public string OpenBrowserName => EntraAuthStrings.OpenBrowserName(LinkText);

    public string RemainingText => GitHubAuthStrings.ExpiresIn(CountdownFormatter.Format(RemainingTime));

    public string OutcomeName => $"{OutcomeTitle} {OutcomeMessage}";

    public string SignedInLabel => AccountName.Length > 0
        ? EntraAuthStrings.SignedInAs(AccountName)
        : EntraAuthStrings.SignedInGeneric;

    public string SignOutText => IsSigningOut ? EntraAuthStrings.SigningOut : EntraAuthStrings.SignOut;

    public string SignOutAutomationName => IsSigningOut ? EntraAuthStrings.SigningOut : EntraAuthStrings.SignOutName;

    [ObservableProperty]
    public partial EntraSignInState State { get; set; }

    [ObservableProperty]
    public partial EntraOutcomeKind OutcomeKind { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsSigningOut { get; set; }

    [ObservableProperty]
    public partial string AccountName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UserCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Uri? VerificationUri { get; set; }

    [ObservableProperty]
    public partial TimeSpan RemainingTime { get; set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial bool IsCopied { get; set; }

    [ObservableProperty]
    public partial string Announcement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutcomeTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutcomeMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? RetryLabel { get; set; }

    public async Task<bool> RunAsync(bool useCode)
    {
        Abort();
        var cts = _cts = new CancellationTokenSource();
        _useCode = useCode;
        Announcement = EntraAuthStrings.SigningInAnnouncement;
        State = EntraSignInState.SigningIn;
        try
        {
            return Apply(cts, await AcquireAsync(useCode, cts));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return Apply(cts, new EntraSignInOutcome.Cancelled());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _services.Logger.LogWarning("Microsoft sign-in failed ({Error}).", ex.GetType().Name);
            return Apply(cts, new EntraSignInOutcome.Failed(EntraFailure.Other));
        }
    }

    public async Task<bool> SignOutAsync(CancellationToken cancellationToken)
    {
        IsSigningOut = true;
        try
        {
            await _services.SignIn.SignOutAsync(cancellationToken);
            Reset();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _services.Logger.LogWarning("Could not remove the saved Microsoft sign-in ({Error}).", ex.GetType().Name);
            return false;
        }
        finally
        {
            IsSigningOut = false;
        }
    }

    public void Abort()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        Reset();
    }

    public void Reset()
    {
        ClearTransient();
        State = _services.SignIn.IsSignedIn ? EntraSignInState.SignedIn : EntraSignInState.Idle;
    }

    public void UpdateRemaining(TimeSpan remaining)
    {
        RemainingTime = remaining;
        Announce(remaining);
        if (remaining <= TimeSpan.Zero && IsDeviceCode)
        {
            _expiredByClock = true;
            _cts?.Cancel();
        }
    }

    public void Dispose()
    {
        Abort();
        _countdown.Dispose();
    }

    partial void OnStateChanged(EntraSignInState value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsSigningIn));
        OnPropertyChanged(nameof(IsDeviceCode));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(ShowStartActions));
        OnPropertyChanged(nameof(ShowOutcome));
        OnPropertyChanged(nameof(IsExpiredOutcome));
        OnPropertyChanged(nameof(ShowCodeFallback));
        OnPropertyChanged(nameof(IsExpiringSoon));
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutcomeKindChanged(EntraOutcomeKind value) => OnPropertyChanged(nameof(ShowCodeFallback));

    partial void OnIsSigningOutChanged(bool value)
    {
        OnPropertyChanged(nameof(SignOutText));
        OnPropertyChanged(nameof(SignOutAutomationName));
    }

    partial void OnAccountNameChanged(string value) => OnPropertyChanged(nameof(SignedInLabel));

    partial void OnUserCodeChanged(string value) => OnPropertyChanged(nameof(UserCodeName));

    partial void OnVerificationUriChanged(Uri? value)
    {
        OnPropertyChanged(nameof(LinkText));
        OnPropertyChanged(nameof(VerificationUrl));
        OnPropertyChanged(nameof(CodeStepOpenText));
        OnPropertyChanged(nameof(OpenBrowserName));
    }

    partial void OnRemainingTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(IsExpiringSoon));
    }

    partial void OnNoticeChanged(string? value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnRetryLabelChanged(string? value) => OnPropertyChanged(nameof(HasRetry));

    partial void OnOutcomeTitleChanged(string value) => OnPropertyChanged(nameof(OutcomeName));

    partial void OnOutcomeMessageChanged(string value) => OnPropertyChanged(nameof(OutcomeName));

    [RelayCommand(CanExecute = nameof(IsDeviceCode))]
    private void Cancel()
    {
        Abort();
        UserCanceled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (!_services.Clipboard.TrySetText(UserCode))
        {
            Notice = GitHubAuthStrings.CopyFailed;
            return;
        }

        Notice = null;
        IsCopied = true;
        Announcement = GitHubAuthStrings.CopiedAnnouncement;
        _copyTimer?.Dispose();
        _copyTimer = _time.CreateTimer(_ => ClearCopied(), null, CopiedFeedbackDuration, Timeout.InfiniteTimeSpan);
    }

    [RelayCommand]
    private void OpenBrowser()
    {
        if (VerificationUri is not { } uri)
        {
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            Notice = EntraAuthStrings.UnsafeUrl;
            return;
        }

        Notice = _services.Links.TryOpen(uri) ? null : GitHubAuthStrings.BrowserFailed(LinkText);
    }

    [RelayCommand]
    private void Retry()
    {
        var command = _useCode ? _actions.StartWithCode : _actions.Start;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    [RelayCommand]
    private void UsePat()
    {
        Reset();
        UsePatRequested?.Invoke(this, EventArgs.Empty);
    }

    private Task<EntraSignInOutcome> AcquireAsync(bool useCode, CancellationTokenSource cts) => useCode
        ? _services.SignIn.SignInWithCodeAsync(code => ShowCodeAsync(code, cts), cts.Token)
        : _services.SignIn.SignInAsync(cts.Token);

    private Task ShowCodeAsync(EntraDeviceCode code, CancellationTokenSource cts) =>
        UiThread.InvokeAsync(() => BeginCode(code, cts));

    private void BeginCode(EntraDeviceCode code, CancellationTokenSource cts)
    {
        if (!ReferenceEquals(_cts, cts))
        {
            return;
        }

        _expiredByClock = false;
        _expiresIn = code.ExpiresAt - _time.GetUtcNow();
        _announced.Clear();
        UserCode = code.UserCode;
        VerificationUri = code.VerificationUri;
        Notice = null;
        Announcement = string.Empty;
        State = EntraSignInState.DeviceCode;
        _countdown.Start(code.ExpiresAt, UpdateRemaining);
        _requestFocus(RemoteFocusKeys.EntraCopy);
    }

    private bool Apply(CancellationTokenSource cts, EntraSignInOutcome outcome)
    {
        if (!ReferenceEquals(_cts, cts))
        {
            return false;
        }

        _countdown.Stop();
        if (outcome is not EntraSignInOutcome.SignedIn)
        {
            ShowTerminal(outcome);
            return false;
        }

        ClearTransient();
        State = EntraSignInState.SignedIn;
        Announcement = SignedInLabel;
        return true;
    }

    private void ShowTerminal(EntraSignInOutcome outcome)
    {
        switch (outcome)
        {
            case EntraSignInOutcome.Expired:
            case EntraSignInOutcome.Cancelled when _expiredByClock:
                ShowExpired();
                break;
            case EntraSignInOutcome.Cancelled:
                ShowOutcomeBlock(EntraOutcomeKind.UserCanceled, EntraAuthStrings.UserCanceledTitle, EntraAuthStrings.UserCanceledMessage, EntraAuthStrings.SignInName);
                break;
            default:
                ShowOutcomeBlock(EntraOutcomeKind.Error, EntraAuthStrings.FailedTitle, EntraAuthStrings.FailedMessage, GitHubAuthStrings.TryAgain);
                break;
        }
    }

    private void ShowExpired() => ShowOutcomeBlock(
        EntraOutcomeKind.Expired,
        EntraAuthStrings.ExpiredTitle,
        GitHubAuthStrings.ExpiredMessage((int)Math.Round(_expiresIn.TotalMinutes)),
        GitHubAuthStrings.GetNewCode);

    private void ShowOutcomeBlock(EntraOutcomeKind kind, string title, string message, string retryLabel)
    {
        ClearTransient();
        OutcomeKind = kind;
        OutcomeTitle = title;
        OutcomeMessage = message;
        RetryLabel = retryLabel;
        State = kind == EntraOutcomeKind.Expired ? EntraSignInState.Expired : EntraSignInState.Failed;
        _requestFocus(RemoteFocusKeys.EntraOutcome);
    }

    private void ClearTransient()
    {
        _countdown.Stop();
        _copyTimer?.Dispose();
        IsCopied = false;
        Notice = null;
        Announcement = string.Empty;
        UserCode = string.Empty;
        VerificationUri = null;
        RetryLabel = null;
    }

    private void Announce(TimeSpan remaining)
    {
        foreach (var seconds in AnnounceAtSeconds)
        {
            if (remaining.TotalSeconds <= seconds && _announced.Add(seconds))
            {
                Announcement = GitHubAuthStrings.ExpiresAnnouncement(CountdownFormatter.Approximate(remaining));
            }
        }
    }

    private void ClearCopied()
    {
        IsCopied = false;
        Announcement = string.Empty;
    }
}
