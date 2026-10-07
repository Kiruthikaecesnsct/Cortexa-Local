using Collector.Server.Application.Building;

namespace Collector.Server.Tests.Building;

public class TextAndTokenTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    public void Estimate_RoundsUpByFourCharsPerToken(int length, int expected)
    {
        var text = new string('x', length);

        Assert.Equal(expected, TokenEstimator.Estimate(text));
    }

    [Fact]
    public void Render_IncludesKindTitleSummaryDetailsAndExcerpt()
    {
        var text = ChunkTextRenderer.Render(TestData.PaperItem());

        Assert.Equal(
            "Structured knowledge summary from page 3\nlogic: Spectral Method\nSummary text\nDetail text\nExcerpt: Key excerpt",
            text);
    }

    [Fact]
    public void Render_OmitsMissingDetailsAndExcerpt()
    {
        var text = ChunkTextRenderer.Render(TestData.PlainItem("Parse"));

        Assert.Equal("method: Parse\nSum", text);
    }

    [Fact]
    public void Render_UsesSnakeCaseKindName()
    {
        var item = TestData.PlainItem("Schema") with { Kind = Collector.Domain.Enums.KnowledgeKind.DataModel };

        Assert.StartsWith("data_model: Schema", ChunkTextRenderer.Render(item), StringComparison.Ordinal);
    }
}
