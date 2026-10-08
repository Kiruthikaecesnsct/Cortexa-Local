using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class UploadFieldClampTests
{
    private const int OversizedLength = 5000;

    private static ExtractedKnowledgeItem Page(KnowledgeSource source) =>
        TestData.Item() with { UnitKind = UnitKind.Page, Source = source };

    private static ExtractedKnowledgeItem Section(KnowledgeSource source) =>
        TestData.Item() with { UnitKind = UnitKind.Section, Source = source };

    private static ExtractedKnowledgeItem File(KnowledgeSource source) => TestData.Item() with { Source = source };

    private static ExtractedKnowledgeItem Module(KnowledgeSource source) =>
        TestData.Item(kind: KnowledgeKind.Layer) with { UnitKind = UnitKind.Module, Source = source };

    [Fact]
    public void Truncate_UnderLimit_ReturnsTrimmedText()
    {
        Assert.Equal("short", UploadFieldClamp.Truncate("  short  ", 20));
    }

    [Fact]
    public void Truncate_OverLimit_CutsAtWordBoundary()
    {
        Assert.Equal("alpha beta", UploadFieldClamp.Truncate("alpha beta gamma", 12));
    }

    [Fact]
    public void Truncate_NoLateBoundary_CutsAtLimit()
    {
        Assert.Equal("ab cdefghi", UploadFieldClamp.Truncate("ab cdefghijklmnop", 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TruncateOptional_BlankInput_ReturnsNull(string? text)
    {
        Assert.Null(UploadFieldClamp.TruncateOptional(text, 10));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(7, 7)]
    public void ToKnowledgeItem_PageUnit_PageNumberAtLeastOne(int? page, int expected)
    {
        var result = UploadFieldClamp.ToKnowledgeItem(Page(new KnowledgeSource { PageNumber = page }));

        Assert.Equal(expected, result.Source.PageNumber);
    }

    [Fact]
    public void ToKnowledgeItem_SectionWithoutTitle_UsesFallbackSection()
    {
        var result = UploadFieldClamp.ToKnowledgeItem(Section(new KnowledgeSource { PageNumber = 2, Section = " " }));

        Assert.Equal("Untitled section", result.Source.Section);
    }

    [Fact]
    public void ToKnowledgeItem_SectionWithInvalidPage_DropsPage()
    {
        var result = UploadFieldClamp.ToKnowledgeItem(Section(new KnowledgeSource { PageNumber = 0, Section = "Intro" }));

        Assert.Null(result.Source.PageNumber);
        Assert.Equal("Intro", result.Source.Section);
    }

    [Fact]
    public void ToKnowledgeItem_FileWithoutPath_FallsBackToDocumentPath()
    {
        var item = File(new KnowledgeSource { LineStart = 1, LineEnd = 2 });

        var result = UploadFieldClamp.ToKnowledgeItem(item);

        Assert.Equal(item.DocumentPath, result.Source.FilePath);
    }

    [Fact]
    public void ToKnowledgeItem_FileLineEndBeforeStart_RaisedToStart()
    {
        var result = UploadFieldClamp.ToKnowledgeItem(File(new KnowledgeSource { FilePath = "a.cs", LineStart = 9, LineEnd = 4 }));

        Assert.Equal(9, result.Source.LineStart);
        Assert.Equal(9, result.Source.LineEnd);
    }

    [Fact]
    public void ToKnowledgeItem_FileLinesBelowOne_AreDropped()
    {
        var result = UploadFieldClamp.ToKnowledgeItem(File(new KnowledgeSource { FilePath = "a.cs", LineStart = 0, LineEnd = -1 }));

        Assert.Null(result.Source.LineStart);
        Assert.Null(result.Source.LineEnd);
    }

    [Fact]
    public void ToKnowledgeItem_ModuleUnit_HasFolderPathAndNoLineRange()
    {
        var result = UploadFieldClamp.ToKnowledgeItem(Module(new KnowledgeSource { FilePath = "src/app/", LineStart = 1, LineEnd = 2 }));

        Assert.Equal("src/app/", result.Source.FilePath);
        Assert.Null(result.Source.LineStart);
        Assert.Null(result.Source.LineEnd);
    }

    [Fact]
    public void ToKnowledgeItem_ModuleUnitWithoutPath_FallsBackToDocumentPath()
    {
        var item = Module(new KnowledgeSource());

        var result = UploadFieldClamp.ToKnowledgeItem(item);

        Assert.Equal(item.DocumentPath, result.Source.FilePath);
        Assert.Null(result.Source.LineStart);
    }

    [Fact]
    public void ToKnowledgeItem_OversizedFields_AreClampedToLimits()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", OversizedLength));
        var item = TestData.Item() with
        {
            Title = text,
            Summary = text,
            Details = text,
            Excerpt = text,
            Source = new KnowledgeSource { FilePath = new string('p', OversizedLength), LineStart = 1, LineEnd = 1 },
        };

        var result = UploadFieldClamp.ToKnowledgeItem(item);

        Assert.True(result.Title.Length <= UploadLimitsMirror.TitleMax);
        Assert.True(result.Summary.Length <= UploadLimitsMirror.SummaryMax);
        Assert.True(result.Details!.Length <= UploadLimitsMirror.DetailsMax);
        Assert.True(result.Excerpt!.Length <= UploadLimitsMirror.ExcerptMax);
        Assert.True(result.Source.FilePath!.Length <= UploadLimitsMirror.FilePathMax);
    }
}
