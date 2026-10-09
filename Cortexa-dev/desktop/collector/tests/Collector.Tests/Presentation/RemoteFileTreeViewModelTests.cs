using Collector.Application.Extraction;
using Collector.Application.Remote;
using Collector.Application.Remote.Selection;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Presentation;

public sealed class RemoteFileTreeViewModelTests : IDisposable
{
    private const string CommitSha = "commit-tree";
    private const int GenerousLimit = 100;
    private const int SingleFileLimit = 1;
    private const int SearchDelayMilliseconds = 200;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
    private readonly FileTreeBuilder _builder = new(new RemoteFileFilter(MsOptions.Create(new RemoteFetchOptions())));
    private readonly List<RemoteFileTreeViewModel> _created = [];

    public void Dispose() => _created.ForEach(viewModel => viewModel.Dispose());

    private static RemoteTree Tree(bool truncated = false) => new(
        CommitSha,
        [
            RemoteData.Entry("README.md", "s1", 100),
            RemoteData.Entry("docs/guide.md", "s2", 200),
            RemoteData.Entry("docs/api/ref.md", "s3", 300),
            RemoteData.Entry("docs/logo.png", "s4", 50),
            RemoteData.Entry("src/big.md", "s5", FileContentGuard.MaxFileBytes + 1),
            RemoteData.Entry("node_modules/pkg/readme.md", "s6", 10),
        ],
        truncated);

    private RemoteFileTreeViewModel Loaded(int limit = GenerousLimit, bool truncated = false)
    {
        var viewModel = new RemoteFileTreeViewModel(_builder, limit, _time);
        _created.Add(viewModel);
        viewModel.Load(Tree(truncated));
        return viewModel;
    }

    private static FileTreeRowViewModel Row(RemoteFileTreeViewModel viewModel, string name) =>
        viewModel.VisibleRows.Single(row => row.Name == name);

    private static FileTreeRowViewModel Expanded(RemoteFileTreeViewModel viewModel, params string[] folders)
    {
        FileTreeRowViewModel row = null!;
        foreach (var folder in folders)
        {
            row = Row(viewModel, folder);
            if (!row.IsExpanded)
            {
                viewModel.ToggleExpand(row);
            }
        }

        return row;
    }

    private static string[] VisibleNames(RemoteFileTreeViewModel viewModel) =>
        [.. viewModel.VisibleRows.Select(row => row.Name)];

    [Fact]
    public void Load_Tree_ShowsTopLevelFoldersFirstAndNothingSelected()
    {
        var viewModel = Loaded();

        Assert.True(viewModel.IsLoaded);
        Assert.Equal(["docs", "src", "README.md"], VisibleNames(viewModel));
        Assert.False(viewModel.CanProceed);
        Assert.Equal(RemoteSourceStrings.NoFilesSelected, viewModel.SummaryText);
        Assert.Equal(RemoteSourceStrings.SelectFileHint, viewModel.ProceedHint);
    }

    [Fact]
    public void Load_TreeWithExcludedFolders_ShowsTheHiddenNotice()
    {
        var viewModel = Loaded();

        Assert.Equal(RemoteSourceStrings.HiddenExcludedNotice(1), viewModel.HiddenExcludedNotice);
    }

    [Fact]
    public void Load_TruncatedTree_ShowsTheTruncatedNotice()
    {
        var viewModel = Loaded(truncated: true);

        Assert.Equal(RemoteSourceStrings.TruncatedTreeNotice, viewModel.TruncatedNotice);
    }

    [Fact]
    public void Load_CompleteTree_HasNoTruncatedNotice()
    {
        Assert.Equal(string.Empty, Loaded().TruncatedNotice);
    }

    [Fact]
    public void ToggleExpand_Folder_ShowsItsChildrenIndented()
    {
        var viewModel = Loaded();

        var docs = Expanded(viewModel, "docs");

        Assert.Equal(["docs", "api", "guide.md", "logo.png", "src", "README.md"], VisibleNames(viewModel));
        Assert.Equal(0, docs.Depth);
        Assert.Equal(1, Row(viewModel, "api").Depth);
    }

    [Fact]
    public void ToggleCheck_SupportedFile_EnablesProceed()
    {
        var viewModel = Loaded();

        viewModel.ToggleCheck(Row(viewModel, "README.md"));

        Assert.True(viewModel.CanProceed);
        Assert.Equal(string.Empty, viewModel.ProceedHint);
    }

    [Fact]
    public void ToggleCheck_OnlyAnUnsupportedFile_KeepsProceedDisabled()
    {
        var viewModel = Loaded();
        Expanded(viewModel, "docs");

        viewModel.ToggleCheck(Row(viewModel, "logo.png"));

        Assert.False(viewModel.CanProceed);
        Assert.Equal(RemoteSourceStrings.SelectFileHint, viewModel.ProceedHint);
    }

    [Fact]
    public void ToggleCheck_Twice_RestoresTheUncheckedState()
    {
        var viewModel = Loaded();
        var readme = Row(viewModel, "README.md");

        viewModel.ToggleCheck(readme);
        viewModel.ToggleCheck(readme);

        Assert.False(viewModel.CanProceed);
        Assert.False(readme.IsChecked);
    }

    [Fact]
    public void ToggleCheck_Folder_PartiallyCheckedAfterOneChildIsUnchecked()
    {
        var viewModel = Loaded();
        var docs = Expanded(viewModel, "docs");
        viewModel.ToggleCheck(docs);

        viewModel.ToggleCheck(Row(viewModel, "guide.md"));

        Assert.Null(docs.IsChecked);
        Assert.True(viewModel.CanProceed);
    }

    [Fact]
    public void ToggleCheck_MoreSupportedFilesThanTheLimit_IsOverLimitAndBlocksProceed()
    {
        var viewModel = Loaded(SingleFileLimit);

        viewModel.ToggleCheck(Row(viewModel, "docs"));

        Assert.True(viewModel.IsOverLimit);
        Assert.False(viewModel.CanProceed);
        Assert.Equal(RemoteSourceStrings.OverLimitHint(SingleFileLimit), viewModel.ProceedHint);
    }

    [Fact]
    public void ToggleCheck_Selection_UpdatesTheSummaryText()
    {
        var viewModel = Loaded();

        viewModel.ToggleCheck(Row(viewModel, "docs"));

        var expected = RemoteSourceStrings.FilesSummary(2, RemoteSizeFormatter.Format(500), 1, 0);
        Assert.Equal(expected, viewModel.SummaryText);
    }

    [Fact]
    public void SearchText_BeforeTheDebounceElapses_LeavesTheRowsUnchanged()
    {
        var viewModel = Loaded();

        viewModel.SearchText = "guide";
        _time.Advance(TimeSpan.FromMilliseconds(SearchDelayMilliseconds - 1));

        Assert.Equal(["docs", "src", "README.md"], VisibleNames(viewModel));
        Assert.False(viewModel.IsSearchActive);
    }

    [Fact]
    public void SearchText_AfterTheDebounceElapses_ShowsMatchesWithTheirFolders()
    {
        var viewModel = Loaded();

        viewModel.SearchText = "guide";
        _time.Advance(TimeSpan.FromMilliseconds(SearchDelayMilliseconds));

        Assert.True(viewModel.IsSearchActive);
        Assert.Equal(["docs", "guide.md"], VisibleNames(viewModel));
    }

    [Fact]
    public void SearchText_TypedAgainBeforeTheDelay_RestartsTheDebounce()
    {
        var viewModel = Loaded();

        viewModel.SearchText = "g";
        _time.Advance(TimeSpan.FromMilliseconds(150));
        viewModel.SearchText = "guide";
        _time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.False(viewModel.IsSearchActive);
        _time.Advance(TimeSpan.FromMilliseconds(50));

        Assert.Equal(["docs", "guide.md"], VisibleNames(viewModel));
    }

    [Fact]
    public void SearchText_NoMatches_ShowsTheNoMatchState()
    {
        var viewModel = Loaded();

        viewModel.SearchText = "zzz";
        _time.Advance(TimeSpan.FromMilliseconds(SearchDelayMilliseconds));

        Assert.True(viewModel.ShowNoMatch);
        Assert.Equal(RemoteSourceStrings.NoFileMatch("zzz"), viewModel.NoMatchText);
        Assert.Empty(viewModel.VisibleRows);
    }

    [Fact]
    public void SelectAllCommand_WhileSearching_SelectsOnlyTheMatchingFiles()
    {
        var viewModel = Loaded();
        viewModel.SearchText = "guide";
        _time.Advance(TimeSpan.FromMilliseconds(SearchDelayMilliseconds));

        viewModel.SelectAllCommand.Execute(null);

        var paths = viewModel.BuildSelection().Entries.Select(entry => entry.Path);
        Assert.Equal(["docs/guide.md"], paths);
        Assert.Equal(RemoteSourceStrings.SelectAllMatching, viewModel.SelectAllLabel);
    }

    [Fact]
    public void ClearCommand_WhileSearching_ClearsOnlyTheMatchingFiles()
    {
        var viewModel = Loaded();
        viewModel.OnlySupportedCommand.Execute(null);
        viewModel.SearchText = "guide";
        _time.Advance(TimeSpan.FromMilliseconds(SearchDelayMilliseconds));

        viewModel.ClearCommand.Execute(null);

        var paths = viewModel.BuildSelection().Entries.Select(entry => entry.Path);
        Assert.Equal(["README.md", "docs/api/ref.md"], paths);
    }

    [Fact]
    public void ClearSearchCommand_AfterSearching_RestoresTheFullTreeAndKeepsMatchedFoldersOpen()
    {
        var viewModel = Loaded();
        viewModel.SearchText = "guide";
        _time.Advance(TimeSpan.FromMilliseconds(SearchDelayMilliseconds));

        viewModel.ClearSearchCommand.Execute(null);

        Assert.False(viewModel.IsSearchActive);
        Assert.Equal(["docs", "api", "guide.md", "logo.png", "src", "README.md"], VisibleNames(viewModel));
    }

    [Fact]
    public void OnlySupportedCommand_AfterSelectingEverything_ExcludesUnsupportedFiles()
    {
        var viewModel = Loaded();
        viewModel.SelectAllCommand.Execute(null);

        viewModel.OnlySupportedCommand.Execute(null);

        Assert.Equal(3, viewModel.SelectionSummary.SelectedFiles);
        Assert.Equal(0, viewModel.SelectionSummary.SkippedUnsupported);
    }

    [Fact]
    public void BuildSelection_MixedSelection_CarriesTheCommitAndOnlySupportedEntries()
    {
        var viewModel = Loaded();
        viewModel.SelectAllCommand.Execute(null);

        var selection = viewModel.BuildSelection();

        Assert.Equal(CommitSha, selection.CommitSha);
        Assert.Equal(["README.md", "docs/api/ref.md", "docs/guide.md"], selection.Entries.Select(entry => entry.Path));
        Assert.False(selection.TreeTruncated);
    }

    [Fact]
    public void BuildSelection_TruncatedTree_CarriesTheTruncatedFlag()
    {
        var viewModel = Loaded(truncated: true);

        Assert.True(viewModel.BuildSelection().TreeTruncated);
    }

    [Fact]
    public void Unload_AfterSelecting_ClearsTheTreeSelectionAndProceed()
    {
        var viewModel = Loaded();
        viewModel.ToggleCheck(Row(viewModel, "README.md"));

        viewModel.Unload();

        Assert.False(viewModel.IsLoaded);
        Assert.False(viewModel.CanProceed);
        Assert.Empty(viewModel.VisibleRows);
        Assert.Empty(viewModel.BuildSelection().Entries);
    }

    [Fact]
    public void Load_Again_DiscardsThePreviousSelection()
    {
        var viewModel = Loaded();
        viewModel.ToggleCheck(Row(viewModel, "README.md"));

        viewModel.Load(Tree());

        Assert.False(viewModel.CanProceed);
        Assert.Empty(viewModel.BuildSelection().Entries);
    }

    [Fact]
    public void Load_EmptyTree_ShowsTheEmptyState()
    {
        var viewModel = new RemoteFileTreeViewModel(_builder, GenerousLimit, _time);
        _created.Add(viewModel);

        viewModel.Load(new RemoteTree(CommitSha, [], false));

        Assert.True(viewModel.ShowEmpty);
    }
}
