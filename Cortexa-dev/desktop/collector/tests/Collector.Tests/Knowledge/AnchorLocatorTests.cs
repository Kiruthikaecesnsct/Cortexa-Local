using Collector.Application.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class AnchorLocatorTests
{
    private const string Z = "​";

    private readonly AnchorLocator _locator = new();

    [Fact]
    public void Locate_ExactQuote_ReturnsOffsetAndLength()
    {
        var unit = TestData.PageUnit("alpha beta gamma");

        var anchor = _locator.Locate(unit, "beta");

        Assert.Equal(new Anchor(6, 4, null, null), anchor);
    }

    [Fact]
    public void Locate_WhitespaceDiffers_MatchesCollapsedAndCoversOriginalSpan()
    {
        var unit = TestData.PageUnit("foo   bar\n  baz");

        var anchor = _locator.Locate(unit, "foo bar baz");

        Assert.Equal(new Anchor(0, unit.Text.Length, null, null), anchor);
    }

    [Fact]
    public void Locate_QuoteContainsZeroWidthSpace_StripsItBeforeMatching()
    {
        var unit = TestData.PageUnit("alpha beta gamma");

        var anchor = _locator.Locate(unit, $"be{Z}ta");

        Assert.Equal(6, anchor!.Offset);
    }

    [Theory]
    [InlineData("x -- y", "x - - y")]
    [InlineData("a == b", "a = = b")]
    public void Locate_NeutralizedSubstitutions_AreUndone(string text, string quote)
    {
        var unit = TestData.PageUnit(text);

        var anchor = _locator.Locate(unit, quote);

        Assert.Equal(new Anchor(0, text.Length, null, null), anchor);
    }

    [Fact]
    public void Locate_NoMatch_ReturnsNull()
    {
        var unit = TestData.PageUnit("alpha beta gamma");

        Assert.Null(_locator.Locate(unit, "delta"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(Z)]
    public void Locate_NullOrBlankQuote_ReturnsNull(string? quote)
    {
        var unit = TestData.PageUnit("alpha beta gamma");

        Assert.Null(_locator.Locate(unit, quote));
    }

    [Fact]
    public void Locate_FileUnit_ComputesLinesFromStartLine()
    {
        var unit = TestData.FileUnit("one\ntwo\nthree\nfour", startLine: 5);

        var anchor = _locator.Locate(unit, "three");

        Assert.Equal(7, anchor!.LineStart);
        Assert.Equal(7, anchor.LineEnd);
    }

    [Fact]
    public void Locate_MultiLineQuote_LineEndCoversAllLines()
    {
        var unit = TestData.FileUnit("one\ntwo\nthree\nfour", startLine: 5);

        var anchor = _locator.Locate(unit, "two\nthree");

        Assert.Equal(6, anchor!.LineStart);
        Assert.Equal(7, anchor.LineEnd);
    }

    [Fact]
    public void Locate_FileUnitWithoutLineRange_ReturnsOffsetsWithNullLines()
    {
        var unit = TestData.FileUnit("one\ntwo") with { StartLine = null, EndLine = null };

        var anchor = _locator.Locate(unit, "two");

        Assert.Equal(new Anchor(4, 3, null, null), anchor);
    }

    [Fact]
    public void Locate_FileLinesOutsideDeclaredRange_ReturnsNull()
    {
        var unit = TestData.FileUnit("one\ntwo\nthree") with { EndLine = TestData.DefaultStartLine + 1 };

        Assert.Null(_locator.Locate(unit, "three"));
    }

    [Theory]
    [InlineData(0, 57)]
    [InlineData(1, 63)]
    [InlineData(2, 47)]
    public void Locate_HandwrittenCodeFixture_ReturnsExpectedLine(int itemIndex, int expectedLine)
    {
        using var fixture = KnowledgeFixtures.Load("handwritten-code-response.json");
        var unit = KnowledgeFixtures.UnitFrom(fixture.RootElement.GetProperty("unit"));
        var quote = fixture.RootElement.GetProperty("response").GetProperty("items")[itemIndex].GetProperty("anchor_quote").GetString();

        var anchor = _locator.Locate(unit, quote);

        Assert.Equal(expectedLine, anchor!.LineStart);
        Assert.Equal(expectedLine, anchor.LineEnd);
    }
}
