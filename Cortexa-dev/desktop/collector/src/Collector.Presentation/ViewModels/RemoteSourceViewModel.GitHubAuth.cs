using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Presentation.ViewModels;

public sealed record GitHubAuthDependencies(
    IGitHubSignIn SignIn,
    IClipboard Clipboard,
    RemoteAuthAvailability Availability,
    IOptions<RemoteSourceOptions> Options);

public enum AuthNoticeKind
{
    None,
    Info,
    Success,
    Warning,
}

public sealed partial class RemoteSourceViewModel
{
    private SourceType _authSource = SourceType.Local;
    private RemoteAuthMethod? _lastOAuthMethod;

    public GitHubDeviceFlowViewModel DeviceFlow { get; private set; } = null!;

    public IReadOnlyList<SegmentOptionViewModel<RemoteAuthMethod>> AuthMethods { get; private set; } = [];

    public bool IsPatMethod => SelectedAuthMethod == RemoteAuthMethod.Pat;

    public bool IsGitHubSignedIn => IsGitHub && _deps.GitHubAuth.SignIn.IsSignedIn;

    public bool ShowAuthPicker => IsTokenSource && AuthMethods.Count > 1 && !IsGitHubSignedIn && !IsEntraSignedIn;

    public string? AuthAvailabilityNote => IsAzure
        ? EntraAvailabilityNote(_deps.GitHubAuth.Availability)
        : AvailabilityNote(_deps.GitHubAuth.Availability);

    public bool ShowTokenField => IsTokenSource && IsPatMethod;

    public bool ShowOAuthSignIn => IsGitHub && !IsPatMethod && !IsGitHubSignedIn;

    public bool ShowStartSignIn => ShowOAuthSignIn && !DeviceFlow.ShowPanel;

    public bool AreConnectInputsEnabled => !IsConnecting;

    public bool HasAuthNotice => AuthNotice is not null;

    public string SectionTwoTitle => AuthMethods.Count > 1
        ? GitHubAuthStrings.SignInSectionTitle
        : RemoteSourceStrings.TokenSectionTitle;

    public string SectionTwoHint => AuthMethods.Count switch
    {
        > 1 => AuthHint,
        _ => TokenHint,
    };

    public string SignedInLabel => !IsGitHub
        ? string.Empty
        : _deps.GitHubAuth.SignIn.ActiveMethod == RemoteAuthMethod.GitHubApp
            ? GitHubAuthStrings.SignedInApp
            : GitHubAuthStrings.SignedInDevice;

    public string SignOutText => IsSigningOut ? GitHubAuthStrings.SigningOut : GitHubAuthStrings.SignOut;

    public string SignOutAutomationName => IsSigningOut ? GitHubAuthStrings.SigningOut : GitHubAuthStrings.SignOutName;

    public string DisconnectAutomationName => (IsGitHubSignedIn, IsEntraSignedIn) switch
    {
        (true, _) => GitHubAuthStrings.DisconnectOAuthName,
        (_, true) => EntraAuthStrings.DisconnectName,
        _ => RemoteSourceStrings.DisconnectName,
    };

    public string ConnectingText => (IsGitHubSignedIn, IsEntraSignedIn) switch
    {
        (true, _) => GitHubAuthStrings.OAuthConnectingMessage,
        (_, true) => EntraAuthStrings.ConnectingMessage,
        _ => RemoteSourceStrings.ConnectingMessage,
    };

    private string AuthHint => IsGitHubSignedIn || IsEntraSignedIn
        ? GitHubAuthStrings.SignedInHint
        : MethodHint(SelectedAuthMethod);

    private string ConnectPrompt
    {
        get
        {
            if (IsPatMethod)
            {
                return RemoteSourceStrings.ConnectPromptText;
            }

            if (IsAzure)
            {
                return EntraConnectPrompt;
            }

            if (IsGitHubSignedIn)
            {
                return GitHubAuthStrings.SignedInConnectPromptText;
            }

            return DeviceFlow.IsActive
                ? GitHubAuthStrings.WaitingPromptText
                : GitHubAuthStrings.OAuthConnectPromptText;
        }
    }

    private RemoteAuthMethod? ExpiredOAuthMethod =>
        IsGitHub && !_deps.GitHubAuth.SignIn.IsSignedIn ? _lastOAuthMethod : null;

    private RemoteAuthMethod? SignedInOAuthMethod => IsEntraSignedIn
        ? RemoteAuthMethod.EntraId
        : IsGitHub && _deps.GitHubAuth.SignIn.IsSignedIn ? _deps.GitHubAuth.SignIn.ActiveMethod : null;

    [ObservableProperty]
    public partial RemoteAuthMethod SelectedAuthMethod { get; set; }

    [ObservableProperty]
    public partial bool IsSigningOut { get; set; }

    [ObservableProperty]
    public partial string? AuthNotice { get; set; }

    [ObservableProperty]
    public partial AuthNoticeKind AuthNoticeKind { get; set; }

    partial void OnSelectedAuthMethodChanged(RemoteAuthMethod value)
    {
        if (value == RemoteAuthMethod.Pat)
        {
            _lastOAuthMethod = null;
        }

        AuthNotice = null;
        SyncAuthMethods();
        NotifyGitHubAuth();
    }

    partial void OnAuthNoticeChanged(string? value) => OnPropertyChanged(nameof(HasAuthNotice));

    partial void OnIsSigningOutChanged(bool value) => NotifyGitHubAuth();

    private bool CanStartSignIn() =>
        IsGitHub && ShowOAuthSignIn && AreControlsEnabled && !IsConnecting && !IsListLoading && !DeviceFlow.IsActive;

    private bool CanConnectOAuth() =>
        IsGitHubSignedIn && AreControlsEnabled && !IsConnecting && !IsListLoading;

    private bool CanSignOut() => IsGitHubSignedIn && !IsSigningOut && !IsConnecting && AreControlsEnabled;

    [RelayCommand(CanExecute = nameof(CanStartSignIn))]
    private async Task StartGitHubSignInAsync()
    {
        if (!TryAcceptOrganization())
        {
            return;
        }

        var method = SelectedAuthMethod;
        AuthNotice = null;
        Status = ConnectionStatus.Connecting;
        if (await DeviceFlow.RunAsync(method))
        {
            await ListAfterSignInAsync(method);
            return;
        }

        ReleaseFailedSignIn();
    }

    [RelayCommand(CanExecute = nameof(CanConnectOAuth))]
    private async Task ConnectOAuthAsync()
    {
        if (TryAcceptOrganization())
        {
            await RunListAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutGitHubAsync(CancellationToken cancellationToken)
    {
        IsSigningOut = true;
        try
        {
            await _deps.GitHubAuth.SignIn.SignOutAsync(cancellationToken);
            CompleteSignOut();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not remove the saved GitHub sign-in.");
            ShowAuthNotice(GitHubAuthStrings.SignOutFailed, AuthNoticeKind.Warning);
        }
        finally
        {
            IsSigningOut = false;
        }
    }

    [RelayCommand]
    private void ConnectActiveMethod()
    {
        var command = ActiveConnectCommand;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    private IRelayCommand? ActiveConnectCommand
    {
        get
        {
            if (IsPatMethod)
            {
                return ConnectCommand;
            }

            if (IsAzure)
            {
                return ActiveEntraCommand;
            }

            return IsGitHubSignedIn ? ConnectOAuthCommand : StartGitHubSignInCommand;
        }
    }

    private GitHubDeviceFlowViewModel CreateDeviceFlow()
    {
        var auth = _deps.GitHubAuth;
        var baseUrl = new Uri(auth.Options.Value.GitHub.AuthBaseUrl, UriKind.Absolute);
        var services = new DeviceFlowServices(auth.SignIn, auth.Clipboard, _deps.Links, baseUrl);
        var flow = new GitHubDeviceFlowViewModel(services, _time, RequestFocus);
        flow.PropertyChanged += (_, args) => OnDeviceFlowChanged(args.PropertyName);
        flow.UserCanceled += (_, _) => OnSignInCanceled();
        flow.RetryRequested += (_, _) => StartGitHubSignInCommand.Execute(null);
        flow.UsePatRequested += (_, _) => OnUsePat();
        return flow;
    }

    private void OnDeviceFlowChanged(string? property)
    {
        if (property == nameof(GitHubDeviceFlowViewModel.State))
        {
            NotifyGitHubAuth();
        }
    }

    private void OnSignInCanceled()
    {
        Status = ConnectionStatus.NotConnected;
        ShowAuthNotice(GitHubAuthStrings.Canceled, AuthNoticeKind.Info);
        RequestFocus(RemoteFocusKeys.GitHubSignIn);
    }

    private void OnUsePat()
    {
        Status = ConnectionStatus.NotConnected;
        SelectedAuthMethod = RemoteAuthMethod.Pat;
        RequestFocus(RemoteFocusKeys.Token);
    }

    private bool TryAcceptOrganization()
    {
        var form = RemoteConnectRules.ValidateOAuthForm(SelectedSource, OrgUrl);
        OrgUrlError = form.UrlError;
        if (!form.IsValid)
        {
            RequestFocus(RemoteFocusKeys.Organization);
            return false;
        }

        _connectedOrganization = form.Organization!.Organization;
        return true;
    }

    private async Task ListAfterSignInAsync(RemoteAuthMethod method)
    {
        if (!IsGitHubSignedIn)
        {
            return;
        }

        _lastOAuthMethod = method;
        NotifyGitHubAuth();
        await RunListAsync();
    }

    private void ReleaseFailedSignIn()
    {
        if (!DeviceFlow.IsActive)
        {
            Status = ConnectionStatus.NotConnected;
        }
    }

    private void CompleteSignOut()
    {
        _lastOAuthMethod = null;
        SelectedAuthMethod = InitialAuthMethod();
        ShowAuthNotice(GitHubAuthStrings.SignedOut, AuthNoticeKind.Success);
        RequestFocus(RemoteFocusKeys.GitHubSignIn);
    }

    private void SignInAgain()
    {
        var method = _lastOAuthMethod;
        DisconnectCore();
        if (method is { } previous)
        {
            SelectedAuthMethod = previous;
        }

        RequestFocus(IsAzure ? RemoteFocusKeys.EntraSignIn : RemoteFocusKeys.GitHubSignIn);
    }

    private void ShowAuthNotice(string text, AuthNoticeKind kind)
    {
        AuthNoticeKind = kind;
        AuthNotice = text;
    }

    private void ResetGitHubAuth()
    {
        DeviceFlow.Abort();
        EntraSignIn.Abort();
        var active = _deps.GitHubAuth.SignIn.ActiveMethod;
        if (active != RemoteAuthMethod.Pat)
        {
            _lastOAuthMethod = active;
        }

        if (_authSource != SelectedSource || AuthMethods.Count == 0)
        {
            _authSource = SelectedSource;
            AuthMethods = BuildAuthMethods();
            OnPropertyChanged(nameof(AuthMethods));
        }

        SelectedAuthMethod = InitialAuthMethod();
        SyncAuthMethods();
        AuthNotice = null;
        NotifyGitHubAuth();
    }

    private RemoteAuthMethod InitialAuthMethod()
    {
        var active = _deps.GitHubAuth.SignIn.ActiveMethod;
        var availability = _deps.GitHubAuth.Availability;
        return IsGitHub && active != RemoteAuthMethod.Pat
            ? active
            : RemoteConnectRules.DefaultMethod(SelectedSource, availability);
    }

    private IReadOnlyList<SegmentOptionViewModel<RemoteAuthMethod>> BuildAuthMethods() =>
        [.. RemoteConnectRules.AuthMethodsFor(SelectedSource)
            .Select(CreateAuthOption)];

    private SegmentOptionViewModel<RemoteAuthMethod> CreateAuthOption(RemoteAuthMethod method)
    {
        var enabled = RemoteConnectRules.IsMethodEnabled(method, _deps.GitHubAuth.Availability);
        return new SegmentOptionViewModel<RemoteAuthMethod>(
            method,
            MethodLabel(method),
            value => SelectedAuthMethod = value,
            enabled ? null : MethodUnavailable(method),
            MethodHint(method));
    }

    private static string? EntraAvailabilityNote(RemoteAuthAvailability availability) =>
        availability.EntraEnabled ? null : EntraAuthStrings.UnavailableNote;

    private static string? AvailabilityNote(RemoteAuthAvailability availability) => availability switch
    {
        { DeviceEnabled: false, AppEnabled: false } => GitHubAuthStrings.BothMethodsUnavailableNote,
        { DeviceEnabled: false } => GitHubAuthStrings.DeviceMethodUnavailableNote,
        { AppEnabled: false } => GitHubAuthStrings.AppMethodUnavailableNote,
        _ => null,
    };

    private static string MethodUnavailable(RemoteAuthMethod method) => method switch
    {
        RemoteAuthMethod.GitHubApp => GitHubAuthStrings.AppMethodUnavailable,
        RemoteAuthMethod.EntraId => EntraAuthStrings.MethodUnavailable,
        _ => GitHubAuthStrings.DeviceMethodUnavailable,
    };

    private void SyncAuthMethods()
    {
        foreach (var option in AuthMethods)
        {
            option.Sync(SelectedAuthMethod);
        }
    }

    private static string MethodLabel(RemoteAuthMethod method) => method switch
    {
        RemoteAuthMethod.GitHubDevice => GitHubAuthStrings.DeviceMethodLabel,
        RemoteAuthMethod.GitHubApp => GitHubAuthStrings.AppMethodLabel,
        RemoteAuthMethod.EntraId => EntraAuthStrings.MethodLabel,
        _ => GitHubAuthStrings.PatMethodLabel,
    };

    private static string MethodHint(RemoteAuthMethod method) => method switch
    {
        RemoteAuthMethod.GitHubDevice => GitHubAuthStrings.DeviceMethodHint,
        RemoteAuthMethod.GitHubApp => GitHubAuthStrings.AppMethodHint,
        RemoteAuthMethod.EntraId => EntraAuthStrings.MethodHint,
        _ => GitHubAuthStrings.PatMethodHint,
    };

    private string GitHubEmptyText() => _deps.GitHubAuth.SignIn.ActiveMethod == RemoteAuthMethod.GitHubApp
        ? GitHubAuthStrings.EmptyGitHubApp(_connectedOrganization)
        : RemoteSourceStrings.EmptyGitHub;

    private void NotifyGitHubAuth()
    {
        OnPropertyChanged(nameof(IsPatMethod));
        OnPropertyChanged(nameof(IsGitHubSignedIn));
        OnPropertyChanged(nameof(ShowAuthPicker));
        OnPropertyChanged(nameof(AuthAvailabilityNote));
        OnPropertyChanged(nameof(ShowTokenField));
        OnPropertyChanged(nameof(ShowOAuthSignIn));
        OnPropertyChanged(nameof(ShowStartSignIn));
        OnPropertyChanged(nameof(AreConnectInputsEnabled));
        OnPropertyChanged(nameof(SectionTwoTitle));
        OnPropertyChanged(nameof(SectionTwoHint));
        OnPropertyChanged(nameof(SignedInLabel));
        OnPropertyChanged(nameof(SignOutText));
        OnPropertyChanged(nameof(SignOutAutomationName));
        OnPropertyChanged(nameof(DisconnectAutomationName));
        OnPropertyChanged(nameof(ConnectingText));
        OnPropertyChanged(nameof(StepPrompt));
        StartGitHubSignInCommand.NotifyCanExecuteChanged();
        ConnectOAuthCommand.NotifyCanExecuteChanged();
        SignOutGitHubCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
        NotifyEntraAuth();
    }
}
