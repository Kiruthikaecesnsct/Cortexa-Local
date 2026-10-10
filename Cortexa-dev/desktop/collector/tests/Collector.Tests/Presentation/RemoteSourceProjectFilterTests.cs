using System.Windows.Controls;
using System.Windows.Data;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class RemoteSourceProjectFilterTests
{
    private static readonly RemoteBranch[] Branches = [new("main", "3f2a9c1e55")];

    private static Task<RemoteSourceViewModel> OpenAsync(RemoteSourceHarness harness) =>
        harness.OpenAzureAsync(
            RemoteSourceHarness.AzureRepo("api", "research"),
            RemoteSourceHarness.AzureRepo("web", "Platform"),
            RemoteSourceHarness.AzureRepo("core", "Platform"),
            RemoteSourceHarness.AzureRepo("lab", "Research"));

    private static ProjectOption Option(RemoteSourceViewModel viewModel, string label) =>
        viewModel.ProjectOptions.Single(option => option.Label == label);

    [Fact]
    public async Task AzureProjectOptions_AreAllProjectsThenDistinctSortedNames()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());

        Assert.Equal([RemoteSourceStrings.ProjectAll, "Platform", "research"], viewModel.ProjectOptions.Select(option => option.Label));
        Assert.Null(viewModel.ProjectOptions[0].Value);
        Assert.Same(viewModel.ProjectOptions[0], viewModel.SelectedProjectOption);
    }

    [Fact]
    public async Task ProjectOption_ToString_ReturnsTheLabel()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());

        Assert.Equal("Platform", Option(viewModel, "Platform").ToString());
    }

    [Fact]
    public async Task ShowFlags_FollowTheSelectedSource()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());

        Assert.True(viewModel.ShowProjectFilter);
        Assert.False(viewModel.ShowVisibilityFilter);

        viewModel.SelectedSource = SourceType.Github;

        Assert.False(viewModel.ShowProjectFilter);
        Assert.True(viewModel.ShowVisibilityFilter);
    }

    [Fact]
    public async Task SelectingAProject_FiltersTheRowsIgnoringCase()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());

        viewModel.SelectedProjectOption = Option(viewModel, "research");

        Assert.Equal(["api", "lab"], viewModel.Repositories.Select(row => row.Name));
    }

    [Fact]
    public async Task GitHubPrivateFilter_DoesNotHideAzureRepositories()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());

        viewModel.VisibilityFilter = RepositoryVisibility.Public;

        Assert.Equal(4, viewModel.Repositories.Count);
    }

    [Fact]
    public async Task Reload_KeepsTheProjectWhenItStillExists()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());
        viewModel.SelectedProjectOption = Option(viewModel, "Platform");

        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        Assert.Equal("Platform", viewModel.ProjectFilter);
        Assert.Equal(["core", "web"], viewModel.Repositories.Select(row => row.Name));
    }

    [Fact]
    public async Task Reload_ResetsToAllWhenTheProjectNoLongerExists()
    {
        var harness = new RemoteSourceHarness();
        var viewModel = await OpenAsync(harness);
        viewModel.SelectedProjectOption = Option(viewModel, "Platform");
        harness.AzureDevOps.Repositories = [RemoteSourceHarness.AzureRepo("api", "research")];

        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ProjectFilter);
        Assert.Equal(2, viewModel.ProjectOptions.Count);
        Assert.Single(viewModel.Repositories);
    }

    [Fact]
    public async Task NoMatchText_SearchAndProject_NamesBoth()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());
        viewModel.SelectedProjectOption = Option(viewModel, "Platform");

        viewModel.SearchText = "zzz";

        Assert.Equal(RemoteSourceStrings.NoMatchInProjectQuery("zzz", "Platform"), viewModel.NoMatchText);
        Assert.Equal(RemoteSourceStrings.ClearFilters, viewModel.ClearActionLabel);
    }

    [Fact]
    public async Task NoMatchText_SearchOnly_KeepsClearSearch()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());

        viewModel.SearchText = "zzz";

        Assert.Equal(RemoteSourceStrings.NoMatch("zzz"), viewModel.NoMatchText);
        Assert.Equal(RemoteSourceStrings.ClearSearch, viewModel.ClearActionLabel);
    }

    [Fact]
    public void NoMatchText_ProjectOnly_NamesTheProject()
    {
        var state = new RepositoryFilterState(string.Empty, "Beta", false);

        Assert.Equal(RemoteSourceStrings.NoMatchInProject("Beta"), RepositoryNoMatch.Text(state));
        Assert.Equal(RemoteSourceStrings.ClearFiltersName, RepositoryNoMatch.ActionName(state));
    }

    [Fact]
    public async Task GitHubVisibilityOnly_UsesTheGenericFilteredMessage()
    {
        var harness = new RemoteSourceHarness();
        var viewModel = await harness.OpenGitHubAsync(RemoteSourceHarness.GitHubRepo("alpha"));

        viewModel.VisibilityFilter = RepositoryVisibility.Private;

        Assert.True(viewModel.ShowNoMatch);
        Assert.Equal(RemoteSourceStrings.NoMatchFiltered(), viewModel.NoMatchText);
        Assert.Equal(RemoteSourceStrings.ClearFiltersName, viewModel.ClearActionName);
    }

    [Fact]
    public async Task ClearFilters_ResetsSearchProjectAndVisibilityButKeepsTheSort()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());
        viewModel.SelectedProjectOption = Option(viewModel, "Platform");
        viewModel.SearchText = "web";
        viewModel.VisibilityFilter = RepositoryVisibility.Private;
        viewModel.SortOrder = RepositorySort.RecentlyUpdated;

        viewModel.ClearFiltersCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Null(viewModel.ProjectFilter);
        Assert.Equal(RepositoryVisibility.All, viewModel.VisibilityFilter);
        Assert.Equal(RepositorySort.RecentlyUpdated, viewModel.SortOrder);
        Assert.Equal(4, viewModel.Repositories.Count);
        Assert.Equal(RemoteFocusKeys.Search, viewModel.PendingFocus);
    }

    [Fact]
    public async Task AzureFetch_RateLimitPause_ReachesTheIntakeAndClearsOnRunning()
    {
        var harness = new RemoteSourceHarness();
        harness.AzureDevOps.Branches = Branches;
        var viewModel = await OpenAsync(harness);
        viewModel.SelectedRepository = viewModel.Repositories[0];
        viewModel.SelectedBranch ??= viewModel.Branches[0];
        harness.Fetcher.Gate = new TaskCompletionSource();
        var running = viewModel.FetchCommand.ExecuteAsync(null);

        harness.RateLimits.Raise(new RateLimitStatus(SourceType.AzureDevops, true, DateTimeOffset.UtcNow.AddMinutes(2), 0));
        harness.IntakeTime.Advance(IntakeTiming.FlushInterval);

        Assert.True(viewModel.IsPaused);
        Assert.Equal(ExtractionStrings.IntakePaused(1, 2), harness.Intake.FetchedText);

        harness.RateLimits.Raise(RateLimitStatus.Running(SourceType.AzureDevops));
        harness.IntakeTime.Advance(IntakeTiming.FlushInterval);

        Assert.False(viewModel.IsPaused);
        Assert.Equal(ExtractionStrings.IntakeFetched(1, 2), harness.Intake.FetchedText);
        viewModel.FetchCancelCommand.Execute(null);
        await running;
    }

    [Fact]
    public async Task AzureFetch_SummaryCardShowsTheTooLargeCountFromTheResult()
    {
        var harness = new RemoteSourceHarness();
        harness.AzureDevOps.Branches = Branches;
        harness.Fetcher.Result = new RemoteFetchResult(SourceType.AzureDevops, "abcdef1234", [new FetchedFile("a.md", "a.md")], 1, 0, 0, 3, false);
        var viewModel = await OpenAsync(harness);
        viewModel.SelectedRepository = viewModel.Repositories[0];
        viewModel.SelectedBranch ??= viewModel.Branches[0];

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.Contains("3 too large", viewModel.Summary!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AzureRow_ShowsTheProjectDateAndAutomationName()
    {
        var updated = DateTimeOffset.UtcNow.AddDays(-3);

        var row = new RemoteRepositoryRowViewModel(RemoteSourceHarness.AzureRepo("core", "Research", updated), long.MaxValue);

        Assert.Equal(RemoteSourceStrings.ProjectUpdatedText("3 days ago"), row.UpdatedText);
        Assert.True(row.ShowUpdatedAfterSize);
        Assert.False(row.ShowUpdatedBeforeSize);
        Assert.Equal("core, project Research, default branch main, 2 KB, project updated 3 days ago", row.AutomationName);
    }

    [Fact]
    public void AzureRow_WithoutADate_HidesTheDateAndOmitsItFromTheName()
    {
        var row = new RemoteRepositoryRowViewModel(RemoteSourceHarness.AzureRepo("core"), 1024);

        Assert.False(row.HasUpdatedText);
        Assert.Equal("core, project Research, default branch main, 2 KB, over the 1 KB fetch limit", row.AutomationName);
    }

    [Fact]
    public void GitHubRow_KeepsTheOriginalDateTextAndOrder()
    {
        var repository = RemoteSourceHarness.GitHubRepo("alpha") with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2) };

        var row = new RemoteRepositoryRowViewModel(repository, long.MaxValue);

        Assert.Equal(RemoteSourceStrings.UpdatedText("2 days ago"), row.UpdatedText);
        Assert.True(row.ShowUpdatedBeforeSize);
    }

    [Fact]
    public async Task NullWriteBack_FallsBackToAllProjectsWithEveryRepositoryShowing()
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());
        viewModel.SelectedProjectOption = Option(viewModel, "Platform");

        viewModel.SelectedProjectOption = null;

        Assert.Same(viewModel.ProjectOptions[0], viewModel.SelectedProjectOption);
        Assert.Equal(4, viewModel.Repositories.Count);
    }

    [Fact]
    public Task ComboBox_BoundToTheViewModel_KeepsItsSelectionAcrossAReload() => UiThreadHost.RunAsync(async () =>
    {
        var viewModel = await OpenAsync(new RemoteSourceHarness());
        var combo = new ComboBox { DataContext = viewModel };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(RemoteSourceViewModel.ProjectOptions)));
        combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(RemoteSourceViewModel.SelectedProjectOption)));
        viewModel.SelectedProjectOption = Option(viewModel, "Platform");

        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        Assert.Equal("Platform", viewModel.ProjectFilter);
        Assert.Same(viewModel.SelectedProjectOption, combo.SelectedItem);
        Assert.Equal(["core", "web"], viewModel.Repositories.Select(row => row.Name));
    });
}
