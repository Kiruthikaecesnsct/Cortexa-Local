using Collector.Application.Knowledge;
using Collector.Domain.Enums;

namespace Collector.Tests.Knowledge;

public sealed class LayerKnowledgeParserTests
{
    private readonly LayerKnowledgeParser _parser = new();

    private static string LayerItemJson(string title, string summary = "A module summary.") =>
        $"{{\"kind\":\"layer\",\"title\":\"{title}\",\"summary\":\"{summary}\",\"details\":\"\"}}";

    [Fact]
    public void Parse_LayerKind_IsKept()
    {
        var result = _parser.Parse($"{{\"items\":[{LayerItemJson("Domain model")}]}}");

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Items);
        Assert.Equal(KnowledgeKind.Layer, item.Kind);
        Assert.Equal("Domain model", item.Title);
    }

    [Theory]
    [InlineData("logic")]
    [InlineData("method")]
    [InlineData("poem")]
    public void Parse_NonLayerKind_IsDropped(string wireKind)
    {
        var response = $"{{\"items\":[{{\"kind\":\"{wireKind}\",\"title\":\"X\",\"summary\":\"Y\",\"details\":\"\"}}]}}";

        var result = _parser.Parse(response);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.DroppedItems);
    }

    [Fact]
    public void Parse_MixedKinds_KeepsOnlyLayer()
    {
        var response = $"{{\"items\":[{LayerItemJson("Kept")},{{\"kind\":\"method\",\"title\":\"X\",\"summary\":\"Y\",\"details\":\"\"}}]}}";

        var result = _parser.Parse(response);

        Assert.Equal("Kept", Assert.Single(result.Items).Title);
        Assert.Equal(1, result.DroppedItems);
    }

    [Fact]
    public void Parse_EmptyItemsArray_SucceedsWithNoItems()
    {
        var result = _parser.Parse("{\"items\": []}");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Parse_FencedJson_ReturnsItems()
    {
        var result = _parser.Parse($"```json\n{{\"items\":[{LayerItemJson("Fenced")}]}}\n```");

        Assert.True(result.Succeeded);
        Assert.Single(result.Items);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"other\": []}")]
    public void Parse_UnusableResponse_IsNotSucceeded(string text)
    {
        var result = _parser.Parse(text);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Items);
    }
}
