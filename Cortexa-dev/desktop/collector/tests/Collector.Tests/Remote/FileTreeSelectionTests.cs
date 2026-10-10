using Collector.Application.Extraction;
using Collector.Application.Remote;
using Collector.Application.Remote.Selection;
using Collector.Domain.Remote;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Remote;

public sealed class FileTreeSelectionTests
{
    private const int ReadmeBytes = 100;
    private const int GuideBytes = 200;
    private const int ReferenceBytes = 300;
    private const int LogoBytes = 50;
    private const int GenerousLimit = 100;
    private const int TightLimit = 2;
    private const int SupportedFileCount = 3;
    private const int ExpectedHiddenCount = 2;

    private readonly FileTreeBuilder _builder = new(new RemoteFileFilter(MsOptions.Create(new RemoteFetchOptions())));
    private readonly FileTreeBuild _build;

    public FileTreeSelectionTests()
    {
        _build = _builder.Build(new RemoteTree("commit-1", Entries(), false));
    }

    private static RemoteTreeEntry[] Entries() =>
    [
        RemoteData.Entry("README.md", "s1", ReadmeBytes),
        RemoteData.Entry("docs/guide.md", "s2", GuideBytes),
        RemoteData.Entry("docs/api/ref.md", "s3", ReferenceBytes),
        RemoteData.Entry("docs/logo.png", "s4", LogoBytes),
        RemoteData.Entry("src/big.md", "s5", FileContentGuard.MaxFileBytes + 1),
        RemoteData.Entry("node_modules/pkg/readme.md", "s6", 10),
        RemoteData.Entry("src/bin/out.md", "s7", 10),
    ];

    private FileTreeSelection NewSelection(int limit = GenerousLimit) => new(_build.Root, limit);

    private FileTreeNode Node(string path)
    {
        var current = _build.Root;
        foreach (var segment in path.Split('/'))
        {
            current = current.Children.Single(child => child.Name == segment);
        }

        return current;
    }

    [Fact]
    public void Build_ExcludedFolderEntries_AreCountedAsHiddenAndNeverBuilt()
    {
        Assert.Equal(ExpectedHiddenCount, _build.HiddenExcluded);
        Assert.DoesNotContain(_build.Root.Children, child => child.Name == "node_modules");
        Assert.DoesNotContain(Node("src").Children, child => child.Name == "bin");
    }

    [Fact]
    public void Build_Tree_RollsUpVerdictCountsAndSupportedBytes()
    {
        var total = _build.Root.Total;

        Assert.Equal(5, total.Files);
        Assert.Equal(SupportedFileCount, total.Supported);
        Assert.Equal(1, total.Unsupported);
        Assert.Equal(1, total.TooLarge);
        Assert.Equal(ReadmeBytes + GuideBytes + ReferenceBytes, total.SupportedBytes);
    }

    [Fact]
    public void Children_Folder_ListsFoldersFirstThenFilesByName()
    {
        var names = Node("docs").Children.Select(child => child.Name);

        Assert.Equal(["api", "guide.md", "logo.png"], names);
    }

    [Fact]
    public void Build_NothingChecked_EveryNodeIsUnchecked()
    {
        Assert.False(_build.Root.IsChecked);
        Assert.False(Node("docs").IsChecked);
        Assert.False(Node("README.md").IsChecked);
    }

    [Fact]
    public void SetChecked_OneFileInAFolder_ParentsBecomePartial()
    {
        var selection = NewSelection();

        selection.SetChecked(Node("docs/guide.md"), true);

        Assert.True(Node("docs/guide.md").IsChecked);
        Assert.Null(Node("docs").IsChecked);
        Assert.Null(_build.Root.IsChecked);
        Assert.False(Node("docs/api").IsChecked);
    }

    [Fact]
    public void SetChecked_EveryChildOfAFolder_FolderBecomesChecked()
    {
        var selection = NewSelection();

        selection.SetChecked(Node("docs/guide.md"), true);
        selection.SetChecked(Node("docs/logo.png"), true);
        selection.SetChecked(Node("docs/api/ref.md"), true);

        Assert.True(Node("docs").IsChecked);
        Assert.True(Node("docs/api").IsChecked);
        Assert.Null(_build.Root.IsChecked);
    }

    [Fact]
    public void SetChecked_Folder_CascadesToEveryDescendant()
    {
        var selection = NewSelection();

        selection.SetChecked(Node("docs"), true);

        Assert.True(Node("docs").IsChecked);
        Assert.True(Node("docs/api/ref.md").IsChecked);
        Assert.True(Node("docs/logo.png").IsChecked);
        Assert.False(Node("README.md").IsChecked);
        Assert.Null(_build.Root.IsChecked);
    }

    [Fact]
    public void SetChecked_UncheckingFolder_ClearsDescendantsAndUpdatesAncestors()
    {
        var selection = NewSelection();
        selection.SetChecked(_build.Root, true);

        selection.SetChecked(Node("docs"), false);

        Assert.False(Node("docs").IsChecked);
        Assert.False(Node("docs/guide.md").IsChecked);
        Assert.True(Node("README.md").IsChecked);
        Assert.Null(_build.Root.IsChecked);
    }

    [Fact]
    public void SetChecked_SameStateTwice_DoesNotDoubleCount()
    {
        var selection = NewSelection();

        selection.SetChecked(Node("README.md"), true);
        selection.SetChecked(Node("README.md"), true);

        Assert.Equal(1, selection.Summary().SelectedFiles);
    }

    [Fact]
    public void SelectAll_NoScope_ChecksEveryFile()
    {
        var selection = NewSelection();

        selection.SelectAll();

        Assert.True(_build.Root.IsChecked);
        Assert.Equal(_build.Root.Total.Files, selection.Summary().SelectedFiles);
    }

    [Fact]
    public void SelectAll_WithScope_ChecksOnlyTheScopedNodes()
    {
        var selection = NewSelection();

        selection.SelectAll([Node("docs/guide.md"), Node("README.md")]);

        Assert.True(Node("docs/guide.md").IsChecked);
        Assert.True(Node("README.md").IsChecked);
        Assert.False(Node("docs/api/ref.md").IsChecked);
        Assert.Equal(2, selection.Summary().SelectedFiles);
    }

    [Fact]
    public void Clear_WithScope_UnchecksOnlyTheScopedNodes()
    {
        var selection = NewSelection();
        selection.SelectAll();

        selection.Clear([Node("docs/guide.md")]);

        Assert.False(Node("docs/guide.md").IsChecked);
        Assert.True(Node("docs/api/ref.md").IsChecked);
        Assert.Null(Node("docs").IsChecked);
    }

    [Fact]
    public void Clear_NoScope_UnchecksEverything()
    {
        var selection = NewSelection();
        selection.SelectAll();

        selection.Clear();

        Assert.False(_build.Root.IsChecked);
        Assert.Equal(0, selection.Summary().SelectedFiles);
    }

    [Fact]
    public void OnlySupported_AfterSelectingEverything_KeepsOnlySupportedFilesChecked()
    {
        var selection = NewSelection();
        selection.SelectAll();

        selection.OnlySupported();

        var summary = selection.Summary();
        Assert.Equal(SupportedFileCount, summary.SelectedFiles);
        Assert.Equal(SupportedFileCount, summary.SupportedSelected);
        Assert.Equal(0, summary.SkippedUnsupported);
        Assert.Equal(0, summary.SkippedTooLarge);
        Assert.False(Node("docs/logo.png").IsChecked);
        Assert.False(Node("src/big.md").IsChecked);
    }

    [Fact]
    public void Summary_FolderWithUnsupportedFile_CountsSkippedAndSumsSupportedBytesOnly()
    {
        var selection = NewSelection();

        selection.SetChecked(Node("docs"), true);

        var summary = selection.Summary();
        Assert.Equal(3, summary.SelectedFiles);
        Assert.Equal(2, summary.SupportedSelected);
        Assert.Equal(1, summary.SkippedUnsupported);
        Assert.Equal(0, summary.SkippedTooLarge);
        Assert.Equal(GuideBytes + ReferenceBytes, summary.SelectedBytes);
    }

    [Fact]
    public void Summary_TooLargeFileChecked_CountsAsSkippedTooLarge()
    {
        var selection = NewSelection();

        selection.SetChecked(Node("src/big.md"), true);

        var summary = selection.Summary();
        Assert.Equal(1, summary.SkippedTooLarge);
        Assert.Equal(0, summary.SupportedSelected);
        Assert.Equal(0, summary.SelectedBytes);
    }

    [Fact]
    public void Summary_SupportedSelectedAboveTheLimit_IsOverLimit()
    {
        var selection = NewSelection(TightLimit);

        selection.OnlySupported();

        Assert.True(selection.Summary().OverLimit);
    }

    [Fact]
    public void Summary_SupportedSelectedAtTheLimit_IsNotOverLimit()
    {
        var selection = NewSelection(SupportedFileCount);

        selection.OnlySupported();

        Assert.False(selection.Summary().OverLimit);
    }

    [Fact]
    public void Summary_UnsupportedFilesDoNotCountTowardTheLimit()
    {
        var selection = NewSelection(TightLimit);

        selection.SetChecked(Node("docs/logo.png"), true);
        selection.SetChecked(Node("src/big.md"), true);
        selection.SetChecked(Node("README.md"), true);

        Assert.False(selection.Summary().OverLimit);
    }

    [Fact]
    public void SelectedSupportedEntries_MixedSelection_ReturnsOnlySupportedEntriesSortedByPath()
    {
        var selection = NewSelection();
        selection.SelectAll();

        var paths = selection.SelectedSupportedEntries().Select(entry => entry.Path);

        Assert.Equal(["README.md", "docs/api/ref.md", "docs/guide.md"], paths);
    }

    [Fact]
    public void SelectedSupportedEntries_NothingSelected_IsEmpty()
    {
        Assert.Empty(NewSelection().SelectedSupportedEntries());
    }

    [Fact]
    public void Build_BackslashPaths_AreSplitIntoFolders()
    {
        var tree = new RemoteTree("commit-1", [RemoteData.Entry("docs\\deep\\note.md", "s1", 10)], false);

        var build = _builder.Build(tree);

        Assert.Equal("note.md", build.Root.Children.Single().Children.Single().Children.Single().Name);
    }

    private FileTreeBuild BuildTree(params RemoteTreeEntry[] entries) => _builder.Build(new RemoteTree("commit-2", entries, false));

    [Fact]
    public void Summary_SupportedFilesWithoutSize_CountUnknownAndAddNoBytes()
    {
        var build = BuildTree(RemoteData.Entry("a.md", "u1"), RemoteData.Entry("docs/b.md", "u2"));
        var selection = new FileTreeSelection(build.Root, GenerousLimit);

        selection.SetChecked(build.Root, true);

        var summary = selection.Summary();
        Assert.Equal(2, summary.UnknownSizeFiles);
        Assert.Equal(0, summary.SelectedBytes);
        Assert.Equal(2, summary.SupportedSelected);
    }

    [Fact]
    public void Summary_MixedKnownAndUnknownSizes_BytesCountKnownOnly()
    {
        var build = BuildTree(
            RemoteData.Entry("a.md", "u1"),
            RemoteData.Entry("b.md", "u2", ReadmeBytes),
            RemoteData.Entry("logo.png", "u3"));
        var selection = new FileTreeSelection(build.Root, GenerousLimit);

        selection.SetChecked(build.Root, true);

        var summary = selection.Summary();
        Assert.Equal(1, summary.UnknownSizeFiles);
        Assert.Equal(ReadmeBytes, summary.SelectedBytes);
    }

    [Fact]
    public void SetChecked_FolderCheckedThenUnchecked_ReturnsCountsToZero()
    {
        var build = BuildTree(RemoteData.Entry("docs/a.md", "u1"), RemoteData.Entry("docs/b.md", "u2", ReadmeBytes));
        var selection = new FileTreeSelection(build.Root, GenerousLimit);
        var folder = build.Root.Children.Single(child => child.Name == "docs");

        selection.SetChecked(folder, true);
        selection.SetChecked(folder, false);

        Assert.Equal(FileTreeCounts.Zero, build.Root.Selected);
        Assert.Equal(0, selection.Summary().UnknownSizeFiles);
        Assert.Equal(0, selection.Summary().SelectedBytes);
    }
}
