using Collector.Application.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class UnitSplitterTests
{
    private const int StartLine = 10;

    private readonly UnitSplitter _splitter = new();

    [Fact]
    public void Split_FileUnit_SplitsNearMiddleAndRecomputesLines()
    {
        var unit = TestData.FileUnit("line1\nline2\nline3\nline4", StartLine);

        var halves = _splitter.Split(unit)!.Value;

        Assert.Equal("line1\nline2", halves.First.Text);
        Assert.Equal("line3\nline4", halves.Second.Text);
        Assert.Equal(StartLine, halves.First.StartLine);
        Assert.Equal(StartLine + 1, halves.First.EndLine);
        Assert.Equal(StartLine + 2, halves.Second.StartLine);
        Assert.Equal(unit.EndLine, halves.Second.EndLine);
    }

    [Fact]
    public void Split_Halves_GetDerivedIds()
    {
        var unit = TestData.FileUnit("line1\nline2\nline3\nline4", StartLine);

        var halves = _splitter.Split(unit)!.Value;

        Assert.Equal("unit-1.a", halves.First.Id);
        Assert.Equal("unit-1.b", halves.Second.Id);
    }

    [Fact]
    public void Split_BreakCloserToStart_ChoosesNearestBreak()
    {
        var unit = TestData.FileUnit("aaaaaaaaaa\nbbbbbbbbbbbb\ncc", StartLine);

        var halves = _splitter.Split(unit)!.Value;

        Assert.Equal("aaaaaaaaaa", halves.First.Text);
    }

    [Theory]
    [InlineData("single line only")]
    [InlineData("")]
    public void Split_NoLineBreak_ReturnsNull(string text)
    {
        Assert.Null(_splitter.Split(TestData.FileUnit(text, StartLine)));
    }

    [Fact]
    public void Split_BlankHalf_ReturnsNull()
    {
        Assert.Null(_splitter.Split(TestData.FileUnit("   \nabc", StartLine)));
    }

    [Fact]
    public void Split_PageUnit_SplitsTextAndKeepsPage()
    {
        var unit = TestData.PageUnit("first part\nsecond part");

        var halves = _splitter.Split(unit)!.Value;

        Assert.Equal(TestData.DefaultPage, halves.First.PageNumber);
        Assert.Equal(TestData.DefaultPage, halves.Second.PageNumber);
        Assert.Null(halves.First.StartLine);
    }

    [Fact]
    public void Split_Halves_TokenCountIsAtLeastOne()
    {
        var unit = TestData.FileUnit("a\nb") with { TokenCount = 1 };

        var halves = _splitter.Split(unit)!.Value;

        Assert.True(halves.First.TokenCount >= 1);
        Assert.True(halves.Second.TokenCount >= 1);
    }
}
