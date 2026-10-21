using Collector.Application.Candidates;

namespace Collector.Tests.Candidates;

public sealed class CandidateParserTests
{
    private const string SourceFile = "C:\\docs\\paper.pdf";
    private const string ValidItem =
        "{\"title\":\"Adaptive cache\",\"summary\":\"Summary text.\",\"patentability_rationale\":\"Looks new.\",\"excerpt_quote\":\"exact quote\"}";

    private readonly CandidateParser _parser = new();

    private static string Wrap(params string[] items) => "{\"candidates\":[" + string.Join(',', items) + "]}";

    private static string Item(string title, string summary, string rationale, string quote) =>
        $"{{\"title\":\"{title}\",\"summary\":\"{summary}\",\"patentability_rationale\":\"{rationale}\",\"excerpt_quote\":{quote}}}";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    [Fact]
    public void Parse_ValidItem_MapsAllFields()
    {
        var result = _parser.Parse(Wrap(ValidItem), SourceFile);

        var candidate = Assert.Single(result.Candidates!);
        Assert.Equal("Adaptive cache", candidate.Title);
        Assert.Equal("Summary text.", candidate.Summary);
        Assert.Equal("Looks new.", candidate.PatentabilityRationale);
        Assert.Equal(SourceFile, candidate.SourceFile);
        Assert.Equal(new CandidateExcerpt("exact quote", false, null, null), candidate.Excerpt);
    }

    [Fact]
    public void Parse_EmptyCandidates_IsSuccessWithNone()
    {
        var result = _parser.Parse("{\"candidates\":[]}", SourceFile);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Candidates!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"candidates\":{}}")]
    [InlineData("```json\n{\"candidates\":[]}\n```")]
    public void Parse_Malformed_Fails(string text)
    {
        var result = _parser.Parse(text, SourceFile);

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Failure));
    }

    [Theory]
    [InlineData("{\"summary\":\"s\",\"patentability_rationale\":\"r\",\"excerpt_quote\":null}")]
    [InlineData("{\"title\":\"t\",\"patentability_rationale\":\"r\",\"excerpt_quote\":null}")]
    [InlineData("{\"title\":\"t\",\"summary\":\"s\",\"excerpt_quote\":null}")]
    [InlineData("{\"title\":\" \",\"summary\":\"s\",\"patentability_rationale\":\"r\"}")]
    [InlineData("{\"title\":5,\"summary\":\"s\",\"patentability_rationale\":\"r\"}")]
    [InlineData("7")]
    public void Parse_EntryMissingRequiredString_IsSkipped(string entry)
    {
        var result = _parser.Parse(Wrap(entry, ValidItem), SourceFile);

        Assert.Single(result.Candidates!);
    }

    [Fact]
    public void Parse_NonEmptyArrayWithNoSurvivors_Fails()
    {
        var result = _parser.Parse(Wrap("{\"title\":\"t\"}", "7"), SourceFile);

        Assert.False(result.IsSuccess);
        Assert.Equal("output malformed", result.Failure);
    }

    [Fact]
    public void Parse_LongFields_AreClamped()
    {
        var quote = "\"" + new string('q', CandidateParser.MaxQuoteChars + 50) + "\"";
        var item = Item(
            Words(CandidateParser.MaxTitleWords + 3),
            Words(CandidateParser.MaxSummaryWords + 3),
            Words(CandidateParser.MaxRationaleWords + 3),
            quote);

        var candidate = Assert.Single(_parser.Parse(Wrap(item), SourceFile).Candidates!);

        Assert.Equal(CandidateParser.MaxTitleWords, candidate.Title.Split(' ').Length);
        Assert.Equal(CandidateParser.MaxSummaryWords, candidate.Summary.Split(' ').Length);
        Assert.Equal(CandidateParser.MaxRationaleWords, candidate.PatentabilityRationale.Split(' ').Length);
        Assert.Equal(CandidateParser.MaxQuoteChars, candidate.Excerpt!.Quote.Length);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"   \"")]
    [InlineData("\"\"")]
    public void Parse_BlankOrNullQuote_YieldsNullExcerpt(string quote)
    {
        var candidate = Assert.Single(_parser.Parse(Wrap(Item("t", "s", "r", quote)), SourceFile).Candidates!);

        Assert.Null(candidate.Excerpt);
    }

    [Fact]
    public void Parse_MissingQuoteProperty_YieldsNullExcerpt()
    {
        var item = "{\"title\":\"t\",\"summary\":\"s\",\"patentability_rationale\":\"r\"}";

        var candidate = Assert.Single(_parser.Parse(Wrap(item), SourceFile).Candidates!);

        Assert.Null(candidate.Excerpt);
    }
}
