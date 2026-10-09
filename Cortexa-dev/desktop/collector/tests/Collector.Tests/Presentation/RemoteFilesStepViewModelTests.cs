using Collector.Application.Extraction;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;

namespace Collector.Tests.Presentation;

public sealed class RemoteFilesStepViewModelTests
{
    private const string TreeCommit = "tree-commit-1";
    private const int SingleFileLimit = 1;

    private static readonly RemoteBranch[] Branches = [new("main", "3f2a9c1e55"), new("develop", "9b1e0d47aa")];

    private static RemoteTree Tree() => new(
        TreeCommit,
        [
            RemoteData.Entry("README.md", "s1", 100),
            RemoteData.Entry("docs/guide.md", "s2", 200),
            RemoteData.Entry("docs/logo.png", "s3", 50),
        ],
        false);

    private static async Task<(RemoteSourceHarness Harness, RemoteSourceViewModel ViewModel)> OnBranchStepAsync(
        Action<RemoteFetchOptions>? configureOptions = null)
    {
        var harness = new RemoteSourceHarness(configureOptions);
        harness.GitHub.Branches = Branches;
        harness.GitHub.Tree = Tree();
        var viewModel = await harness.OpenGitHubAsync(RemoteSourceHarness.GitHubRepo("alpha"));
        viewModel.SelectedRepository = viewModel.Repositories[0];
        return (harness, viewModel);
    }

    private static async Task<(RemoteSourceHarness Harness, RemoteSourceViewModel ViewModel)> OnFilesStepAsync(
        Action<RemoteFetchOptions>? configureOptions = null)
    {
        var (harness, viewModel) = await OnBranchStepAsync(configureOptions);
        viewModel.SelectedBranch = viewModel.Branches[0];
        return (harness, viewModel);
    }

    private static FileTreeRowViewModel Row(RemoteSourceViewModel viewModel, string name) =>
        viewModel.FileTree.VisibleRows.Single(row => row.Name == name);

    private static void Check(RemoteSourceViewModel viewModel, string name) =>
        viewModel.FileTree.ToggleCheck(Row(viewModel, name));

    private static void CheckDocsFolderAndReadme(RemoteSourceViewModel viewModel)
    {
        Check(viewModel, "docs");
        Check(viewModel, "README.md");
    }

    [Fact]
    public async Task SelectingABranch_ReadsTheTreeOnceAndLoadsTheFileTree()
    {
        var (harness, viewModel) = await OnFilesStepAsync();

        Assert.Equal(1, harness.GitHub.TreeCalls);
        Assert.True(viewModel.FileTree.IsLoaded);
        Assert.Equal(RemoteWizardStep.Files, viewModel.Step);
        Assert.True(viewModel.ShowFilesStep);
        Assert.False(viewModel.IsLoadingTree);
    }

    [Fact]
    public async Task Proceed_NothingSelected_CannotExecute()
    {
        var (_, viewModel) = await OnFilesStepAsync();

        Assert.False(viewModel.ProceedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Proceed_SupportedFileSelected_CanExecute()
    {
        var (_, viewModel) = await OnFilesStepAsync();

        Check(viewModel, "README.md");

        Assert.True(viewModel.ProceedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Proceed_OnlyAnUnsupportedFileSelected_CannotExecute()
    {
        var (_, viewModel) = await OnFilesStepAsync();
        viewModel.FileTree.ToggleExpand(Row(viewModel, "docs"));

        Check(viewModel, "logo.png");

        Assert.False(viewModel.ProceedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Proceed_SelectionOverTheConfiguredLimit_CannotExecute()
    {
        var (_, viewModel) = await OnFilesStepAsync(options => options.MaxSelectedFiles = SingleFileLimit);

        CheckDocsFolderAndReadme(viewModel);

        Assert.True(viewModel.FileTree.IsOverLimit);
        Assert.False(viewModel.ProceedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Proceed_Execute_SendsTheTreeCommitAndOnlySupportedEntries()
    {
        var (harness, viewModel) = await OnFilesStepAsync();
        CheckDocsFolderAndReadme(viewModel);

        await viewModel.ProceedCommand.ExecuteAsync(null);

        var selection = harness.Fetcher.LastRequest!.Selection!;
        Assert.Equal(TreeCommit, selection.CommitSha);
        Assert.Equal(["README.md", "docs/guide.md"], selection.Entries.Select(entry => entry.Path));
        Assert.Equal("main", harness.Fetcher.LastRequest.Branch);
    }

    [Fact]
    public async Task Proceed_Success_MovesToTheProceedStepAndRaisesFilesFetchedWithRepoPaths()
    {
        var (_, viewModel) = await OnFilesStepAsync();
        Check(viewModel, "README.md");
        RemoteFilesFetchedEventArgs? raised = null;
        viewModel.FilesFetched += (_, args) => raised = args;

        await viewModel.ProceedCommand.ExecuteAsync(null);

        Assert.Equal(RemoteWizardStep.Proceed, viewModel.Step);
        Assert.True(viewModel.ShowProceedStep);
        Assert.True(viewModel.ShowChangeFiles);
        Assert.True(viewModel.HasSummary);
        Assert.Equal(["docs/a.md", "docs/b.md"], raised!.Files.Select(file => file.RepoPath));
        Assert.Equal("octo/alpha @ main", raised.Origin);
    }

    [Fact]
    public async Task ProceedSelectionText_AfterSelecting_ReportsSupportedFilesAndSize()
    {
        var (_, viewModel) = await OnFilesStepAsync();

        Check(viewModel, "docs");

        Assert.Equal(RemoteSourceStrings.SelectionText(1, RemoteSizeFormatter.Format(200)), viewModel.ProceedSelectionText);
        Assert.True(viewModel.HasProceedSkipped);
        Assert.Equal(RemoteSourceStrings.SkippedText(1, 0), viewModel.ProceedSkippedText);
    }

    [Fact]
    public async Task ChangeFiles_AfterProceed_ReturnsToTheFilesStepKeepingTheSelection()
    {
        var (_, viewModel) = await OnFilesStepAsync();
        Check(viewModel, "README.md");
        await viewModel.ProceedCommand.ExecuteAsync(null);

        viewModel.ChangeFilesCommand.Execute(null);

        Assert.Equal(RemoteWizardStep.Files, viewModel.Step);
        Assert.True(viewModel.FileTree.IsLoaded);
        Assert.True(viewModel.FileTree.CanProceed);
        Assert.Equal(["README.md"], viewModel.FileTree.BuildSelection().Entries.Select(entry => entry.Path));
        Assert.Null(viewModel.Summary);
        Assert.Equal(RemoteFocusKeys.FileSearch, viewModel.PendingFocus);
    }

    [Fact]
    public async Task ChangeBranch_AfterSelecting_ClearsTheTreeAndSelection()
    {
        var (_, viewModel) = await OnFilesStepAsync();
        Check(viewModel, "README.md");

        viewModel.ChangeBranchCommand.Execute(null);

        Assert.Equal(RemoteWizardStep.Branch, viewModel.Step);
        Assert.False(viewModel.FileTree.IsLoaded);
        Assert.False(viewModel.FileTree.CanProceed);
        Assert.Empty(viewModel.FileTree.BuildSelection().Entries);
    }

    [Fact]
    public async Task ChangeBranch_ThenPickingAgain_LoadsAFreshTreeWithNothingSelected()
    {
        var (harness, viewModel) = await OnFilesStepAsync();
        Check(viewModel, "README.md");
        viewModel.ChangeBranchCommand.Execute(null);

        viewModel.SelectedBranch = viewModel.Branches[1];

        Assert.Equal(2, harness.GitHub.TreeCalls);
        Assert.True(viewModel.FileTree.IsLoaded);
        Assert.False(viewModel.FileTree.CanProceed);
    }

    [Fact]
    public async Task ChangeRepository_FromTheFilesStep_ClearsTheTree()
    {
        var (_, viewModel) = await OnFilesStepAsync();
        Check(viewModel, "README.md");

        viewModel.ChangeRepositoryCommand.Execute(null);

        Assert.Equal(RemoteWizardStep.Repository, viewModel.Step);
        Assert.False(viewModel.FileTree.IsLoaded);
    }

    [Fact]
    public async Task TreeLoadFailure_ShowsTheBannerAndLeavesTheTreeUnloaded()
    {
        var (harness, viewModel) = await OnBranchStepAsync();
        harness.GitHub.TreeError = new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.Github);

        viewModel.SelectedBranch = viewModel.Branches[0];

        Assert.Equal(RemoteSourceStrings.UpstreamTitle("GitHub"), viewModel.Banner!.Title);
        Assert.False(viewModel.FileTree.IsLoaded);
        Assert.False(viewModel.ProceedCommand.CanExecute(null));
        Assert.False(viewModel.IsLoadingTree);
    }

    [Fact]
    public async Task TreeLoading_WhileInFlight_DisablesFileControlsUntilItCompletes()
    {
        var (harness, viewModel) = await OnBranchStepAsync();
        harness.GitHub.TreeGate = new TaskCompletionSource();

        viewModel.SelectedBranch = viewModel.Branches[0];

        Assert.True(viewModel.IsLoadingTree);
        Assert.False(viewModel.AreFileControlsEnabled);
        harness.GitHub.TreeGate.SetResult();
        await Task.Yield();
        Assert.False(viewModel.IsLoadingTree);
        Assert.True(viewModel.AreFileControlsEnabled);
    }

    [Fact]
    public async Task TreeLoading_BranchChangedWhileInFlight_IgnoresTheStaleResult()
    {
        var (harness, viewModel) = await OnBranchStepAsync();
        harness.GitHub.TreeGate = new TaskCompletionSource();
        viewModel.SelectedBranch = viewModel.Branches[0];

        viewModel.ChangeBranchCommand.Execute(null);
        harness.GitHub.TreeGate.SetResult();
        await Task.Yield();

        Assert.False(viewModel.FileTree.IsLoaded);
        Assert.False(viewModel.IsLoadingTree);
    }

    [Fact]
    public async Task CortexaFetch_WithoutAFileTree_SendsNoSelection()
    {
        var harness = new RemoteSourceHarness();
        var viewModel = await harness.OpenCortexaAsync(RemoteSourceHarness.CortexaRepo("alpha", branch: "release"));
        viewModel.SelectedRepository = viewModel.Repositories[0];
        viewModel.SelectedBranch ??= viewModel.Branches[0];

        await viewModel.FetchCommand.ExecuteAsync(null);

        Assert.Null(harness.Fetcher.LastRequest!.Selection);
    }
}
