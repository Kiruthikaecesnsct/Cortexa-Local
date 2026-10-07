using Collector.Application.Extraction;

namespace Collector.Tests.Extraction;

public sealed class HeadingDetectorTests
{
    private readonly HeadingDetector _detector = new();

    [Fact]
    public void Detects_atx_markdown_heading()
    {
        var headings = _detector.DetectHeadings("# Introduction\nSome body text.");

        Assert.Single(headings);
        Assert.Equal("Introduction", headings[0].Text);
    }

    [Fact]
    public void Detects_setext_heading_with_underline()
    {
        var headings = _detector.DetectHeadings("Overview\n========\nBody text here.");

        Assert.Single(headings);
        Assert.Equal("Overview", headings[0].Text);
    }

    [Fact]
    public void Detects_numbered_section_heading()
    {
        var headings = _detector.DetectHeadings("1.2 Background Research\nSome content follows.");

        Assert.Single(headings);
        Assert.Equal("1.2 Background Research", headings[0].Text);
    }

    [Fact]
    public void Detects_allcaps_section_heading()
    {
        var headings = _detector.DetectHeadings("RELATED WORK\nSome content follows here.");

        Assert.Single(headings);
        Assert.Equal("RELATED WORK", headings[0].Text);
    }

    [Fact]
    public void Detects_title_case_heading_followed_by_capitalized_line()
    {
        var headings = _detector.DetectHeadings("Experimental Results And Discussion\nWe observed strong gains.");

        Assert.Single(headings);
        Assert.Equal("Experimental Results And Discussion", headings[0].Text);
    }

    [Fact]
    public void Returns_empty_for_blank_text()
    {
        Assert.Empty(_detector.DetectHeadings("   \n  "));
    }

    [Fact]
    public void Finds_nearest_preceding_heading_for_a_position()
    {
        var text = "# First\nbody one\n# Second\nbody two";
        var headings = _detector.DetectHeadings(text);

        var positionInSecondSection = text.IndexOf("body two", StringComparison.Ordinal);
        var result = _detector.FindHeadingForPosition(headings, positionInSecondSection);

        Assert.Equal("Second", result);
    }

    [Fact]
    public void Returns_null_when_position_precedes_all_headings()
    {
        var text = "intro text\n# First\nbody";
        var headings = _detector.DetectHeadings(text);

        var result = _detector.FindHeadingForPosition(headings, 0);

        Assert.Null(result);
    }
}
