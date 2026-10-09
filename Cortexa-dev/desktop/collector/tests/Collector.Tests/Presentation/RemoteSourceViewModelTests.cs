using Collector.Application.Remote;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class RemoteSourceViewModelTests
{
    private static readonly RemoteBranch[] DefaultBranches =
    [
        new("develop", "9b1e0d47aa"),
        new("main", "3f2a9c1e55"),
    ];

    private static async Task<(RemoteSourceHarness Harness, RemoteSourceViewModel ViewModel)> ReadyAsync(
        params RemoteRepository[] repositories)
    {
        var harness = new RemoteSourceHarness();
        harness.GitHub.Branches = DefaultBranches;
        var viewModel = await harness.OpenGitHubAsync(repositories.Length == 0 ? [RemoteSourceHarness.GitHubRepo("alpha")] : repositories);
        return (harness, viewModel);
    }

    [Fact]
    public async Task SelectingGitHub_WithToken_LoadsRepositoriesAndShowsCount()
    {
        var (_, viewModel) = await ReadyAsync(RemoteSourceHarness.GitHubRepo("alpha"), RemoteSourceHarness.GitHubRepo("beta"));

        Assert.Equal(RemoteListState.Ready, viewModel.ListState);
        Assert.Equal(2, viewModel.Repositories.Count);
        Assert.Equal(RemoteSourceStrings.Showing(2, 2), viewModel.ShowingText);
        Assert.True(viewModel.ShowRepositoryList);
        Assert.Null(viewModel.Banner);
    }

    [Fact]
    public void SelectingGitHub_WithoutToken_ShowsMissingTokenBannerAndSkipsTheApiCall()
    {
        var harness = new RemoteSourceHarness();

        harness.ViewModel.SelectedSource = SourceType.Github;

        Assert.Equal(0, harness.GitHub.ListCalls);
        Assert.Equal(BannerSeverity.Warning, harness.ViewModel.Banner!.Severity);
        Assert.Equal(RemoteSourceStrings.MissingTitle("GitHub"), harness.ViewModel.Banner.Title);
        Assert.Equal(RemoteSourceStrings.OpenSettings, harness.ViewModel.Banner.ActionText);
        Assert.Equal(RemoteListState.Idle, harness.ViewModel.ListState);
    }

    [Fact]
    public void MissingTokenBanner_OpenSettings_NavigatesAndRemembersTheTokenRow()
    {
        var harness = new RemoteSourceHarness();
        harness.ViewModel.SelectedSource = SourceType.Github;

        harness.ViewModel.Banner!.ActionCommand!.Execute(null);

        Assert.Equal([ScreenKeys.Settings], harness.Navigation.Visited);
    }

    [Fact]
    public async Task SwitchingBackToGitHub_ReusesTheLoadedList()
    {
        var (harness, viewModel) = await ReadyAsync();

        viewModel.SelectedSource = SourceType.Local;
        viewModel.SelectedSource = SourceType.Github;

        Assert.Equal(1, harness.GitHub.ListCalls);
        Assert.Single(viewModel.Repositories);
    }

    [Fact]
    public void AzureDevOps_PastedUrl_IsReducedToTheOrganizationSavedAndUsedAsScope()
    {
        var harness = new RemoteSourceHarness();
        harness.AddToken(SecretSlot.AzureDevOpsPat);
        harness.AzureDevOps.Repositories = [RemoteSourceHarness.AzureRepo("core")];
        var viewModel = harness.ViewModel;
        viewModel.SelectedSource = SourceType.AzureDevops;
        viewModel.OrganizationText = "https://dev.azure.com/contoso";

        viewModel.LoadRepositoriesCommand.Execute(null);

        Assert.Equal("contoso", viewModel.OrganizationText);
        Assert.Equal("contoso", harness.AzureDevOps.LastScope);
        Assert.Equal("contoso", harness.Store.Remote.AzureDevOpsOrganization);
        Assert.Single(viewModel.Repositories);
        Assert.Null(viewModel.OrganizationError);
    }

    [Theory]
    [InlineData("", "Enter the organization name.")]
    [InlineData("bad org!", RemoteSourceRules.OrganizationInvalidReason)]
    public void AzureDevOps_InvalidOrganization_ShowsFieldErrorWithoutCallingTheApi(string text, string expected)
    {
        var harness = new RemoteSourceHarness();
        harness.AddToken(SecretSlot.AzureDevOpsPat);
        var viewModel = harness.ViewModel;
        viewModel.SelectedSource = SourceType.AzureDevops;
        viewModel.OrganizationText = text;

        viewModel.LoadRepositoriesCommand.Execute(null);

        Assert.Equal(expected, viewModel.OrganizationError);
        Assert.Equal(0, harness.AzureDevOps.ListCalls);
    }

    [Fact]
    public void AzureDevOps_OrganizationNotFound_IsAFieldErrorNotABanner()
    {
        var harness = new RemoteSourceHarness();
        harness.AddToken(SecretSlot.AzureDevOpsPat);
        harness.AzureDevOps.ListError = new RemoteSourceException(RemoteFailureKind.NotFound, SourceType.AzureDevops);
        var viewModel = harness.ViewModel;
        viewModel.SelectedSource = SourceType.AzureDevops;
        viewModel.OrganizationText = "contosso";

        viewModel.LoadRepositoriesCommand.Execute(null);

        Assert.Equal(RemoteSourceStrings.OrganizationNotFound("contosso"), viewModel.OrganizationError);
        Assert.Null(viewModel.Banner);
    }

    [Fact]
    public async Task SelectingRepository_LoadsBranchesWithTheDefaultFirstAndSelected()
    {
        var (_, viewModel) = await ReadyAsync();

        viewModel.SelectedRepository = viewModel.Repositories[0];

        Assert.Equal(["main", "develop"], viewModel.Branches.Select(branch => branch.Name));
        Assert.Equal("main", viewModel.SelectedBranch!.Name);
        Assert.Equal("main (default)", viewModel.SelectedBranch.DisplayName);
        Assert.Equal(RemoteSourceStrings.Commit("3f2a9c1"), viewModel.CommitText);
        Assert.True(viewModel.FetchCommand.CanExecute(null));
    }

    [Fact]
    public async Task BranchLoadFailure_StillOffersTheDefaultBranchAndShowsTheBanner()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.GitHub.BranchError = new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.Github);

        viewModel.SelectedRepository = viewModel.Repositories[0];

        Assert.Equal(["main"], viewModel.Branches.Select(branch => branch.Name));
        Assert.Equal(RemoteSourceStrings.UpstreamTitle("GitHub"), viewModel.Banner!.Title);
    }

    [Fact]
    public async Task RepositoryOverTheLimit_DisablesFetchWithAHint()
    {
        var (_, viewModel) = await ReadyAsync(RemoteSourceHarness.GitHubRepo("huge", 600L * 1024 * 1024));

        viewModel.SelectedRepository = viewModel.Repositories[0];

        Assert.False(viewModel.FetchCommand.CanExecute(null));
        Assert.True(viewModel.FetchHelpIsError);
        Assert.Equal(RemoteSourceStrings.TooBigHelp("600 MB", "500 MB"), viewModel.FetchHelp);
        Assert.True(viewModel.Repositories[0].IsTooBig);
    }

    [Fact]
    public async Task Search_FiltersByNameAndDropsASelectionThatNoLongerMatches()
    {
        var (_, viewModel) = await ReadyAsync(RemoteSourceHarness.GitHubRepo("alpha"), RemoteSourceHarness.GitHubRepo("beta"));
        viewModel.SelectedRepository = viewModel.Repositories[0];

        viewModel.SearchText = "BET";

        Assert.Equal(["beta"], viewModel.Repositories.Select(row => row.Name));
        Assert.Null(viewModel.SelectedRepository);
        Assert.Empty(viewModel.Branches);
        Assert.Equal(RemoteSourceStrings.Showing(1, 2), viewModel.ShowingText);

        viewModel.SearchText = "zzz";

        Assert.True(viewModel.ShowNoMatch);
        Assert.Equal(RemoteSourceStrings.NoMatch("zzz"), viewModel.NoMatchText);
    }

    [Fact]
    public void EmptyRepositoryList_ShowsTheEmptyState()
    {
        var harness = new RemoteSourceHarness();
        harness.AddToken(SecretSlot.GitHubPat);

        harness.ViewModel.SelectedSource = SourceType.Github;

        Assert.True(harness.ViewModel.ShowEmptyList);
        Assert.False(harness.ViewModel.HasRepositories);
        Assert.Equal(RemoteSourceStrings.EmptyGitHub, harness.ViewModel.EmptyText);
    }

    [Fact]
    public async Task Fetch_Success_ShowsSummaryRaisesFilesFetchedAndAppendsOrigin()
    {
        var (harness, viewModel) = await ReadyAsync();
        viewModel.SelectedRepository = viewModel.Repositories[0];
        RemoteFilesFetchedEventArgs? raised = null;
        viewModel.FilesFetched += (_, args) => raised = args;

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.Equal("main", harness.Fetcher.LastRequest!.Branch);
        Assert.Equal(["a.md", "b.md"], raised!.Paths);
        Assert.Equal(SourceType.Github, raised.Source);
        Assert.Equal("octo/alpha @ main", raised.Origin);
        Assert.True(viewModel.HasSummary);
        Assert.False(viewModel.ShowForm);
        Assert.Contains("3f2a9c1", viewModel.Summary!.Detail);
        Assert.Contains("Fetched 2 files", viewModel.Summary.Detail);
        Assert.Null(viewModel.Banner);
        Assert.False(viewModel.IsFetching);
    }

    [Fact]
    public async Task Fetch_Truncated_ShowsAWarningAlongsideTheSummary()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Result = harness.Fetcher.Result with { Truncated = true };
        viewModel.SelectedRepository = viewModel.Repositories[0];

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSummary);
        Assert.Equal(BannerSeverity.Warning, viewModel.Banner!.Severity);
        Assert.Equal(RemoteSourceStrings.TruncatedTitle, viewModel.Banner.Title);
    }

    [Fact]
    public async Task Fetch_NoFiles_ShowsAWarningAndRaisesNothing()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Result = harness.Fetcher.Result with { LocalPaths = [] };
        viewModel.SelectedRepository = viewModel.Repositories[0];
        var raised = false;
        viewModel.FilesFetched += (_, _) => raised = true;

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(viewModel.HasSummary);
        Assert.Equal(RemoteSourceStrings.NoFilesTitle, viewModel.Banner!.Title);
    }

    [Fact]
    public async Task Fetch_Failure_ShowsTheMappedBannerAndKeepsTheForm()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Error = new RemoteSourceException(RemoteFailureKind.AccessDenied, SourceType.Github);
        viewModel.SelectedRepository = viewModel.Repositories[0];

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.Equal(RemoteSourceStrings.DeniedTitle, viewModel.Banner!.Title);
        Assert.Equal(BannerSeverity.Error, viewModel.Banner.Severity);
        Assert.True(viewModel.ShowForm);
        Assert.False(viewModel.IsFetching);
    }

    [Fact]
    public async Task Fetch_TryAgain_RunsTheFetchAgain()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Error = new RemoteSourceException(RemoteFailureKind.Auth, SourceType.Github);
        viewModel.SelectedRepository = viewModel.Repositories[0];
        await viewModel.FetchCommand.ExecuteAsync(null);
        harness.Fetcher.Error = null;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.Banner!.SecondaryActionCommand!).ExecuteAsync(null);

        Assert.Equal(2, harness.Fetcher.Calls);
        Assert.True(viewModel.HasSummary);
    }

    [Fact]
    public async Task Fetch_Canceled_ShowsTheCanceledBannerAndRestoresControls()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Gate = new TaskCompletionSource();
        viewModel.SelectedRepository = viewModel.Repositories[0];

        var running = viewModel.FetchCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsFetching);
        Assert.False(viewModel.AreControlsEnabled);
        Assert.Equal(RemoteSourceStrings.Progress(1, 2), viewModel.ProgressText);
        viewModel.FetchCancelCommand.Execute(null);
        await running;

        Assert.False(viewModel.IsFetching);
        Assert.True(viewModel.AreControlsEnabled);
        Assert.Equal(RemoteSourceStrings.CanceledTitle, viewModel.Banner!.Title);
        Assert.False(viewModel.HasSummary);
    }

    [Fact]
    public async Task Fetch_RequestsFocusOnCancelBeforeControlsAreDisabled()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Gate = new TaskCompletionSource();
        viewModel.SelectedRepository = viewModel.Repositories[0];

        var running = viewModel.FetchCommand.ExecuteAsync(null);

        Assert.Equal(RemoteFocusKeys.Cancel, viewModel.PendingFocus);
        viewModel.FetchCancelCommand.Execute(null);
        await running;
    }

    [Fact]
    public async Task RateLimitPause_ShowsCountdownBannerThenAnnouncesResume()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Gate = new TaskCompletionSource();
        viewModel.SelectedRepository = viewModel.Repositories[0];
        var running = viewModel.FetchCommand.ExecuteAsync(null);

        harness.RateLimits.Raise(new RateLimitStatus(SourceType.Github, true, DateTimeOffset.UtcNow.AddMinutes(4), 0));

        Assert.True(viewModel.IsPaused);
        Assert.Equal(RemoteSourceStrings.RateTitle("GitHub", true), viewModel.Banner!.Title);
        Assert.Contains("Resuming in", viewModel.Banner.Message);
        Assert.StartsWith("Paused.", viewModel.ProgressText, StringComparison.Ordinal);
        Assert.Contains("about 4 minutes", viewModel.Banner.AutomationName, StringComparison.Ordinal);

        harness.RateLimits.Raise(RateLimitStatus.Running(SourceType.Github));

        Assert.False(viewModel.IsPaused);
        Assert.Equal(RemoteSourceStrings.Resumed, viewModel.Banner!.Title);
        viewModel.FetchCancelCommand.Execute(null);
        await running;
    }

    [Fact]
    public async Task RateLimitStatusForAnotherProvider_IsIgnored()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Gate = new TaskCompletionSource();
        viewModel.SelectedRepository = viewModel.Repositories[0];
        var running = viewModel.FetchCommand.ExecuteAsync(null);

        harness.RateLimits.Raise(new RateLimitStatus(SourceType.AzureDevops, true, DateTimeOffset.UtcNow.AddMinutes(1), 0));

        Assert.False(viewModel.IsPaused);
        viewModel.FetchCancelCommand.Execute(null);
        await running;
    }

    [Fact]
    public async Task Locked_BlocksFetchAndSourceChanges()
    {
        var (_, viewModel) = await ReadyAsync();
        viewModel.SelectedRepository = viewModel.Repositories[0];

        viewModel.SetLocked(true);
        viewModel.Sources.First(chip => chip.Source == SourceType.Local).IsSelected = true;

        Assert.False(viewModel.FetchCommand.CanExecute(null));
        Assert.Equal(SourceType.Github, viewModel.SelectedSource);
        Assert.True(viewModel.Sources.First(chip => chip.Source == SourceType.Github).IsSelected);
    }

    [Fact]
    public async Task ChangeRepository_ReturnsToTheFormKeepingTheLoadedList()
    {
        var (_, viewModel) = await ReadyAsync();
        viewModel.SelectedRepository = viewModel.Repositories[0];
        await viewModel.FetchCommand.ExecuteAsync(null);

        viewModel.ChangeRepositoryCommand.Execute(null);

        Assert.True(viewModel.ShowForm);
        Assert.Equal(RemoteFocusKeys.Repositories, viewModel.PendingFocus);
        Assert.Single(viewModel.Repositories);
    }

    [Fact]
    public async Task TooLargeBanner_UseLocalSwitchesToLocalFiles()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Error = new RemoteSourceException(RemoteFailureKind.RepositoryTooLarge, SourceType.Github);
        viewModel.SelectedRepository = viewModel.Repositories[0];
        await viewModel.FetchCommand.ExecuteAsync(null);

        viewModel.Banner!.ActionCommand!.Execute(null);

        Assert.True(viewModel.IsLocal);
    }

    [Fact]
    public async Task SsoBanner_OpensTheGitHubTokenPageInTheBrowser()
    {
        var (harness, viewModel) = await ReadyAsync();
        harness.Fetcher.Error = new RemoteSourceException(RemoteFailureKind.SsoRequired, SourceType.Github);
        viewModel.SelectedRepository = viewModel.Repositories[0];
        await viewModel.FetchCommand.ExecuteAsync(null);

        viewModel.Banner!.ActionCommand!.Execute(null);

        Assert.Equal(RemoteSourceStrings.SsoTitle("octo"), viewModel.Banner.Title);
        Assert.Equal(new Uri(RemoteSourceStrings.GitHubTokensUrl), Assert.Single(harness.Links.Opened));
    }

    [Fact]
    public void Sources_Always_ListCortexaLast()
    {
        var harness = new RemoteSourceHarness();

        Assert.Equal(SourceType.CortexaRepo, harness.ViewModel.Sources[^1].Source);
    }

    [Fact]
    public async Task SelectingCortexa_WithoutAnyToken_LoadsSavedRepositoriesWithoutAMissingTokenBanner()
    {
        var harness = new RemoteSourceHarness();

        var viewModel = await harness.OpenCortexaAsync(RemoteSourceHarness.CortexaRepo("alpha"));

        Assert.Equal(1, harness.Cortexa.ListCalls);
        Assert.Equal(RemoteListState.Ready, viewModel.ListState);
        Assert.Single(viewModel.Repositories);
        Assert.Null(viewModel.Banner);
        Assert.True(viewModel.ShowForm);
        Assert.True(viewModel.IsCortexa);
    }

    [Fact]
    public async Task SelectingCortexa_Again_ReloadsTheList()
    {
        var harness = new RemoteSourceHarness();
        var viewModel = await harness.OpenCortexaAsync(RemoteSourceHarness.CortexaRepo("alpha"));
        harness.Cortexa.Repositories = [RemoteSourceHarness.CortexaRepo("alpha"), RemoteSourceHarness.CortexaRepo("beta")];

        viewModel.SelectedSource = SourceType.Local;
        viewModel.SelectedSource = SourceType.CortexaRepo;

        Assert.Equal(2, harness.Cortexa.ListCalls);
        Assert.Equal(2, viewModel.Repositories.Count);
    }

    [Fact]
    public async Task SelectingCortexa_EmptyList_ShowsTheCortexaEmptyText()
    {
        var harness = new RemoteSourceHarness();

        var viewModel = await harness.OpenCortexaAsync();

        Assert.True(viewModel.ShowEmptyList);
        Assert.Equal(RemoteSourceStrings.EmptyCortexa, viewModel.EmptyText);
    }

    [Fact]
    public void CortexaListAccessDenied_ShowsADismissibleBannerAndHidesTheCard()
    {
        var harness = new RemoteSourceHarness();
        harness.Cortexa.ListError = new RemoteSourceException(RemoteFailureKind.AccessDenied, SourceType.CortexaRepo);

        harness.ViewModel.SelectedSource = SourceType.CortexaRepo;

        Assert.Equal(RemoteSourceStrings.CortexaDeniedTitle, harness.ViewModel.Banner!.Title);
        Assert.Equal(BannerSeverity.Error, harness.ViewModel.Banner.Severity);
        Assert.True(harness.ViewModel.Banner.CanDismiss);
        Assert.False(harness.ViewModel.ShowForm);
    }

    [Fact]
    public void CortexaListAccessDenied_Dismissed_ClearsTheBanner()
    {
        var harness = new RemoteSourceHarness();
        harness.Cortexa.ListError = new RemoteSourceException(RemoteFailureKind.AccessDenied, SourceType.CortexaRepo);
        harness.ViewModel.SelectedSource = SourceType.CortexaRepo;

        harness.ViewModel.Banner!.DismissCommand!.Execute(null);

        Assert.Null(harness.ViewModel.Banner);
    }

    [Fact]
    public void CortexaListUnauthorized_ShowsATryAgainBannerThatCannotBeDismissed()
    {
        var harness = new RemoteSourceHarness();
        harness.Cortexa.ListError = new RemoteSourceException(RemoteFailureKind.Auth, SourceType.CortexaRepo);

        harness.ViewModel.SelectedSource = SourceType.CortexaRepo;

        var banner = harness.ViewModel.Banner!;
        Assert.Equal(RemoteSourceStrings.CortexaAuthTitle, banner.Title);
        Assert.Equal(RemoteSourceStrings.TryAgain, banner.ActionText);
        Assert.False(banner.CanDismiss);
        Assert.False(harness.ViewModel.ShowForm);
    }

    [Fact]
    public async Task CortexaListUnauthorized_TryAgain_ReloadsAndRestoresTheCard()
    {
        var harness = new RemoteSourceHarness();
        harness.Cortexa.ListError = new RemoteSourceException(RemoteFailureKind.Auth, SourceType.CortexaRepo);
        harness.ViewModel.SelectedSource = SourceType.CortexaRepo;
        harness.Cortexa.ListError = null;
        harness.Cortexa.Repositories = [RemoteSourceHarness.CortexaRepo("alpha")];

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)harness.ViewModel.Banner!.ActionCommand!).ExecuteAsync(null);

        Assert.Equal(2, harness.Cortexa.ListCalls);
        Assert.Null(harness.ViewModel.Banner);
        Assert.True(harness.ViewModel.ShowForm);
        Assert.Single(harness.ViewModel.Repositories);
    }

    [Fact]
    public void CortexaListUpstreamFailure_ShowsTheGenericUpstreamBanner()
    {
        var harness = new RemoteSourceHarness();
        harness.Cortexa.ListError = new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.CortexaRepo);

        harness.ViewModel.SelectedSource = SourceType.CortexaRepo;

        Assert.Equal(RemoteSourceStrings.UpstreamTitle("Cortexa"), harness.ViewModel.Banner!.Title);
    }

    [Fact]
    public async Task SelectingCortexaRepository_BuildsTheBranchFromTheSavedOneWithoutLoadingBranches()
    {
        var harness = new RemoteSourceHarness();
        harness.Cortexa.BranchError = new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.CortexaRepo);
        var viewModel = await harness.OpenCortexaAsync(RemoteSourceHarness.CortexaRepo("alpha", branch: "release"));

        viewModel.SelectedRepository = viewModel.Repositories[0];

        Assert.Equal(["release"], viewModel.Branches.Select(branch => branch.Name));
        Assert.Equal("release", viewModel.SelectedBranch!.Name);
        Assert.Null(viewModel.CommitText);
        Assert.Null(viewModel.Banner);
        Assert.False(viewModel.IsLoadingBranches);
        Assert.True(viewModel.FetchCommand.CanExecute(null));
    }

    [Fact]
    public async Task CortexaFetch_Success_RaisesFilesFetchedWithTheCortexaSource()
    {
        var harness = new RemoteSourceHarness();
        harness.Fetcher.Result = new RemoteFetchResult(SourceType.CortexaRepo, "3f2a9c1e55", ["a.md"], 1, 0, 0, 0, false);
        var viewModel = await harness.OpenCortexaAsync(RemoteSourceHarness.CortexaRepo("alpha", branch: "release"));
        viewModel.SelectedRepository = viewModel.Repositories[0];
        RemoteFilesFetchedEventArgs? raised = null;
        viewModel.FilesFetched += (_, args) => raised = args;

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.Equal(SourceType.CortexaRepo, raised!.Source);
        Assert.Equal(["a.md"], raised.Paths);
        Assert.Equal("octo/alpha @ release", raised.Origin);
        Assert.Equal(SourceType.CortexaRepo, harness.Fetcher.LastRequest!.Repository.Provider);
        Assert.Equal("release", harness.Fetcher.LastRequest.Branch);
        Assert.True(viewModel.HasSummary);
    }

    [Fact]
    public async Task CortexaFetch_RepositoryGone_ShowsTheNotFoundBannerAndKeepsTheForm()
    {
        var harness = new RemoteSourceHarness();
        harness.Fetcher.Error = new RemoteSourceException(RemoteFailureKind.NotFound, SourceType.CortexaRepo);
        var viewModel = await harness.OpenCortexaAsync(RemoteSourceHarness.CortexaRepo("alpha"));
        viewModel.SelectedRepository = viewModel.Repositories[0];

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.Equal(RemoteSourceStrings.NotFoundTitle, viewModel.Banner!.Title);
        Assert.True(viewModel.ShowForm);
        Assert.False(viewModel.IsFetching);
    }

    [Fact]
    public async Task CortexaFetch_SessionExpired_ShowsTheAuthBannerAndHidesTheCard()
    {
        var harness = new RemoteSourceHarness();
        harness.Fetcher.Error = new RemoteSourceException(RemoteFailureKind.Auth, SourceType.CortexaRepo);
        var viewModel = await harness.OpenCortexaAsync(RemoteSourceHarness.CortexaRepo("alpha"));
        viewModel.SelectedRepository = viewModel.Repositories[0];

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.Equal(RemoteSourceStrings.CortexaAuthTitle, viewModel.Banner!.Title);
        Assert.False(viewModel.ShowForm);
    }

    [Fact]
    public async Task CortexaSearch_MatchesTheOwnerThroughTheFullName()
    {
        var harness = new RemoteSourceHarness();
        var viewModel = await harness.OpenCortexaAsync(
            RemoteSourceHarness.CortexaRepo("alpha", owner: "octo"),
            RemoteSourceHarness.CortexaRepo("beta", owner: "acme"));

        viewModel.SearchText = "ACME";

        Assert.Equal(["beta"], viewModel.Repositories.Select(row => row.Name));
    }

    [Fact]
    public void SwitchingAwayFromCortexa_ClearsItsBanner()
    {
        var harness = new RemoteSourceHarness();
        harness.Cortexa.ListError = new RemoteSourceException(RemoteFailureKind.AccessDenied, SourceType.CortexaRepo);
        harness.ViewModel.SelectedSource = SourceType.CortexaRepo;

        harness.ViewModel.SelectedSource = SourceType.Local;

        Assert.Null(harness.ViewModel.Banner);
        Assert.False(harness.ViewModel.IsCortexa);
    }

    [Theory]
    [InlineData("github", "GitHub")]
    [InlineData("azure-devops", "Azure DevOps")]
    [InlineData("gitlab", null)]
    [InlineData(null, null)]
    public void CortexaRow_UpstreamTag_ReflectsTheSavedRepositorySource(string? tag, string? expected)
    {
        var row = new RemoteRepositoryRowViewModel(RemoteSourceHarness.CortexaRepo("alpha", tag), long.MaxValue);

        Assert.Equal(expected, row.UpstreamTag);
        Assert.Equal(expected is not null, row.HasUpstreamTag);
    }

    [Fact]
    public void NonCortexaRow_WithGitHubLikeProject_HasNoUpstreamTag()
    {
        var repository = RemoteSourceHarness.AzureRepo("core") with { Project = "github" };

        var row = new RemoteRepositoryRowViewModel(repository, long.MaxValue);

        Assert.Null(row.UpstreamTag);
    }

    [Fact]
    public void CortexaRow_OverTheSizeLimit_IsMarkedTooBig()
    {
        const long Limit = 100;
        var repository = RemoteSourceHarness.CortexaRepo("alpha") with { SizeBytes = Limit + 1 };

        var row = new RemoteRepositoryRowViewModel(repository, Limit);

        Assert.True(row.IsTooBig);
    }
}
