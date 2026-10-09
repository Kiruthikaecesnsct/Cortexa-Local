using Collector.Application.Remote;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;

namespace Collector.Tests.Presentation;

public sealed class RemoteWizardViewModelTests : IDisposable
{
    private const string Folder = "/var/data";
    private const string Passphrase = "phrase";

    private static readonly RemoteBranch[] Branches =
    [
        new("develop", "9b1e0d47aa"),
        new("main", "3f2a9c1e55aa"),
        new("release", "aa00bb11cc22", IsProtected: true),
    ];

    private readonly string _keyFile = Path.Combine(Path.GetTempPath(), $"collector-key-{Guid.NewGuid():N}");

    public RemoteWizardViewModelTests() => File.WriteAllText(_keyFile, "not a real key");

    public void Dispose() => File.Delete(_keyFile);

    private static async Task<(RemoteSourceHarness Harness, RemoteSourceViewModel ViewModel)> ConnectedAsync()
    {
        var harness = new RemoteSourceHarness();
        harness.GitHub.Branches = Branches;
        var viewModel = await harness.OpenGitHubAsync(
            RemoteSourceHarness.GitHubRepo("alpha"),
            RemoteSourceHarness.GitHubRepo("beta", isPrivate: true));
        return (harness, viewModel);
    }

    private static IEnumerable<WizardStepState> StepStates(RemoteSourceViewModel viewModel) =>
        viewModel.Steps.Select(step => step.State);

    [Fact]
    public void NewViewModel_StartsOnTheConnectStepNotConnected()
    {
        var harness = new RemoteSourceHarness();
        harness.ViewModel.SelectedSource = SourceType.Github;

        Assert.Equal(RemoteWizardStep.Connect, harness.ViewModel.Step);
        Assert.Equal(ConnectionStatus.NotConnected, harness.ViewModel.Status);
        Assert.Equal(RemoteSourceStrings.StatusNotConnected, harness.ViewModel.StatusText);
        Assert.Equal(
            [WizardStepState.Current, WizardStepState.Upcoming, WizardStepState.Upcoming, WizardStepState.Upcoming],
            StepStates(harness.ViewModel));
        Assert.StartsWith("Step 1 of 4 - ", harness.ViewModel.StepPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_Succeeds_MovesToTheRepositoryStepAndShowsConnectedTo()
    {
        var (harness, viewModel) = await ConnectedAsync();

        Assert.Equal(RemoteWizardStep.Repository, viewModel.Step);
        Assert.Equal(ConnectionStatus.Connected, viewModel.Status);
        Assert.Equal("Connected to octo", viewModel.StatusText);
        Assert.Equal([WizardStepState.Done, WizardStepState.Current, WizardStepState.Upcoming, WizardStepState.Upcoming], StepStates(viewModel));
        Assert.StartsWith("Step 2 of 4 - ", viewModel.StepPrompt, StringComparison.Ordinal);
        Assert.Equal("token", harness.Credentials.GetToken(SourceType.Github));
        Assert.Equal("octo", harness.GitHub.LastScope);
    }

    [Fact]
    public async Task Connect_ShowsConnectingWhileTheListIsLoading()
    {
        var harness = new RemoteSourceHarness();
        harness.GitHub.Gate = new TaskCompletionSource();
        harness.ViewModel.SelectedSource = SourceType.Github;
        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;
        harness.ViewModel.Token = "token";

        var running = harness.ViewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(ConnectionStatus.Connecting, harness.ViewModel.Status);
        Assert.Equal(RemoteSourceStrings.StatusConnecting, harness.ViewModel.StatusText);
        harness.GitHub.Gate.SetResult();
        await running;
        Assert.Equal(ConnectionStatus.Connected, harness.ViewModel.Status);
    }

    [Fact]
    public async Task Connect_BadToken_ReturnsToTheConnectStepAndForgetsTheToken()
    {
        var harness = new RemoteSourceHarness();
        harness.GitHub.ListError = new RemoteSourceException(RemoteFailureKind.Auth, SourceType.Github);

        await harness.ConnectAsync(SourceType.Github, RemoteSourceHarness.GitHubUrl);

        Assert.Equal(RemoteWizardStep.Connect, harness.ViewModel.Step);
        Assert.Equal(ConnectionStatus.NotConnected, harness.ViewModel.Status);
        Assert.Null(harness.Credentials.GetToken(SourceType.Github));
        Assert.Equal(RemoteSourceStrings.AuthTitle("GitHub"), harness.ViewModel.Banner!.Title);
    }

    [Fact]
    public async Task Connect_RateLimited_KeepsTheTokenSoTryAgainWorks()
    {
        var harness = new RemoteSourceHarness();
        harness.GitHub.ListError = new RemoteSourceException(RemoteFailureKind.RateLimited, SourceType.Github);
        await harness.ConnectAsync(SourceType.Github, RemoteSourceHarness.GitHubUrl);
        harness.GitHub.ListError = null;
        harness.GitHub.Repositories = [RemoteSourceHarness.GitHubRepo("alpha")];

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)harness.ViewModel.Banner!.ActionCommand!).ExecuteAsync(null);

        Assert.Equal(ConnectionStatus.Connected, harness.ViewModel.Status);
        Assert.Equal(RemoteWizardStep.Repository, harness.ViewModel.Step);
    }

    [Fact]
    public async Task Connect_InvalidFields_ShowBothErrorsAndStayOnTheConnectStep()
    {
        var harness = new RemoteSourceHarness();

        await harness.ConnectAsync(SourceType.Github, "https://example.com/octo", token: "  ");

        Assert.Equal("Enter a URL like https://github.com/your-organization", harness.ViewModel.OrgUrlError);
        Assert.Equal("Enter a personal access token", harness.ViewModel.TokenError);
        Assert.Equal(RemoteWizardStep.Connect, harness.ViewModel.Step);
        Assert.Equal(0, harness.GitHub.ListCalls);
        Assert.Null(harness.Credentials.GetToken(SourceType.Github));
    }

    [Fact]
    public async Task Connect_EditingAFieldClearsItsError()
    {
        var harness = new RemoteSourceHarness();
        await harness.ConnectAsync(SourceType.Github, "bad", token: "");

        harness.ViewModel.OrgUrl = RemoteSourceHarness.GitHubUrl;
        harness.ViewModel.Token = "ghp_x";

        Assert.Null(harness.ViewModel.OrgUrlError);
        Assert.Null(harness.ViewModel.TokenError);
    }

    [Fact]
    public async Task Connect_AcceptsTheOrgsUrlForm()
    {
        var harness = new RemoteSourceHarness();

        await harness.ConnectAsync(SourceType.Github, "https://github.com/orgs/octo/");

        Assert.Equal("octo", harness.GitHub.LastScope);
        Assert.Equal(ConnectionStatus.Connected, harness.ViewModel.Status);
    }

    [Fact]
    public async Task SelectingARepository_MovesToTheBranchStepAndPromptsForABranch()
    {
        var (_, viewModel) = await ConnectedAsync();

        viewModel.SelectedRepository = viewModel.Repositories[0];

        Assert.Equal(RemoteWizardStep.Branch, viewModel.Step);
        Assert.True(viewModel.ShowBranchStep);
        Assert.True(viewModel.ShowChangeRepository);
        Assert.False(viewModel.ShowChangeBranch);
        Assert.Contains("3 branches", viewModel.StepPrompt, StringComparison.Ordinal);
        Assert.StartsWith("Step 3 of 4 - ", viewModel.StepPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectingABranch_MovesToTheFetchStep()
    {
        var (_, viewModel) = await ConnectedAsync();
        viewModel.SelectedRepository = viewModel.Repositories[0];

        viewModel.SelectedBranch = viewModel.Branches[0];

        Assert.Equal(RemoteWizardStep.Fetch, viewModel.Step);
        Assert.True(viewModel.ShowFetchStep);
        Assert.True(viewModel.ShowChangeBranch);
        Assert.Equal([WizardStepState.Done, WizardStepState.Done, WizardStepState.Done, WizardStepState.Current], StepStates(viewModel));
        Assert.Contains("octo/alpha @ main", viewModel.StepPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeBranch_ReturnsToTheBranchStepWithoutASelection()
    {
        var (_, viewModel) = await ConnectedAsync();
        viewModel.SelectedRepository = viewModel.Repositories[0];
        viewModel.SelectedBranch = viewModel.Branches[0];

        viewModel.ChangeBranchCommand.Execute(null);

        Assert.Equal(RemoteWizardStep.Branch, viewModel.Step);
        Assert.Null(viewModel.SelectedBranch);
        Assert.NotNull(viewModel.SelectedRepository);
        Assert.Equal(3, viewModel.Branches.Count);
    }

    [Fact]
    public async Task ChangeRepository_FromTheBranchStep_ReturnsToTheRepositoryStep()
    {
        var (_, viewModel) = await ConnectedAsync();
        viewModel.SelectedRepository = viewModel.Repositories[0];

        viewModel.ChangeRepositoryCommand.Execute(null);

        Assert.Equal(RemoteWizardStep.Repository, viewModel.Step);
        Assert.Null(viewModel.SelectedRepository);
        Assert.Empty(viewModel.Branches);
        Assert.Equal(2, viewModel.Repositories.Count);
    }

    [Fact]
    public async Task BranchList_ShowsDefaultFirstWithProtectedFlagsAndShortShas()
    {
        var (_, viewModel) = await ConnectedAsync();

        viewModel.SelectedRepository = viewModel.Repositories[0];

        Assert.Equal(["main", "develop", "release"], viewModel.Branches.Select(branch => branch.Name));
        Assert.True(viewModel.Branches[0].IsDefault);
        Assert.Equal("3f2a9c1", viewModel.Branches[0].ShortSha);
        Assert.True(viewModel.Branches.Single(branch => branch.Name == "release").IsProtected);
        Assert.False(viewModel.Branches[1].IsProtected);
    }

    [Fact]
    public async Task BranchSearch_FiltersTheListAndKeepsTheFullSet()
    {
        var (_, viewModel) = await ConnectedAsync();
        viewModel.SelectedRepository = viewModel.Repositories[0];

        viewModel.BranchSearchText = "REL";

        Assert.Equal(["release"], viewModel.Branches.Select(branch => branch.Name));

        viewModel.BranchSearchText = "zzz";

        Assert.True(viewModel.ShowNoBranchMatch);

        viewModel.BranchSearchText = string.Empty;

        Assert.Equal(3, viewModel.Branches.Count);
    }

    [Fact]
    public async Task RepositoryFilters_CombineAndUpdateTheShowingCount()
    {
        var harness = new RemoteSourceHarness();
        var old = RemoteSourceHarness.GitHubRepo("old") with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30) };
        var fresh = RemoteSourceHarness.GitHubRepo("fresh", isPrivate: true) with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var other = RemoteSourceHarness.GitHubRepo("other", isPrivate: true) with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3) };
        var viewModel = await harness.OpenGitHubAsync(old, fresh, other);

        viewModel.VisibilityFilter = RepositoryVisibility.Private;
        viewModel.SortOrder = RepositorySort.RecentlyUpdated;

        Assert.Equal(["fresh", "other"], viewModel.Repositories.Select(row => row.Name));
        Assert.Equal(RemoteSourceStrings.Showing(2, 3), viewModel.ShowingText);
        Assert.True(viewModel.VisibilityOptions.Single(option => option.Value == RepositoryVisibility.Private).IsSelected);
        Assert.True(viewModel.SortOptions.Single(option => option.Value == RepositorySort.RecentlyUpdated).IsSelected);

        viewModel.SearchText = "oth";

        Assert.Equal(["other"], viewModel.Repositories.Select(row => row.Name));
        Assert.Equal(RemoteSourceStrings.Showing(1, 3), viewModel.ShowingText);
    }

    [Fact]
    public async Task SegmentOption_Selecting_UpdatesTheFilter()
    {
        var (_, viewModel) = await ConnectedAsync();

        viewModel.VisibilityOptions.Single(option => option.Value == RepositoryVisibility.Public).IsSelected = true;

        Assert.Equal(RepositoryVisibility.Public, viewModel.VisibilityFilter);
        Assert.Equal(["alpha"], viewModel.Repositories.Select(row => row.Name));
    }

    [Fact]
    public async Task RepositoryRow_ShowsVisibilityDescriptionAndRelativeUpdate()
    {
        var harness = new RemoteSourceHarness();
        var repository = RemoteSourceHarness.GitHubRepo("alpha", isPrivate: true) with
        {
            Description = "Billing service",
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        };
        var viewModel = await harness.OpenGitHubAsync(repository);

        var row = viewModel.Repositories[0];

        Assert.Equal("Private", row.VisibilityText);
        Assert.Equal("Billing service", row.Description);
        Assert.True(row.HasDescription);
        Assert.Equal("Updated 3 days ago", row.UpdatedText);
        Assert.Equal("main", row.DefaultBranch);
    }

    [Fact]
    public async Task Disconnect_ClearsTheTokenListAndWizardButKeepsTheUrl()
    {
        var (harness, viewModel) = await ConnectedAsync();
        viewModel.SelectedRepository = viewModel.Repositories[0];
        var cleared = 0;
        viewModel.CredentialsCleared += (_, _) => cleared++;

        viewModel.DisconnectCommand.Execute(null);

        Assert.Null(harness.Credentials.GetToken(SourceType.Github));
        Assert.Equal(ConnectionStatus.NotConnected, viewModel.Status);
        Assert.Equal(RemoteWizardStep.Connect, viewModel.Step);
        Assert.Empty(viewModel.Repositories);
        Assert.Empty(viewModel.Branches);
        Assert.Null(viewModel.SelectedRepository);
        Assert.Equal(RemoteSourceHarness.GitHubUrl, viewModel.OrgUrl);
        Assert.Equal(string.Empty, viewModel.Token);
        Assert.True(cleared > 0);
    }

    [Fact]
    public async Task Disconnect_ThenConnectAgain_AsksForTheTokenAgain()
    {
        var (harness, viewModel) = await ConnectedAsync();
        viewModel.DisconnectCommand.Execute(null);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Enter a personal access token", viewModel.TokenError);
        Assert.Equal(1, harness.GitHub.ListCalls);
    }

    [Fact]
    public async Task SourceChange_ClearsThePreviousProvidersToken()
    {
        var (harness, viewModel) = await ConnectedAsync();

        viewModel.SelectedSource = SourceType.AzureDevops;

        Assert.Null(harness.Credentials.GetToken(SourceType.Github));
        Assert.Equal(ConnectionStatus.NotConnected, viewModel.Status);
        Assert.Equal(RemoteWizardStep.Connect, viewModel.Step);
        Assert.Empty(viewModel.Repositories);
    }

    [Fact]
    public async Task SourceChange_KeepsTheOtherProvidersToken()
    {
        var harness = new RemoteSourceHarness();
        harness.Credentials.SetToken(SourceType.AzureDevops, "ado");
        await harness.OpenGitHubAsync();

        harness.ViewModel.SelectedSource = SourceType.Local;

        Assert.Equal("ado", harness.Credentials.GetToken(SourceType.AzureDevops));
    }

    [Fact]
    public async Task Tokens_AreNeverWrittenToTheSecretStoreOrTheSettingsFile()
    {
        var (harness, _) = await ConnectedAsync();

        Assert.Empty(harness.Secrets.Values);
    }

    [Fact]
    public async Task AzureDevOps_PrefillsTheUrlFromTheSavedOrganization()
    {
        var harness = new RemoteSourceHarness();
        await harness.Settings.SaveRemoteSourcesAsync(new RemoteSourceSettings("contoso"), TestSupport.Ct);

        harness.ViewModel.SelectedSource = SourceType.AzureDevops;

        Assert.Equal(RemoteSourceHarness.AzureUrl, harness.ViewModel.OrgUrl);
    }

    [Fact]
    public async Task Ssh_Connect_UsesTheFolderAsTheStartFolderAndStoresThePassphraseInSessionOnly()
    {
        var harness = new RemoteSourceHarness();
        harness.Fetcher.Result = new RemoteFetchResult(SourceType.Ssh, string.Empty, ["a.md"], 1, 0, 0, 0, false);
        FillSsh(harness.ViewModel);

        await harness.ViewModel.SshConnectCommand.ExecuteAsync(null);

        var repository = harness.Fetcher.LastRequest!.Repository;
        Assert.Equal(Folder, repository.Name);
        Assert.Equal("alice@192.0.2.10", repository.FullName);
        Assert.Equal(Passphrase, harness.Credentials.GetSshPassphrase());
        Assert.Empty(harness.Secrets.Values);
        var profile = Assert.Single(harness.Store.Remote.SshProfiles);
        Assert.Equal(Folder, profile.RemoteRoot);
        Assert.Equal(2222, profile.Port);
        Assert.True(harness.ViewModel.HasSummary);
    }

    [Fact]
    public async Task Ssh_Connect_WithoutAPassphrase_ClearsAnyEarlierOne()
    {
        var harness = new RemoteSourceHarness();
        FillSsh(harness.ViewModel);
        harness.ViewModel.SshPassphrase = string.Empty;
        harness.Credentials.SetSshPassphrase("stale");

        await harness.ViewModel.SshConnectCommand.ExecuteAsync(null);

        Assert.Null(harness.Credentials.GetSshPassphrase());
    }

    [Fact]
    public async Task Ssh_Connect_EmptyForm_ShowsEveryFieldErrorAndSkipsTheFetch()
    {
        var harness = new RemoteSourceHarness();
        harness.ViewModel.SelectedSource = SourceType.Ssh;
        harness.ViewModel.SshPort = "abc";

        await harness.ViewModel.SshConnectCommand.ExecuteAsync(null);

        Assert.Equal(RemoteConnectRules.HostRequired, harness.ViewModel.SshHostError);
        Assert.Equal(RemoteConnectRules.PortInvalid, harness.ViewModel.SshPortError);
        Assert.Equal(RemoteConnectRules.UsernameRequired, harness.ViewModel.SshUsernameError);
        Assert.Equal(RemoteConnectRules.KeyFileRequired, harness.ViewModel.SshKeyFileError);
        Assert.Equal(RemoteConnectRules.FolderInvalid, harness.ViewModel.SshFolderError);
        Assert.Equal(0, harness.Fetcher.Calls);
    }

    [Fact]
    public async Task Ssh_Connect_MissingKeyFileOnDisk_ShowsTheNotFoundError()
    {
        var harness = new RemoteSourceHarness();
        FillSsh(harness.ViewModel);
        harness.ViewModel.SshKeyFilePath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");

        await harness.ViewModel.SshConnectCommand.ExecuteAsync(null);

        Assert.Equal(RemoteSourceStrings.SshKeyFileNotFound, harness.ViewModel.SshKeyFileError);
        Assert.Equal(0, harness.Fetcher.Calls);
    }

    [Fact]
    public async Task Ssh_RelativeFolder_IsRejected()
    {
        var harness = new RemoteSourceHarness();
        FillSsh(harness.ViewModel);
        harness.ViewModel.SshFolder = "relative/path";

        await harness.ViewModel.SshConnectCommand.ExecuteAsync(null);

        Assert.Equal(RemoteConnectRules.FolderInvalid, harness.ViewModel.SshFolderError);
        Assert.Null(harness.ViewModel.SshHostError);
    }

    [Fact]
    public async Task Ssh_EditingAFieldClearsItsError()
    {
        var harness = new RemoteSourceHarness();
        harness.ViewModel.SelectedSource = SourceType.Ssh;
        await harness.ViewModel.SshConnectCommand.ExecuteAsync(null);

        harness.ViewModel.SshHost = "192.0.2.10";

        Assert.Null(harness.ViewModel.SshHostError);
        Assert.NotNull(harness.ViewModel.SshUsernameError);
    }

    [Fact]
    public async Task Ssh_SourceChange_ClearsThePassphrase()
    {
        var harness = new RemoteSourceHarness();
        FillSsh(harness.ViewModel);
        harness.Credentials.SetSshPassphrase(Passphrase);

        harness.ViewModel.SelectedSource = SourceType.Local;
        await Task.Yield();

        Assert.Null(harness.Credentials.GetSshPassphrase());
        Assert.Equal(string.Empty, harness.ViewModel.SshPassphrase);
    }

    private void FillSsh(RemoteSourceViewModel viewModel)
    {
        viewModel.SelectedSource = SourceType.Ssh;
        viewModel.SshHost = "192.0.2.10";
        viewModel.SshPort = "2222";
        viewModel.SshUsername = "alice";
        viewModel.SshKeyFilePath = _keyFile;
        viewModel.SshPassphrase = Passphrase;
        viewModel.SshFolder = Folder;
    }

    [Fact]
    public async Task Ssh_Connect_ClosesAnyLiveConnectionBeforeConnecting()
    {
        var harness = new RemoteSourceHarness();
        FillSsh(harness.ViewModel);

        await harness.ViewModel.SshConnectCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.SshCloser.Closed);
    }

    [Fact]
    public void Ssh_SourceChange_ClosesTheConnection()
    {
        var harness = new RemoteSourceHarness();
        harness.ViewModel.SelectedSource = SourceType.Ssh;

        harness.ViewModel.SelectedSource = SourceType.Local;

        Assert.Equal(1, harness.SshCloser.Closed);
    }

    [Fact]
    public void NonSshSourceChange_DoesNotCloseTheConnection()
    {
        var harness = new RemoteSourceHarness();
        harness.ViewModel.SelectedSource = SourceType.Github;

        harness.ViewModel.SelectedSource = SourceType.Local;

        Assert.Equal(0, harness.SshCloser.Closed);
    }
}
