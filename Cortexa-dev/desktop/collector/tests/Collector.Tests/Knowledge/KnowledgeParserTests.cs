using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class KnowledgeParserTests
{
    private readonly KnowledgeParser _parser = new();

    private static string OneItem() => TestData.Response(TestData.ItemJson("Title"));

    [Fact]
    public void Parse_PlainJson_ReturnsItems()
    {
        var result = _parser.Parse(OneItem());

        Assert.True(result.Succeeded);
        Assert.Equal("Title", Assert.Single(result.Items).Title);
    }

    [Fact]
    public void Parse_FencedJson_ReturnsItems()
    {
        var result = _parser.Parse($"```json\n{OneItem()}\n```");

        Assert.True(result.Succeeded);
        Assert.Single(result.Items);
    }

    [Fact]
    public void Parse_ProseAroundOutermostObject_ReturnsItems()
    {
        var result = _parser.Parse($"Here is the result: {OneItem()} Hope that helps.");

        Assert.True(result.Succeeded);
        Assert.Single(result.Items);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"items\": ")]
    [InlineData("[]")]
    [InlineData("{\"other\": []}")]
    [InlineData("{\"items\": {}}")]
    [InlineData("{\"items\": \"text\"}")]
    public void Parse_UnusableResponse_IsNotSucceeded(string text)
    {
        var result = _parser.Parse(text);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Parse_EmptyItemsArray_SucceedsWithNoItems()
    {
        var result = _parser.Parse("{\"items\": []}");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.DroppedItems);
    }

    [Theory]
    [InlineData("logic", KnowledgeKind.Logic)]
    [InlineData("algorithm", KnowledgeKind.Algorithm)]
    [InlineData("method", KnowledgeKind.Method)]
    [InlineData("data_model", KnowledgeKind.DataModel)]
    [InlineData("interface", KnowledgeKind.Interface)]
    [InlineData("workflow", KnowledgeKind.Workflow)]
    [InlineData("key_content", KnowledgeKind.KeyContent)]
    public void Parse_WireKind_MapsToKnowledgeKind(string wire, KnowledgeKind expected)
    {
        var result = _parser.Parse(TestData.Response(TestData.ItemJson("Title", kind: wire)));

        Assert.Equal(expected, Assert.Single(result.Items).Kind);
    }

    [Fact]
    public void Parse_LayerAndUnknownKinds_AreDroppedAndCounted()
    {
        var response = TestData.Response(
            TestData.ItemJson("Layer", kind: "layer"),
            TestData.ItemJson("Unknown", kind: "poem"),
            TestData.ItemJson("Kept"));

        var result = _parser.Parse(response);

        Assert.Equal("Kept", Assert.Single(result.Items).Title);
        Assert.Equal(2, result.DroppedItems);
    }

    [Fact]
    public void Parse_BlankTitleOrSummary_AreDropped()
    {
        var response = TestData.Response(
            TestData.ItemJson("  "),
            TestData.ItemJson("Has title", summary: " "),
            TestData.ItemJson("Fine"));

        var result = _parser.Parse(response);

        Assert.Equal("Fine", Assert.Single(result.Items).Title);
        Assert.Equal(2, result.DroppedItems);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankAnchorQuote_BecomesNull(string? quote)
    {
        var result = _parser.Parse(TestData.Response(TestData.ItemJson("Title", anchorQuote: quote)));

        Assert.Null(Assert.Single(result.Items).AnchorQuote);
    }

    [Fact]
    public void Parse_AnchorQuote_IsKeptTrimmed()
    {
        var result = _parser.Parse(TestData.Response(TestData.ItemJson("Title", anchorQuote: "  quoted text ")));

        Assert.Equal("quoted text", Assert.Single(result.Items).AnchorQuote);
    }
}
