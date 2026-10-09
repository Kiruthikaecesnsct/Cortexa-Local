using Collector.Domain.Enums;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class DocumentFilterTests
{
    private static DocumentRowViewModel Row(string repoPath, DocumentStatus status = DocumentStatus.Extracted) =>
        new(SplitOutcomes.Make(@"C:\cache\blob.md", status, repoPath: repoPath));

    [Theory]
    [InlineData(DocumentStatusFilter.All, DocumentStatus.Extracted, true)]
    [InlineData(DocumentStatusFilter.All, DocumentStatus.Failed, true)]
    [InlineData(DocumentStatusFilter.All, DocumentStatus.Excluded, true)]
    [InlineData(DocumentStatusFilter.Analyzed, DocumentStatus.Extracted, true)]
    [InlineData(DocumentStatusFilter.Analyzed, DocumentStatus.Failed, false)]
    [InlineData(DocumentStatusFilter.Failed, DocumentStatus.Failed, true)]
    [InlineData(DocumentStatusFilter.Failed, DocumentStatus.Excluded, false)]
    [InlineData(DocumentStatusFilter.Skipped, DocumentStatus.Excluded, true)]
    [InlineData(DocumentStatusFilter.Skipped, DocumentStatus.Extracted, false)]
    public void Matches_StatusFilter_AcceptsOnlyTheMatchingStatus(DocumentStatusFilter filter, DocumentStatus status, bool expected)
    {
        var row = Row("docs/a.md", status);

        Assert.Equal(expected, DocumentFilter.Matches(row, filter, string.Empty));
    }

    [Theory]
    [InlineData("GUIDE", true)]
    [InlineData("  guide  ", true)]
    [InlineData("docs", true)]
    [InlineData("DOCS/guides", true)]
    [InlineData("missing", false)]
    public void Matches_Search_ChecksFilenameAndFolderIgnoringCaseAndPadding(string search, bool expected)
    {
        var row = Row("docs/guides/setup-guide.md");

        Assert.Equal(expected, DocumentFilter.Matches(row, DocumentStatusFilter.All, search));
    }

    [Fact]
    public void Matches_BlankSearch_AcceptsEveryRow()
    {
        Assert.True(DocumentFilter.Matches(Row("docs/a.md"), DocumentStatusFilter.All, "   "));
    }

    [Fact]
    public void Matches_StatusAndSearchTogether_BothMustHold()
    {
        var row = Row("docs/guide.md", DocumentStatus.Failed);

        Assert.False(DocumentFilter.Matches(row, DocumentStatusFilter.Analyzed, "guide"));
        Assert.True(DocumentFilter.Matches(row, DocumentStatusFilter.Failed, "guide"));
    }
}
