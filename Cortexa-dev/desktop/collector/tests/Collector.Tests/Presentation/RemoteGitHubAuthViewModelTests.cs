using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class RemoteGitHubAuthViewModelTests
{
    private static readonly RemoteAuthAvailability Both = new(true, true);

    private static RemoteSourceHarness GitHubHarness(
        RemoteAuthAvailability? availability = null,
        Action<FakeGitHubSignIn>? configure = null)
    {
        var harness = new RemoteSourceHarness(null, availability ?? Both, configure);
        harness.ViewModel.SelectedSource = SourceType.Github;
        return harness;
    }

    private static IEnumerable<RemoteAuthMethod> Methods(RemoteSourceViewModel viewModel) =>
        viewModel.AuthMethods.Select(option => option.Value);

    private static IEnumerable<bool> Availability(RemoteSourceViewModel viewModel) =>
        viewModel.AuthMethods.Select(option => option.IsAvailable);

    [Fact]
    public void GitHubWithNoClientIds_ShowsThePickerWithOnlyTokenEnabled_AndKeepsTheTokenForm()
    {
        var viewModel = GitHubHarness(RemoteAuthAvailability.None).ViewModel;

        Assert.Equal([RemoteAuthMethod.GitHubDevice, RemoteAuthMethod.GitHubApp, RemoteAuthMethod.Pat], Methods(viewModel));
        Assert.Equal([false, false, true], Availability(viewModel));
        Assert.True(viewModel.ShowAuthPicker);
        Assert.Equal(RemoteAuthMethod.Pat, viewModel.SelectedAuthMethod);
        Assert.True(viewModel.ShowTokenField);
        Assert.Equal(GitHubAuthStrings.SignInSectionTitle, viewModel.SectionTwoTitle);
        Assert.Equal(GitHubAuthStrings.PatMethodHint, viewModel.SectionTwoHint);
        Assert.EndsWith(RemoteSourceStrings.ConnectPromptText, viewModel.StepPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableOptions_CannotBeSelected_AndCannotStartSignIn()
    {
        var viewModel = GitHubHarness(RemoteAuthAvailability.None).ViewModel;
        viewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;

        viewModel.AuthMethods[0].IsSelected = true;
        viewModel.AuthMethods[1].IsSelected = true;

        Assert.Equal(RemoteAuthMethod.Pat, viewModel.SelectedAuthMethod);
        Assert.False(viewModel.AuthMethods[0].IsSelected);
        Assert.False(viewModel.StartGitHubSignInCommand.CanExecute(null));
    }

    [Fact]
    public void UnavailableOptions_CarryTheReasonAsTooltipAndHelpText()
    {
        var options = GitHubHarness(new RemoteAuthAvailability(false, false)).ViewModel.AuthMethods;

        Assert.Equal(GitHubAuthStrings.DeviceMethodUnavailable, options[0].UnavailableReason);
        Assert.Equal(GitHubAuthStrings.DeviceMethodUnavailable, options[0].HelpText);
        Assert.Equal(GitHubAuthStrings.AppMethodUnavailable, options[1].UnavailableReason);
        Assert.Null(options[2].UnavailableReason);
        Assert.Equal(GitHubAuthStrings.PatMethodHint, options[2].HelpText);
    }

    [Theory]
    [InlineData(true, true, null)]
    [InlineData(false, false, GitHubAuthStrings.BothMethodsUnavailableNote)]
    [InlineData(false, true, GitHubAuthStrings.DeviceMethodUnavailableNote)]
    [InlineData(true, false, GitHubAuthStrings.AppMethodUnavailableNote)]
    public void AvailabilityNote_NamesTheMissingMethods(bool device, bool app, string? expected)
    {
        var viewModel = GitHubHarness(new RemoteAuthAvailability(device, app)).ViewModel;

        Assert.Equal(expected, viewModel.AuthAvailabilityNote);
    }

    [Fact]
    public void OnlyAppConfigured_DefaultsToAppAndKeepsDeviceDisabled()
    {
        var viewModel = GitHubHarness(new RemoteAuthAvailability(false, true)).ViewModel;

        Assert.Equal(RemoteAuthMethod.GitHubApp, viewModel.SelectedAuthMethod);
        Assert.Equal([false, true, true], Availability(viewModel));
        Assert.Equal(GitHubAuthStrings.AppMethodHint, viewModel.SectionTwoHint);
    }

    [Fact]
    public void GitHubWithBothMethods_OffersDeviceAppAndTokenWithDeviceSelected()
    {
        var viewModel = GitHubHarness().ViewModel;

        Assert.Equal([RemoteAuthMethod.GitHubDevice, RemoteAuthMethod.GitHubApp, RemoteAuthMethod.Pat], Methods(viewModel));
        Assert.Equal(RemoteAuthMethod.GitHubDevice, viewModel.SelectedAuthMethod);
        Assert.True(viewModel.ShowAuthPicker);
        Assert.False(viewModel.ShowTokenField);
        Assert.Equal(GitHubAuthStrings.SignInSectionTitle, viewModel.SectionTwoTitle);
        Assert.Equal(GitHubAuthStrings.DeviceMethodHint, viewModel.SectionTwoHint);
        Assert.True(viewModel.ShowStartSignIn);
    }

    [Fact]
    public void AzureDevOps_IsAlwaysTokenOnly_EvenWhenGitHubSignInIsConfigured()
    {
        var harness = GitHubHarness();
        harness.ViewModel.SelectedSource = SourceType.AzureDevops;

        Assert.Equal([RemoteAuthMethod.Pat], Methods(harness.ViewModel));
        Assert.False(harness.ViewModel.ShowAuthPicker);
        Assert.True(harness.ViewModel.ShowTokenField);
        Assert.True(harness.ViewModel.IsPatMethod);
        Assert.False(harness.ViewModel.ShowStartSignIn);
    }

    [Fact]
    public async Task PatMethod_StillConnectsWithTheTokenForm()
    {
        var harness = GitHubHarness();
        harness.GitHub.Repositories = [RemoteSourceHarness.GitHubRepo("alpha")];
        harness.ViewModel.SelectedAuthMethod = RemoteAuthMethod.Pat;

        await harness.ConnectAsync(SourceType.Github, RemoteSourceHarness.GitHubUrl);

        Assert.Equal(RemoteWizardStep.Repository, harness.ViewModel.Step);
        Assert.Equal("token", harness.Credentials.GetToken(SourceType.Github));
        Assert.Empty(harness.SignIn.Started);
    }

    [Fact]
    public void OAuthMethod_DisablesTheTokenConnectCommand()
    {
        var viewModel = GitHubHarness().ViewModel;
        viewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;
        viewModel.Token = "token";

        Assert.False(viewModel.ConnectCommand.CanExecute(null));
        Assert.True(viewModel.StartGitHubSignInCommand.CanExecute(null));
    }

    [Fact]
    public async Task SignIn_WithAnInvalidOrganization_ShowsTheUrlErrorAndRequestsNoCode()
    {
        var harness = GitHubHarness();
        harness.ViewModel.OrgUrl = "not a url";

        await harness.ViewModel.StartGitHubSignInCommand.ExecuteAsync(null);

        Assert.NotNull(harness.ViewModel.OrgUrlError);
        Assert.Empty(harness.SignIn.Started);
        Assert.Equal(ConnectionStatus.NotConnected, harness.ViewModel.Status);
    }

    [Fact]
    public async Task SignIn_Success_ListsTheOrganizationRepositories_WithoutStoringAToken()
    {
        var harness = GitHubHarness();
        harness.GitHub.Repositories = [RemoteSourceHarness.GitHubRepo("alpha")];
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;

        await harness.ViewModel.StartGitHubSignInCommand.ExecuteAsync(null);

        Assert.Equal([RemoteAuthMethod.GitHubDevice], harness.SignIn.Started);
        Assert.Equal("octo", harness.GitHub.LastScope);
        Assert.Equal(RemoteWizardStep.Repository, harness.ViewModel.Step);
        Assert.Equal(ConnectionStatus.Connected, harness.ViewModel.Status);
        Assert.Null(harness.Credentials.GetToken(SourceType.Github));
        Assert.True(harness.ViewModel.IsGitHubSignedIn);
        Assert.Equal(GitHubAuthStrings.OAuthConnectingMessage, harness.ViewModel.ConnectingText);
    }

    [Fact]
    public async Task SignIn_Denied_StaysOnConnectWithTheOutcomeInThePanel()
    {
        var harness = GitHubHarness(configure: signIn => signIn.Outcome = new DeviceFlowOutcome.Denied());
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;

        await harness.ViewModel.StartGitHubSignInCommand.ExecuteAsync(null);

        Assert.Equal(DeviceFlowState.Denied, harness.ViewModel.DeviceFlow.State);
        Assert.Equal(RemoteWizardStep.Connect, harness.ViewModel.Step);
        Assert.Equal(ConnectionStatus.NotConnected, harness.ViewModel.Status);
        Assert.Equal(0, harness.GitHub.ListCalls);
        Assert.False(harness.ViewModel.ShowStartSignIn);
    }

    [Fact]
    public async Task Cancel_ReturnsToTheFormWithACanceledNotice_AndListsNothing()
    {
        var harness = GitHubHarness(configure: signIn => signIn.CompleteGate = new TaskCompletionSource<DeviceFlowOutcome>());
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;
        var run = harness.ViewModel.StartGitHubSignInCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionStatus.Connecting, harness.ViewModel.Status);

        harness.ViewModel.DeviceFlow.CancelCommand.Execute(null);
        await run;

        Assert.Equal(ConnectionStatus.NotConnected, harness.ViewModel.Status);
        Assert.Equal(GitHubAuthStrings.Canceled, harness.ViewModel.AuthNotice);
        Assert.Equal(AuthNoticeKind.Info, harness.ViewModel.AuthNoticeKind);
        Assert.False(harness.SignIn.IsSignedIn);
        Assert.Equal(0, harness.GitHub.ListCalls);
        Assert.True(harness.ViewModel.ShowStartSignIn);
    }

    [Fact]
    public async Task SwitchingSource_CancelsARunningFlow()
    {
        var harness = GitHubHarness(configure: signIn => signIn.CompleteGate = new TaskCompletionSource<DeviceFlowOutcome>());
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;
        var run = harness.ViewModel.StartGitHubSignInCommand.ExecuteAsync(null);

        harness.ViewModel.SelectedSource = SourceType.Local;
        await run;

        Assert.Equal(DeviceFlowState.Idle, harness.ViewModel.DeviceFlow.State);
        Assert.False(harness.SignIn.IsSignedIn);
        Assert.Equal(0, harness.GitHub.ListCalls);
    }

    [Fact]
    public async Task UsePat_SwitchesToTheTokenFormAndFocusesTheTokenField()
    {
        var harness = GitHubHarness(configure: signIn => signIn.Outcome = new DeviceFlowOutcome.Failed(DeviceFlowFailure.DeviceFlowDisabled));
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;
        await harness.ViewModel.StartGitHubSignInCommand.ExecuteAsync(null);

        harness.ViewModel.DeviceFlow.UsePatCommand.Execute(null);

        Assert.True(harness.ViewModel.IsPatMethod);
        Assert.True(harness.ViewModel.ShowTokenField);
        Assert.Equal(RemoteFocusKeys.Token, harness.ViewModel.PendingFocus);
    }

    [Fact]
    public async Task RestoredSignIn_HidesThePicker_AndConnectsWithoutANewCode()
    {
        var harness = GitHubHarness(configure: signIn => signIn.ActiveMethod = RemoteAuthMethod.GitHubDevice);
        harness.GitHub.Repositories = [RemoteSourceHarness.GitHubRepo("alpha")];
        var viewModel = harness.ViewModel;
        viewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;

        Assert.True(viewModel.IsGitHubSignedIn);
        Assert.False(viewModel.ShowAuthPicker);
        Assert.Equal(RemoteAuthMethod.GitHubDevice, viewModel.SelectedAuthMethod);
        Assert.Equal(GitHubAuthStrings.SignedInDevice, viewModel.SignedInLabel);
        Assert.Equal(GitHubAuthStrings.DisconnectOAuthName, viewModel.DisconnectAutomationName);

        await viewModel.ConnectOAuthCommand.ExecuteAsync(null);

        Assert.Empty(harness.SignIn.Started);
        Assert.Equal(RemoteWizardStep.Repository, viewModel.Step);
        Assert.Equal("octo", harness.GitHub.LastScope);
    }

    [Fact]
    public async Task ConnectOAuth_WithAnInvalidOrganization_FocusesTheOrganizationField()
    {
        var harness = GitHubHarness(configure: signIn => signIn.ActiveMethod = RemoteAuthMethod.GitHubDevice);
        harness.ViewModel.OrgUrl = "bad";

        await harness.ViewModel.ConnectOAuthCommand.ExecuteAsync(null);

        Assert.NotNull(harness.ViewModel.OrgUrlError);
        Assert.Equal(RemoteFocusKeys.Organization, harness.ViewModel.PendingFocus);
        Assert.Equal(0, harness.GitHub.ListCalls);
    }

    [Fact]
    public async Task SignOut_RemovesTheSignIn_AndReturnsThePicker()
    {
        var harness = GitHubHarness(configure: signIn => signIn.ActiveMethod = RemoteAuthMethod.GitHubApp);
        var viewModel = harness.ViewModel;

        await viewModel.SignOutGitHubCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.SignIn.SignOutCalls);
        Assert.False(viewModel.IsGitHubSignedIn);
        Assert.True(viewModel.ShowAuthPicker);
        Assert.Equal(GitHubAuthStrings.SignedOut, viewModel.AuthNotice);
        Assert.Equal(AuthNoticeKind.Success, viewModel.AuthNoticeKind);
        Assert.Equal(RemoteFocusKeys.GitHubSignIn, viewModel.PendingFocus);
    }

    [Fact]
    public async Task SignOut_Failure_KeepsTheSignInAndShowsAWarning()
    {
        var harness = GitHubHarness(configure: signIn =>
        {
            signIn.ActiveMethod = RemoteAuthMethod.GitHubDevice;
            signIn.SignOutError = new IOException("locked");
        });

        await harness.ViewModel.SignOutGitHubCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsGitHubSignedIn);
        Assert.Equal(GitHubAuthStrings.SignOutFailed, harness.ViewModel.AuthNotice);
        Assert.Equal(AuthNoticeKind.Warning, harness.ViewModel.AuthNoticeKind);
        Assert.False(harness.ViewModel.IsSigningOut);
    }

    [Fact]
    public async Task RefreshRejected_ShowsTheSignInAgainBanner_AndPreselectsThePreviousMethod()
    {
        var harness = GitHubHarness(configure: signIn => signIn.ActiveMethod = RemoteAuthMethod.GitHubApp);
        var viewModel = harness.ViewModel;
        viewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;
        harness.GitHub.BeforeList = () => harness.SignIn.ActiveMethod = RemoteAuthMethod.Pat;
        harness.GitHub.ListError = new RemoteSourceException(RemoteFailureKind.Auth, SourceType.Github);

        await viewModel.ConnectOAuthCommand.ExecuteAsync(null);

        Assert.Equal(GitHubAuthStrings.ExpiredSignInTitle, viewModel.Banner!.Title);
        Assert.Equal(GitHubAuthStrings.SignInAgain, viewModel.Banner.ActionText);
        Assert.Equal(RemoteWizardStep.Connect, viewModel.Step);

        viewModel.Banner.ActionCommand!.Execute(null);

        Assert.Equal(RemoteAuthMethod.GitHubApp, viewModel.SelectedAuthMethod);
        Assert.Equal(RemoteFocusKeys.GitHubSignIn, viewModel.PendingFocus);
        Assert.Null(viewModel.Banner);
        Assert.Empty(harness.SignIn.Started);
    }

    [Fact]
    public async Task PatAuthFailure_KeepsTheTokenBanner()
    {
        var harness = GitHubHarness(RemoteAuthAvailability.None);
        harness.GitHub.ListError = new RemoteSourceException(RemoteFailureKind.Auth, SourceType.Github);

        await harness.ConnectAsync(SourceType.Github, RemoteSourceHarness.GitHubUrl);

        Assert.Equal(RemoteSourceStrings.AuthTitle("GitHub"), harness.ViewModel.Banner!.Title);
    }

    [Fact]
    public async Task GitHubApp_WithNoRepositories_ShowsTheAppSpecificEmptyText()
    {
        var harness = GitHubHarness(configure: signIn => signIn.ActiveMethod = RemoteAuthMethod.GitHubApp);
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;

        await harness.ViewModel.ConnectOAuthCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.ShowEmptyList);
        Assert.Equal(GitHubAuthStrings.EmptyGitHubApp("octo"), harness.ViewModel.EmptyText);
    }

    [Fact]
    public async Task Disconnect_KeepsTheGitHubSignIn()
    {
        var harness = GitHubHarness(configure: signIn => signIn.ActiveMethod = RemoteAuthMethod.GitHubDevice);
        harness.GitHub.Repositories = [RemoteSourceHarness.GitHubRepo("alpha")];
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;
        await harness.ViewModel.ConnectOAuthCommand.ExecuteAsync(null);

        harness.ViewModel.DisconnectCommand.Execute(null);

        Assert.Equal(RemoteWizardStep.Connect, harness.ViewModel.Step);
        Assert.True(harness.ViewModel.IsGitHubSignedIn);
        Assert.Equal(0, harness.SignIn.SignOutCalls);
    }

    [Fact]
    public async Task EnterInTheOrganizationField_RunsTheActiveMethod()
    {
        var harness = GitHubHarness();
        harness.GitHub.Repositories = [RemoteSourceHarness.GitHubRepo("alpha")];
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;

        harness.ViewModel.ConnectActiveMethodCommand.Execute(null);
        await Task.Yield();

        Assert.Equal([RemoteAuthMethod.GitHubDevice], harness.SignIn.Started);
    }
}
