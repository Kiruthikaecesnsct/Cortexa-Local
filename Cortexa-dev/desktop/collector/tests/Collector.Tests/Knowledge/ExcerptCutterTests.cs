using Collector.Application.Knowledge;

namespace Collector.Tests.Knowledge;

public sealed class ExcerptCutterTests
{
    private const int WordCount = 300;

    private readonly ExcerptCutter _cutter = new();

    [Fact]
    public void Cut_ShortText_ReturnsWholeText()
    {
        var excerpt = _cutter.Cut("short unit text", null);

        Assert.Equal("short unit text", excerpt);
    }

    [Fact]
    public void Cut_LongText_StaysWithinLimitAndEndsOnWholeWord()
    {
        var text = string.Join(' ', Enumerable.Repeat("abcdef", WordCount));

        var excerpt = _cutter.Cut(text, null)!;

        Assert.True(excerpt.Length <= UploadLimitsMirror.ExcerptMax);
        Assert.All(excerpt.Split(' '), word => Assert.Equal("abcdef", word));
    }

    [Fact]
    public void Cut_AnchorGiven_StartsAtAnchorOffset()
    {
        var anchor = new Anchor(11, 4, null, null);

        var excerpt = _cutter.Cut("0123456789 tail text", anchor);

        Assert.Equal("tail text", excerpt);
    }

    [Fact]
    public void Cut_NoAnchor_StartsAtUnitStart()
    {
        var excerpt = _cutter.Cut("head text", null);

        Assert.StartsWith("head", excerpt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public void Cut_EmptyUnit_ReturnsNull(string text)
    {
        Assert.Null(_cutter.Cut(text, null));
    }
}
