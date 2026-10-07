using Collector.Application.Extraction;

namespace Collector.Tests.Extraction;

public sealed class TextNormalizerTests
{
    private readonly TextNormalizer _normalizer = new();

    [Fact]
    public void Returns_empty_for_empty_input()
    {
        Assert.Equal(string.Empty, _normalizer.Normalize(string.Empty));
    }

    [Fact]
    public void Converts_crlf_and_cr_to_lf()
    {
        var result = _normalizer.Normalize("line1\r\nline2\rline3");

        Assert.Equal("line1\nline2\nline3", result);
    }

    [Fact]
    public void Collapses_inline_whitespace_and_trims_trailing_spaces()
    {
        var result = _normalizer.Normalize("hello   \t  world  \n  next\t");

        Assert.Equal("hello world\n next", result);
    }

    [Fact]
    public void Collapses_three_or_more_blank_lines_to_one_blank_line()
    {
        var result = _normalizer.Normalize("a\n\n\n\nb");

        Assert.Equal("a\n\nb", result);
    }

    [Fact]
    public void Trims_leading_and_trailing_whitespace()
    {
        var result = _normalizer.Normalize("\n\n  hello world  \n\n");

        Assert.Equal("hello world", result);
    }
}
