using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class KnowledgeMergerTests
{
    private const string Separator = "\n\n";
    private const int HalfDetailsLength = 5000;

    private readonly KnowledgeMerger _merger = new();

    private static ExtractedKnowledgeItem WithLines(ExtractedKnowledgeItem item, int start, int end) =>
        item with { Source = new KnowledgeSource { FilePath = TestData.FilePath, LineStart = start, LineEnd = end } };

    [Fact]
    public void Merge_TitlesDifferInCaseAndWhitespace_MergesIntoOne()
    {
        var items = new[] { TestData.Item("Retry Policy"), TestData.Item("  retry   policy ") };

        var merged = _merger.Merge(items);

        Assert.Single(merged);
    }

    [Fact]
    public void Merge_Group_KeepsLongestSummaryAndItsEchoVerdict()
    {
        var echo = new EchoVerdict(true, EchoReasons.SummaryCopied);
        var items = new[]
        {
            TestData.Item("Same", "short"),
            TestData.Item("Same", "a much longer summary wins") with { EchoVerdict = echo },
            TestData.Item("Same", "medium summary"),
        };

        var merged = Assert.Single(_merger.Merge(items));

        Assert.Equal("a much longer summary wins", merged.Summary);
        Assert.Equal(echo, merged.EchoVerdict);
    }

    [Fact]
    public void Merge_DistinctDetails_JoinedWithBlankLineAndDuplicatesDropped()
    {
        var items = new[]
        {
            TestData.Item("Same") with { Details = "first" },
            TestData.Item("Same") with { Details = "first" },
            TestData.Item("Same") with { Details = " " },
            TestData.Item("Same") with { Details = "second" },
        };

        var merged = Assert.Single(_merger.Merge(items));

        Assert.Equal($"first{Separator}second", merged.Details);
    }

    [Fact]
    public void Merge_NoDetails_LeavesDetailsNull()
    {
        var merged = Assert.Single(_merger.Merge([TestData.Item("Same"), TestData.Item("Same")]));

        Assert.Null(merged.Details);
    }

    [Fact]
    public void Merge_LongDetails_CappedAtDetailsMax()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", HalfDetailsLength / 2));
        var items = new[]
        {
            TestData.Item("Same") with { Details = words },
            TestData.Item("Same") with { Details = words + " more" },
        };

        var merged = Assert.Single(_merger.Merge(items));

        Assert.True(merged.Details!.Length <= UploadLimitsMirror.DetailsMax);
    }

    [Fact]
    public void Merge_FileItemsInSameFile_UnionOfLineRange()
    {
        var items = new[]
        {
            WithLines(TestData.Item("Same"), 10, 12),
            WithLines(TestData.Item("Same"), 5, 8),
            WithLines(TestData.Item("Same"), 11, 20),
        };

        var merged = Assert.Single(_merger.Merge(items));

        Assert.Equal(TestData.FilePath, merged.Source.FilePath);
        Assert.Equal(5, merged.Source.LineStart);
        Assert.Equal(20, merged.Source.LineEnd);
    }

    [Fact]
    public void Merge_ItemsInDifferentFiles_IgnoresOtherFileLines()
    {
        var other = TestData.Item("Same") with { Source = new KnowledgeSource { FilePath = "src/Other.cs", LineStart = 1, LineEnd = 99 } };
        var items = new[] { WithLines(TestData.Item("Same"), 10, 12), other };

        var merged = Assert.Single(_merger.Merge(items));

        Assert.Equal(10, merged.Source.LineStart);
        Assert.Equal(12, merged.Source.LineEnd);
    }

    [Fact]
    public void Merge_PageItems_KeepsEarliestSource()
    {
        var first = TestData.Item("Same") with { UnitKind = UnitKind.Page, Source = new KnowledgeSource { PageNumber = 2 } };
        var second = TestData.Item("Same") with { UnitKind = UnitKind.Page, Source = new KnowledgeSource { PageNumber = 1 } };

        var merged = Assert.Single(_merger.Merge([first, second]));

        Assert.Equal(2, merged.Source.PageNumber);
    }

    [Fact]
    public void Merge_Group_FirstNonNullExcerptWins()
    {
        var items = new[]
        {
            TestData.Item("Same"),
            TestData.Item("Same") with { Excerpt = "one" },
            TestData.Item("Same") with { Excerpt = "two" },
        };

        var merged = Assert.Single(_merger.Merge(items));

        Assert.Equal("one", merged.Excerpt);
    }

    [Fact]
    public void Merge_SameTitleInDifferentDocuments_DoesNotMerge()
    {
        var items = new[] { TestData.Item("Same", documentId: "doc-a"), TestData.Item("Same", documentId: "doc-b") };

        Assert.Equal(2, _merger.Merge(items).Count);
    }

    [Fact]
    public void Merge_SameTitleWithDifferentKinds_DoesNotMerge()
    {
        var items = new[] { TestData.Item("Same", kind: KnowledgeKind.Logic), TestData.Item("Same", kind: KnowledgeKind.Workflow) };

        Assert.Equal(2, _merger.Merge(items).Count);
    }
}
