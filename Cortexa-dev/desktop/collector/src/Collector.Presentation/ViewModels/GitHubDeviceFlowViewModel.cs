using Collector.Application.Remote.Auth;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public enum DeviceFlowState
{
    Idle,
    Requesting,
    Waiting,
    Expired,
    Denied,
    Failed,
    Disabled,
}

public sealed record DeviceFlowServices(IGitHubSignIn SignIn, IClipboard Clipboard, IExternalLinkLauncher Links, Uri AuthBase);

public sealed partial class GitHubDeviceFlowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CopiedFeedbackDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExpiringSoon = TimeSpan.FromSeconds(60);
    private static readonly int[] AnnounceAtSeconds = [300, 60];

    private readonly DeviceFlowServices _services;
    private readonly TimeProvider _time;
    private readonly Action<string> _requestFocus;
    private readonly RateLimitCountdown _countdown;
    private readonly HashSet<int> _announced = [];
    private CancellationTokenSource? _cts;
    private ITimer? _copyTimer;
    private bool _expiredByClock;
    private TimeSpan _expiresIn;

    public GitHubDeviceFlowViewModel(DeviceFlowServices services, TimeProvider time, Action<string> requestFocus)
    {
        _services = services;
        _time = time;
        _requestFocus = requestFocus;
        _countdown = new RateLimitCountdown(time);
    }

    public event EventHandler? UserCanceled;

    public event EventHandler? RetryRequested;

    public event EventHandler? UsePatRequested;

    public bool IsActive => State is DeviceFlowState.Requesting or DeviceFlowState.Waiting;

    public bool ShowPanel => State != DeviceFlowState.Idle;

    public bool IsRequesting => State == DeviceFlowState.Requesting;

    public bool IsWaiting => State == DeviceFlowState.Waiting;

    public bool ShowOutcome => ShowPanel && !IsActive;

    public bool IsExpiredOutcome => State == DeviceFlowState.Expired;

    public bool IsErrorOutcome => ShowOutcome && !IsExpiredOutcome;

    public bool HasRetry => RetryLabel is not null;

    public bool HasNotice => Notice is not null;

    public bool IsExpiringSoon => IsWaiting && RemainingTime < ExpiringSoon;

    public string LinkText => VerificationUri is { } uri ? uri.Host + uri.AbsolutePath.TrimEnd('/') : string.Empty;

    public string UserCodeName => GitHubAuthStrings.UserCodeName(SpellCode(UserCode));

    public string RemainingText => GitHubAuthStrings.ExpiresIn(CountdownFormatter.Format(RemainingTime));

    public string OutcomeName => $"{OutcomeTitle} {OutcomeMessage}";

    [ObservableProperty]
    public partial DeviceFlowState State { get; set; }

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

    public async Task<bool> RunAsync(RemoteAuthMethod method)
    {
        Abort();
        var cts = _cts = new CancellationTokenSource();
        State = DeviceFlowState.Requesting;
        try
        {
            var grant = await _services.SignIn.StartAsync(method, cts.Token);
            BeginWaiting(grant);
            var outcome = await _services.SignIn.CompleteAsync(method, grant, cts.Token);
            return Apply(cts, outcome);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return Apply(cts, new DeviceFlowOutcome.Cancelled());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reason = ex is DeviceFlowException flow ? flow.Reason : DeviceFlowFailure.Other;
            return Apply(cts, new DeviceFlowOutcome.Failed(reason));
        }
    }

    public void Abort()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        ResetState();
    }

    public void UpdateRemaining(TimeSpan remaining)
    {
        RemainingTime = remaining;
        Announce(remaining);
        if (remaining <= TimeSpan.Zero && IsWaiting)
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

    partial void OnStateChanged(DeviceFlowState value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ShowPanel));
        OnPropertyChanged(nameof(IsRequesting));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(ShowOutcome));
        OnPropertyChanged(nameof(IsExpiredOutcome));
        OnPropertyChanged(nameof(IsErrorOutcome));
        OnPropertyChanged(nameof(IsExpiringSoon));
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnUserCodeChanged(string value) => OnPropertyChanged(nameof(UserCodeName));

    partial void OnVerificationUriChanged(Uri? value) => OnPropertyChanged(nameof(LinkText));

    partial void OnRemainingTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(IsExpiringSoon));
    }

    partial void OnNoticeChanged(string? value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnRetryLabelChanged(string? value) => OnPropertyChanged(nameof(HasRetry));

    partial void OnOutcomeTitleChanged(string value) => OnPropertyChanged(nameof(OutcomeName));

    partial void OnOutcomeMessageChanged(string value) => OnPropertyChanged(nameof(OutcomeName));

    [RelayCommand(CanExecute = nameof(IsActive))]
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

        if (!HostMatches(uri))
        {
            Notice = GitHubAuthStrings.HostMismatch;
            return;
        }

        Notice = _services.Links.TryOpen(uri) ? null : GitHubAuthStrings.BrowserFailed(LinkText);
    }

    [RelayCommand]
    private void Retry() => RetryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void UsePat()
    {
        ResetState();
        UsePatRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string SpellCode(string code) =>
        string.Join(", dash, ", code.Split('-').Select(part => string.Join(' ', part.AsEnumerable())));

    private bool HostMatches(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, _services.AuthBase.Host, StringComparison.OrdinalIgnoreCase);

    private void BeginWaiting(DeviceCodeGrant grant)
    {
        _expiredByClock = false;
        _expiresIn = grant.ExpiresIn;
        _announced.Clear();
        UserCode = grant.UserCode;
        VerificationUri = grant.VerificationUri;
        Notice = null;
        State = DeviceFlowState.Waiting;
        _countdown.Start(_time.GetUtcNow() + grant.ExpiresIn, UpdateRemaining);
        _requestFocus(RemoteFocusKeys.DeviceCopy);
    }

    private bool Apply(CancellationTokenSource cts, DeviceFlowOutcome outcome)
    {
        if (!ReferenceEquals(_cts, cts))
        {
            return false;
        }

        _countdown.Stop();
        if (outcome is DeviceFlowOutcome.SignedIn)
        {
            ResetState();
            return true;
        }

        ShowTerminal(outcome);
        return false;
    }

    private void ShowTerminal(DeviceFlowOutcome outcome)
    {
        switch (outcome)
        {
            case DeviceFlowOutcome.Expired:
                ShowExpired();
                break;
            case DeviceFlowOutcome.Denied:
                ShowOutcomeBlock(DeviceFlowState.Denied, GitHubAuthStrings.DeniedTitle, GitHubAuthStrings.DeniedMessage, GitHubAuthStrings.GetNewCode);
                break;
            case DeviceFlowOutcome.Failed failed:
                ShowFailure(failed.Reason);
                break;
            case DeviceFlowOutcome.Cancelled when _expiredByClock:
                ShowExpired();
                break;
            default:
                ResetState();
                break;
        }
    }

    private void ShowExpired() => ShowOutcomeBlock(
        DeviceFlowState.Expired,
        GitHubAuthStrings.ExpiredTitle,
        GitHubAuthStrings.ExpiredMessage((int)_expiresIn.TotalMinutes),
        GitHubAuthStrings.GetNewCode);

    private void ShowFailure(DeviceFlowFailure reason)
    {
        if (reason == DeviceFlowFailure.DeviceFlowDisabled)
        {
            ShowOutcomeBlock(DeviceFlowState.Disabled, GitHubAuthStrings.DisabledTitle, GitHubAuthStrings.DisabledMessage, null);
            return;
        }

        ShowOutcomeBlock(DeviceFlowState.Failed, GitHubAuthStrings.FailedTitle, GitHubAuthStrings.FailedMessage, GitHubAuthStrings.TryAgain);
    }

    private void ShowOutcomeBlock(DeviceFlowState state, string title, string message, string? retryLabel)
    {
        _countdown.Stop();
        OutcomeTitle = title;
        OutcomeMessage = message;
        RetryLabel = retryLabel;
        State = state;
        _requestFocus(retryLabel is null ? RemoteFocusKeys.DeviceUsePat : RemoteFocusKeys.DeviceOutcome);
    }

    private void ResetState()
    {
        _countdown.Stop();
        _copyTimer?.Dispose();
        IsCopied = false;
        Notice = null;
        Announcement = string.Empty;
        UserCode = string.Empty;
        VerificationUri = null;
        RetryLabel = null;
        State = DeviceFlowState.Idle;
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
