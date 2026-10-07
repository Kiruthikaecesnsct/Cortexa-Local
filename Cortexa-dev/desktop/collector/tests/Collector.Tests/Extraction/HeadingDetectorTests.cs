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

    [Theory]
    [InlineData("Ab Cd Efgh", true)]
    [InlineData("Ab Cd Efg", false)]
    public void Title_case_line_is_a_heading_only_from_ten_characters(string line, bool expected)
    {
        var headings = _detector.DetectHeadings($"{line}\nBody follows.");

        Assert.Equal(expected, headings.Count == 1);
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public void Title_case_line_is_a_heading_only_up_to_eighty_characters(int length, bool expected)
    {
        var line = TitleCaseLineOfLength(length);

        var headings = _detector.DetectHeadings($"{line}\nBody follows.");

        Assert.Equal(expected, headings.Count == 1);
    }

    [Theory]
    [InlineData("Experimental Results Discussion?")]
    [InlineData("Experimental Results Discussion.")]
    [InlineData("Experimental Results Discussion!")]
    public void Title_case_line_ending_in_sentence_punctuation_is_not_a_heading(string line)
    {
        var headings = _detector.DetectHeadings($"{line}\nBody follows.");

        Assert.Empty(headings);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void Title_case_line_is_a_heading_only_for_three_to_twelve_words(int wordCount, bool expected)
    {
        var line = string.Join(' ', Enumerable.Repeat("Alpha", wordCount));

        var headings = _detector.DetectHeadings($"{line}\nBody follows.");

        Assert.Equal(expected, headings.Count == 1);
    }

    [Fact]
    public void Title_case_line_followed_by_a_lowercase_line_is_not_a_heading()
    {
        var headings = _detector.DetectHeadings("Experimental Results Discussion\nbody starts lowercase.");

        Assert.Empty(headings);
    }

    [Theory]
    [InlineData("ABC", false)]
    [InlineData("ABCD", true)]
    public void All_caps_line_is_a_heading_only_from_four_characters(string line, bool expected)
    {
        var headings = _detector.DetectHeadings($"{line}\nbody text follows.");

        Assert.Equal(expected, headings.Count == 1);
    }

    [Fact]
    public void Digits_only_line_is_not_an_all_caps_heading()
    {
        var headings = _detector.DetectHeadings("12345\nbody text follows.");

        Assert.Empty(headings);
    }

    private static string TitleCaseLineOfLength(int length)
    {
        var words = new List<string> { "Alpha", "Beta" };
        var filler = new string('x', length - "Alpha Beta ".Length);
        words.Add($"G{filler[1..]}");
        return string.Join(' ', words);
    }
}
