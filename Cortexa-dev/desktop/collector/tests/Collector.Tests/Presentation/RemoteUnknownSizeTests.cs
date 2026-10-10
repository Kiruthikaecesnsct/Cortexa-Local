using Collector.Application.Remote;
using Collector.Application.Remote.Selection;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;

namespace Collector.Tests.Presentation;

public sealed class RemoteUnknownSizeTests
{
    private static readonly RemoteBranch[] Branches = [new("main", "3f2a9c1e55")];

    private static FileSelectionSummary Summary(int supported, long bytes, int unknown, int unsupported = 0, int tooLarge = 0) =>
        new(supported + unsupported, bytes, unsupported, tooLarge, supported, false, unknown);

    private static async Task<RemoteSourceViewModel> OnFilesStepAsync(params RemoteTreeEntry[] entries)
    {
        var harness = new RemoteSourceHarness();
        harness.AzureDevOps.Branches = Branches;
        harness.AzureDevOps.Tree = new RemoteTree("tree-1", entries, false);
        var viewModel = await harness.OpenAzureAsync(RemoteSourceHarness.AzureRepo("core"));
        viewModel.SelectedRepository = viewModel.Repositories[0];
        viewModel.SelectedBranch = viewModel.Branches[0];
        return viewModel;
    }

    private static FileTreeRowViewModel Row(RemoteSourceViewModel viewModel, string name) =>
        viewModel.FileTree.VisibleRows.Single(row => row.Name == name);

    [Fact]
    public void SizePhrase_AllUnknown_SaysSizeUnknown()
    {
        Assert.Equal(RemoteSourceStrings.SizeUnknown, RemoteSelectionText.SizePhrase(Summary(3, 0, 3)));
    }

    [Fact]
    public void SizePhrase_Mixed_ReportsAtLeastWithTheUnknownCount()
    {
        Assert.Equal("at least 2 KB (1 size unknown)", RemoteSelectionText.SizePhrase(Summary(3, 2048, 1)));
        Assert.Equal("at least 2 KB (2 sizes unknown)", RemoteSelectionText.SizePhrase(Summary(3, 2048, 2)));
    }

    [Fact]
    public void SizePhrase_NoneUnknown_IsTheFormattedSize()
    {
        Assert.Equal("2 KB", RemoteSelectionText.SizePhrase(Summary(3, 2048, 0)));
    }

    [Fact]
    public void SizePhrase_NothingSelected_IsNotTreatedAsUnknown()
    {
        Assert.Equal("0 KB", RemoteSelectionText.SizePhrase(Summary(0, 0, 0)));
    }

    [Fact]
    public void FooterText_AllUnknown_OmitsTheTooLargeSegment()
    {
        Assert.Equal("3 files · size unknown · 2 unsupported skipped", RemoteSelectionText.FooterText(Summary(3, 0, 3, unsupported: 2)));
    }

    [Fact]
    public void FooterText_Mixed_KeepsTheTooLargeSegment()
    {
        var text = RemoteSelectionText.FooterText(Summary(3, 2048, 1, unsupported: 1, tooLarge: 2));

        Assert.Equal("3 files · at least 2 KB (1 size unknown) · 1 unsupported · 2 too large skipped", text);
    }

    [Fact]
    public void SkippedLine_AllUnknown_ShowsOnlyUnsupportedAndHidesWhenZero()
    {
        Assert.Equal(RemoteSourceStrings.SkippedUnsupportedOnly(2), RemoteSelectionText.SkippedLine(Summary(3, 0, 3, unsupported: 2)));
        Assert.Equal(string.Empty, RemoteSelectionText.SkippedLine(Summary(3, 0, 3)));
    }

    [Fact]
    public async Task AllUnknownSelection_ShowsSizeUnknownAndProceedStaysEnabled()
    {
        var viewModel = await OnFilesStepAsync(
            RemoteData.Entry("readme.md", "s1"),
            RemoteData.Entry("guide.md", "s2"),
            RemoteData.Entry("logo.png", "s3"));

        viewModel.FileTree.ToggleCheck(Row(viewModel, "readme.md"));
        viewModel.FileTree.ToggleCheck(Row(viewModel, "guide.md"));
        viewModel.FileTree.ToggleCheck(Row(viewModel, "logo.png"));

        Assert.Equal("2 files · size unknown · 1 unsupported skipped", viewModel.FileTree.SummaryText);
        Assert.Equal("2 files · size unknown", viewModel.ProceedSelectionText);
        Assert.Equal(RemoteSourceStrings.SkippedUnsupportedOnly(1), viewModel.ProceedSkippedText);
        Assert.True(viewModel.FileTree.HasUnknownSize);
        Assert.True(viewModel.ProceedCommand.CanExecute(null));
    }

    [Fact]
    public async Task MixedSelection_ShowsAtLeastInBothTexts()
    {
        var viewModel = await OnFilesStepAsync(RemoteData.Entry("readme.md", "s1"), RemoteData.Entry("guide.md", "s2", 2048));

        viewModel.FileTree.ToggleCheck(Row(viewModel, "readme.md"));
        viewModel.FileTree.ToggleCheck(Row(viewModel, "guide.md"));

        Assert.Equal("2 files · at least 2 KB (1 size unknown) · 0 unsupported · 0 too large skipped", viewModel.FileTree.SummaryText);
        Assert.Equal("2 files · at least 2 KB (1 size unknown)", viewModel.ProceedSelectionText);
        Assert.True(viewModel.FileTree.HasUnknownSize);
    }

    [Fact]
    public async Task KnownSizesOnly_HasNoUnknownNote()
    {
        var viewModel = await OnFilesStepAsync(RemoteData.Entry("guide.md", "s2", 2048));

        viewModel.FileTree.ToggleCheck(Row(viewModel, "guide.md"));

        Assert.Equal("1 files · 2 KB", viewModel.ProceedSelectionText);
        Assert.False(viewModel.FileTree.HasUnknownSize);
    }

    [Fact]
    public async Task NullSizeFileRow_HasEmptySizeTextAndTheSizeUnknownName()
    {
        var viewModel = await OnFilesStepAsync(RemoteData.Entry("readme.md", "s1"));
        var row = Row(viewModel, "readme.md");

        viewModel.FileTree.ToggleCheck(row);

        Assert.Equal(string.Empty, row.SizeText);
        Assert.Equal("readme.md, size unknown, level 1, selected", row.AutomationName);
    }
}
