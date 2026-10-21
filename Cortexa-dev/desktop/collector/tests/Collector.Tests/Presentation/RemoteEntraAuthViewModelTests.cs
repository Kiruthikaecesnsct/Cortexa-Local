using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Tests.Presentation;

public sealed class RemoteEntraAuthViewModelTests
{
    private static readonly RemoteAuthAvailability EntraOn = new(false, false, true);

    private static RemoteSourceHarness AzureHarness(
        RemoteAuthAvailability? availability = null,
        Action<FakeEntraSignIn>? configure = null)
    {
        var harness = new RemoteSourceHarness(null, availability ?? EntraOn, null, configure);
        harness.ViewModel.SelectedSource = SourceType.AzureDevops;
        harness.ViewModel.OrgUrl = RemoteSourceHarness.AzureUrl;
        return harness;
    }

    private static RemoteBannerFactory Factory()
    {
        var command = new RelayCommand(() => { });
        return new RemoteBannerFactory(new RemoteBannerActions(command, command, command, command, command, command, command, command));
    }

    [Fact]
    public void EntraConfigured_DefaultsToEntra_ShowsThePickerAndThePanel()
    {
        var viewModel = AzureHarness().ViewModel;

        Assert.Equal(RemoteAuthMethod.EntraId, viewModel.SelectedAuthMethod);
        Assert.True(viewModel.ShowAuthPicker);
        Assert.True(viewModel.EntraSignIn.IsAvailable);
        Assert.False(viewModel.ShowTokenField);
        Assert.Null(viewModel.AuthAvailabilityNote);
        Assert.Equal(EntraAuthStrings.MethodHint, viewModel.SectionTwoHint);
        Assert.False(viewModel.ShowStartSignIn);
        Assert.False(viewModel.ShowOAuthSignIn);
    }

    [Fact]
    public void EntraNotConfigured_DisablesTheSegment_ShowsTheNote_AndKeepsThePanelCollapsed()
    {
        var viewModel = AzureHarness(RemoteAuthAvailability.None).ViewModel;

        Assert.Equal(RemoteAuthMethod.Pat, viewModel.SelectedAuthMethod);
        Assert.False(viewModel.AuthMethods[0].IsAvailable);
        Assert.Equal(EntraAuthStrings.UnavailableNote, viewModel.AuthAvailabilityNote);
        Assert.False(viewModel.EntraSignIn.IsAvailable);
        Assert.False(viewModel.StartEntraSignInCommand.CanExecute(null));
    }

    [Fact]
    public void ChoosingTheTokenSegment_HidesThePanel_AndShowsTheTokenForm()
    {
        var viewModel = AzureHarness().ViewModel;

        viewModel.AuthMethods[1].IsSelected = true;

        Assert.False(viewModel.EntraSignIn.IsAvailable);
        Assert.True(viewModel.ShowTokenField);
    }

    [Fact]
    public async Task SignIn_ThenConnect_SavesTheOrganization_AndListsRepositories()
    {
        var harness = AzureHarness();
        harness.AzureDevOps.Repositories = [RemoteSourceHarness.AzureRepo("alpha")];
        var viewModel = harness.ViewModel;

        await viewModel.StartEntraSignInCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEntraSignedIn);
        Assert.False(viewModel.ShowAuthPicker);
        Assert.Equal(ConnectionStatus.NotConnected, viewModel.Status);
        Assert.Equal(0, harness.AzureDevOps.ListCalls);
        await viewModel.ConnectEntraCommand.ExecuteAsync(null);

        Assert.Equal(RemoteWizardStep.Repository, viewModel.Step);
        Assert.Equal("contoso", harness.AzureDevOps.LastScope);
        Assert.Equal("contoso", harness.Settings.GetRemoteSources().AzureDevOpsOrganization);
        Assert.Equal(EntraAuthStrings.ConnectingMessage, viewModel.ConnectingText);
    }

    [Fact]
    public async Task Connect_WithAnInvalidOrganizationUrl_StaysOnTheConnectStep()
    {
        var harness = AzureHarness(configure: entra => entra.IsSignedIn = true);
        harness.ViewModel.OrgUrl = "nonsense";

        await harness.ViewModel.ConnectEntraCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.AzureDevOps.ListCalls);
        Assert.NotNull(harness.ViewModel.OrgUrlError);
    }

    [Fact]
    public async Task ConnectActiveMethod_StartsSignInWhenIdle_AndConnectsWhenSignedIn()
    {
        var harness = AzureHarness();
        harness.AzureDevOps.Repositories = [RemoteSourceHarness.AzureRepo("alpha")];

        harness.ViewModel.ConnectActiveMethodCommand.Execute(null);

        Assert.Equal(1, harness.EntraSignIn.InteractiveCalls);
        Assert.True(harness.ViewModel.IsEntraSignedIn);
        harness.ViewModel.ConnectActiveMethodCommand.Execute(null);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.AzureDevOps.ListCalls);
    }

    [Fact]
    public void ConnectActiveMethod_DoesNothingWhileSigningIn()
    {
        var harness = AzureHarness(configure: entra => entra.Gate = new TaskCompletionSource<EntraSignInOutcome>());

        harness.ViewModel.ConnectActiveMethodCommand.Execute(null);
        harness.ViewModel.ConnectActiveMethodCommand.Execute(null);

        Assert.Equal(1, harness.EntraSignIn.InteractiveCalls);
        Assert.Equal(EntraSignInState.SigningIn, harness.ViewModel.EntraSignIn.State);
        Assert.Equal(ConnectionStatus.Connecting, harness.ViewModel.Status);
        Assert.False(harness.ViewModel.AreConnectInputsEnabled);
    }

    [Fact]
    public async Task ConnectActiveMethod_StillConnectsWithTheTokenWhenThePatSegmentIsChosen()
    {
        var harness = AzureHarness(RemoteAuthAvailability.None);
        harness.AzureDevOps.Repositories = [RemoteSourceHarness.AzureRepo("alpha")];
        harness.ViewModel.Token = "pat";

        await harness.ViewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("pat", harness.Credentials.GetToken(SourceType.AzureDevops));
        Assert.Equal(0, harness.EntraSignIn.InteractiveCalls);
    }

    [Fact]
    public async Task CancelledCodeFlow_ShowsTheCanceledNotice_AndStoresNothing()
    {
        var harness = AzureHarness(configure: entra =>
        {
            entra.Code = new EntraDeviceCode("FQ7K-2RXP", new Uri("https://microsoft.com/devicelogin"), DateTimeOffset.UtcNow.AddMinutes(15));
            entra.Gate = new TaskCompletionSource<EntraSignInOutcome>();
        });
        var viewModel = harness.ViewModel;
        var run = viewModel.StartEntraCodeSignInCommand.ExecuteAsync(null);

        viewModel.EntraSignIn.CancelCommand.Execute(null);
        await run;

        Assert.False(viewModel.IsEntraSignedIn);
        Assert.Equal(ConnectionStatus.NotConnected, viewModel.Status);
        Assert.Equal(EntraAuthStrings.Canceled, viewModel.AuthNotice);
        Assert.Equal(AuthNoticeKind.Info, viewModel.AuthNoticeKind);
        Assert.True(viewModel.StartEntraSignInCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExpiredCode_LeavesNothingSignedIn_AndAllowsAnotherAttempt()
    {
        var harness = AzureHarness(configure: entra => entra.Outcome = new EntraSignInOutcome.Expired());
        var viewModel = harness.ViewModel;

        await viewModel.StartEntraCodeSignInCommand.ExecuteAsync(null);

        Assert.Equal(EntraSignInState.Expired, viewModel.EntraSignIn.State);
        Assert.False(viewModel.IsEntraSignedIn);
        Assert.False(harness.EntraSignIn.IsSignedIn);
        Assert.True(viewModel.StartEntraCodeSignInCommand.CanExecute(null));
    }

    [Fact]
    public async Task AccessDeniedAfterConnect_ShowsTheNoAccessBanner_AndDropsTheSignIn()
    {
        var harness = AzureHarness(configure: entra => entra.IsSignedIn = true);
        harness.AzureDevOps.ListError = new RemoteSourceException(RemoteFailureKind.AccessDenied, SourceType.AzureDevops);
        var viewModel = harness.ViewModel;

        await viewModel.ConnectEntraCommand.ExecuteAsync(null);

        Assert.Equal(EntraAuthStrings.NoAccessTitle, viewModel.Banner?.Title);
        Assert.Equal(RemoteFailureMessages.EntraNoAccess(SourceType.AzureDevops), viewModel.Banner?.Message);
        Assert.Equal(1, harness.EntraSignIn.SignOutCalls);
        Assert.False(viewModel.IsEntraSignedIn);
        Assert.Equal(RemoteWizardStep.Connect, viewModel.Step);
    }

    [Fact]
    public async Task MissingTokenAfterConnect_DropsTheSignIn_AndAsksToSignInAgain()
    {
        var harness = AzureHarness(configure: entra => entra.IsSignedIn = true);
        harness.AzureDevOps.ListError = new RemoteSourceException(RemoteFailureKind.MissingToken, SourceType.AzureDevops);
        var viewModel = harness.ViewModel;

        await viewModel.ConnectEntraCommand.ExecuteAsync(null);

        Assert.Equal(EntraAuthStrings.ExpiredSignInTitle, viewModel.Banner?.Title);
        Assert.Equal(GitHubAuthStrings.SignInAgain, viewModel.Banner?.ActionText);
        Assert.Equal(1, harness.EntraSignIn.SignOutCalls);
        Assert.False(viewModel.IsEntraSignedIn);
        Assert.True(viewModel.StartEntraSignInCommand.CanExecute(null));
    }

    [Fact]
    public void Banner_ForEntraMissingToken_UsesTheSignInAgainWording()
    {
        var context = new RemoteFailureContext(SourceType.AzureDevops, string.Empty, string.Empty, "contoso", string.Empty, string.Empty)
        {
            SignedInWith = RemoteAuthMethod.EntraId,
        };

        var banner = Factory().ForFailure(RemoteFailureKind.MissingToken, context);

        Assert.Equal(EntraAuthStrings.ExpiredSignInTitle, banner.Title);
        Assert.NotEqual(RemoteSourceStrings.ChangeToken, banner.ActionText);
    }

    [Fact]
    public async Task UpstreamFailureAfterConnect_KeepsTheSignIn()
    {
        var harness = AzureHarness(configure: entra => entra.IsSignedIn = true);
        harness.AzureDevOps.ListError = new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.AzureDevops);

        await harness.ViewModel.ConnectEntraCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.EntraSignIn.SignOutCalls);
        Assert.True(harness.ViewModel.IsEntraSignedIn);
    }

    [Fact]
    public async Task NoAccessBannerAction_SwitchesToTheTokenForm()
    {
        var harness = AzureHarness(configure: entra => entra.IsSignedIn = true);
        harness.AzureDevOps.ListError = new RemoteSourceException(RemoteFailureKind.Auth, SourceType.AzureDevops);
        var viewModel = harness.ViewModel;
        await viewModel.ConnectEntraCommand.ExecuteAsync(null);

        viewModel.Banner!.ActionCommand!.Execute(null);

        Assert.Equal(RemoteAuthMethod.Pat, viewModel.SelectedAuthMethod);
        Assert.Null(viewModel.Banner);
        Assert.True(viewModel.ShowTokenField);
    }

    [Fact]
    public async Task SignOut_ReturnsToTheEntraSegment_WithASuccessNotice()
    {
        var harness = AzureHarness(configure: entra => entra.IsSignedIn = true);
        var viewModel = harness.ViewModel;

        await viewModel.SignOutEntraCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEntraSignedIn);
        Assert.True(viewModel.ShowAuthPicker);
        Assert.Equal(EntraAuthStrings.SignedOut, viewModel.AuthNotice);
        Assert.Equal(AuthNoticeKind.Success, viewModel.AuthNoticeKind);
    }

    [Fact]
    public void RestoredEntraSignIn_StartsSignedIn_WithThePickerHidden()
    {
        var viewModel = AzureHarness(configure: entra => entra.IsSignedIn = true).ViewModel;

        Assert.True(viewModel.IsEntraSignedIn);
        Assert.False(viewModel.ShowAuthPicker);
        Assert.Equal(GitHubAuthStrings.SignedInHint, viewModel.SectionTwoHint);
    }

    [Theory]
    [InlineData(RemoteFailureKind.Auth)]
    [InlineData(RemoteFailureKind.AccessDenied)]
    public void Banner_ForEntraSignIn_UsesTheNoAccessArm_BeforeTheGitHubOrgBlockedArm(RemoteFailureKind kind)
    {
        var context = new RemoteFailureContext(SourceType.AzureDevops, string.Empty, string.Empty, "contoso", string.Empty, string.Empty)
        {
            SignedInWith = RemoteAuthMethod.EntraId,
        };

        var banner = Factory().ForFailure(kind, context);

        Assert.Equal(BannerSeverity.Error, banner.Severity);
        Assert.Equal(EntraAuthStrings.NoAccessTitle, banner.Title);
        Assert.Equal(RemoteFailureMessages.EntraNoAccess(SourceType.AzureDevops), banner.Message);
        Assert.Equal(EntraAuthStrings.UsePat, banner.ActionText);
        Assert.NotNull(banner.ActionCommand);
    }

    [Fact]
    public void Banner_ForGitHubOAuth_IsUnchangedByTheEntraArm()
    {
        var context = new RemoteFailureContext(SourceType.Github, string.Empty, string.Empty, "acme", string.Empty, string.Empty)
        {
            SignedInWith = RemoteAuthMethod.GitHubDevice,
        };

        var banner = Factory().ForFailure(RemoteFailureKind.AccessDenied, context);

        Assert.Equal(GitHubAuthStrings.OrgBlockedTitle("acme"), banner.Title);
    }
}
