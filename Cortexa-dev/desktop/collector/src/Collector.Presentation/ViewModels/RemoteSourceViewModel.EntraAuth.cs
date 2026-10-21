using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed record EntraAuthDependencies(IEntraIdSignIn SignIn);

public sealed partial class RemoteSourceViewModel
{
    public EntraSignInViewModel EntraSignIn { get; private set; } = null!;

    public bool IsEntraSignedIn => IsAzure && EntraSignIn.IsSignedIn;

    private bool IsEntraMethod => IsAzure && SelectedAuthMethod == RemoteAuthMethod.EntraId;

    private string EntraConnectPrompt => EntraSignIn.State switch
    {
        EntraSignInState.SignedIn => EntraAuthStrings.SignedInConnectPromptText,
        EntraSignInState.SigningIn or EntraSignInState.DeviceCode => EntraAuthStrings.WaitingPromptText,
        _ => EntraAuthStrings.OrgPromptText,
    };

    private IRelayCommand? ActiveEntraCommand => EntraSignIn.State switch
    {
        EntraSignInState.SigningIn or EntraSignInState.DeviceCode => null,
        EntraSignInState.SignedIn => ConnectEntraCommand,
        _ => StartEntraSignInCommand,
    };

    private bool CanStartEntraSignIn() =>
        IsEntraMethod && !IsEntraSignedIn && AreControlsEnabled && !IsConnecting && !IsListLoading && !EntraSignIn.IsActive;

    private bool CanConnectEntra() => IsEntraSignedIn && AreControlsEnabled && !IsConnecting && !IsListLoading;

    private bool CanSignOutEntra() =>
        IsEntraSignedIn && !EntraSignIn.IsSigningOut && !IsConnecting && AreControlsEnabled;

    [RelayCommand(CanExecute = nameof(CanStartEntraSignIn))]
    private Task StartEntraSignInAsync() => RunEntraSignInAsync(useCode: false);

    [RelayCommand(CanExecute = nameof(CanStartEntraSignIn))]
    private Task StartEntraCodeSignInAsync() => RunEntraSignInAsync(useCode: true);

    [RelayCommand(CanExecute = nameof(CanConnectEntra))]
    private async Task ConnectEntraAsync(CancellationToken cancellationToken)
    {
        if (!TryAcceptOrganization())
        {
            return;
        }

        await PersistAzureOrganizationAsync(cancellationToken);
        await RunListAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSignOutEntra))]
    private async Task SignOutEntraAsync(CancellationToken cancellationToken)
    {
        if (!await EntraSignIn.SignOutAsync(cancellationToken))
        {
            ShowAuthNotice(EntraAuthStrings.SignOutFailed, AuthNoticeKind.Warning);
            return;
        }

        SelectedAuthMethod = InitialAuthMethod();
        ShowAuthNotice(EntraAuthStrings.SignedOut, AuthNoticeKind.Success);
        RequestFocus(RemoteFocusKeys.EntraSignIn);
    }

    private async Task RunEntraSignInAsync(bool useCode)
    {
        AuthNotice = null;
        Banner = null;
        Status = ConnectionStatus.Connecting;
        var signedIn = await EntraSignIn.RunAsync(useCode);
        if (EntraSignIn.IsActive)
        {
            return;
        }

        Status = ConnectionStatus.NotConnected;
        if (signedIn)
        {
            RequestFocus(RemoteFocusKeys.EntraConnect);
        }
    }

    private EntraSignInViewModel CreateEntraSignIn()
    {
        var actions = new EntraSignInActions(
            StartEntraSignInCommand,
            StartEntraCodeSignInCommand,
            ConnectEntraCommand,
            SignOutEntraCommand);
        var services = new EntraSignInServices(_deps.EntraAuth.SignIn, _deps.GitHubAuth.Clipboard, _deps.Links, _logger);
        var flow = new EntraSignInViewModel(services, actions, _time, RequestFocus);
        flow.PropertyChanged += (_, args) => OnEntraChanged(args.PropertyName);
        flow.UserCanceled += (_, _) => OnEntraCanceled();
        flow.UsePatRequested += (_, _) => OnUsePat();
        return flow;
    }

    private void OnEntraChanged(string? property)
    {
        if (property is nameof(EntraSignInViewModel.State) or nameof(EntraSignInViewModel.IsSigningOut))
        {
            NotifyGitHubAuth();
        }
    }

    private void OnEntraCanceled()
    {
        Status = ConnectionStatus.NotConnected;
        ShowAuthNotice(EntraAuthStrings.Canceled, AuthNoticeKind.Info);
        RequestFocus(RemoteFocusKeys.EntraSignIn);
    }

    private void UsePatFromBanner()
    {
        Banner = null;
        OnUsePat();
    }

    private void DropEntraSignInOnDenial(RemoteFailureKind kind)
    {
        if (IsEntraSignedIn && kind is RemoteFailureKind.Auth or RemoteFailureKind.AccessDenied or RemoteFailureKind.MissingToken)
        {
            _ = EntraSignIn.SignOutAsync(CancellationToken.None);
        }
    }

    private void NotifyEntraAuth()
    {
        EntraSignIn.IsAvailable = IsEntraMethod;
        OnPropertyChanged(nameof(IsEntraSignedIn));
        StartEntraSignInCommand.NotifyCanExecuteChanged();
        StartEntraCodeSignInCommand.NotifyCanExecuteChanged();
        ConnectEntraCommand.NotifyCanExecuteChanged();
        SignOutEntraCommand.NotifyCanExecuteChanged();
    }
}
