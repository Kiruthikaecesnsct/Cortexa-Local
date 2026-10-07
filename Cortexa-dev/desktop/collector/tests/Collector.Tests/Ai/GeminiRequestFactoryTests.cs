using System.Text.Json;
using System.Text.Json.Nodes;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class GeminiRequestFactoryTests
{
    private const int ConfiguredMaxTokens = 1234;
    private const double ConfiguredTemperature = 0.4;
    private const string PropertyOrderingKey = "propertyOrdering";

    [Fact]
    public async Task Create_Always_SendsSystemInstructionAndUserContent()
    {
        var (root, _) = await GeminiWire.CaptureAsync(GeminiWire.Request, new GeminiProviderOptions { MaxRetries = 0 });

        Assert.Equal("system instructions", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var content = root.GetProperty("contents")[0];
        Assert.Equal("user", content.GetProperty("role").GetString());
        Assert.Equal("user message", content.GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Create_Always_SendsModelTokensAndJsonMode()
    {
        var options = new GeminiProviderOptions { Model = "gemini-model-x", MaxOutputTokens = ConfiguredMaxTokens, MaxRetries = 0 };

        var (root, request) = await GeminiWire.CaptureAsync(GeminiWire.Request, options);

        var config = root.GetProperty("generationConfig");
        Assert.Contains("models/gemini-model-x:generateContent", request.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(ConfiguredMaxTokens, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
    }

    [Fact]
    public async Task Create_SharedKnowledgeSchema_IsSentUnchangedAsJsonSchema()
    {
        var schema = KnowledgePipeline.Prompt.SchemaJson;
        var request = GeminiWire.Request with { OutputSchemaJson = schema };

        var (root, _) = await GeminiWire.CaptureAsync(request, new GeminiProviderOptions { MaxRetries = 0 });

        var config = root.GetProperty("generationConfig");
        Assert.False(config.TryGetProperty("responseSchema", out _));
        var sent = WithoutPropertyOrdering(JsonNode.Parse(config.GetProperty("responseJsonSchema").GetRawText()));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(schema), sent));
    }

    [Theory]
    [InlineData(GeminiProviderOptions.ThinkingLow, "LOW")]
    [InlineData(GeminiProviderOptions.ThinkingMedium, "MEDIUM")]
    [InlineData(GeminiProviderOptions.ThinkingHigh, "HIGH")]
    public async Task Create_ThinkingLevel_IsSentOnTheWire(string level, string expected)
    {
        var (root, _) = await GeminiWire.CaptureAsync(GeminiWire.Request, new GeminiProviderOptions { Thinking = level, MaxRetries = 0 });

        var thinking = root.GetProperty("generationConfig").GetProperty("thinkingConfig");
        Assert.Equal(expected, thinking.GetProperty("thinkingLevel").GetString());
    }

    [Fact]
    public async Task Create_EmptyThinkingAndNoTemperature_OmitsBoth()
    {
        var (root, _) = await GeminiWire.CaptureAsync(GeminiWire.Request, new GeminiProviderOptions { Thinking = string.Empty, MaxRetries = 0 });

        var config = root.GetProperty("generationConfig");
        Assert.False(config.TryGetProperty("thinkingConfig", out _));
        Assert.False(config.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Create_TemperatureSet_IsSent()
    {
        var (root, _) = await GeminiWire.CaptureAsync(GeminiWire.Request, new GeminiProviderOptions { Temperature = ConfiguredTemperature, MaxRetries = 0 });

        Assert.Equal(ConfiguredTemperature, root.GetProperty("generationConfig").GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task Create_Always_SendsKeyOnlyInHeader()
    {
        var (root, request) = await GeminiWire.CaptureAsync(GeminiWire.Request, new GeminiProviderOptions { MaxRetries = 0 });

        Assert.Equal(GeminiWire.ApiKey, request.Headers["x-goog-api-key"]);
        Assert.DoesNotContain(GeminiWire.ApiKey, request.Uri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(GeminiWire.ApiKey, request.Body, StringComparison.Ordinal);
    }

    private static JsonNode? WithoutPropertyOrdering(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(PropertyOrderingKey);
            foreach (var child in obj.Select(pair => pair.Value).ToList())
            {
                WithoutPropertyOrdering(child);
            }
        }

        foreach (var item in (node as JsonArray)?.ToList() ?? [])
        {
            WithoutPropertyOrdering(item);
        }

        return node;
    }

    [Fact]
    public void Create_SchemaNotAnObject_Throws()
    {
        var request = new AiRequest { SystemText = "s", UserText = "u", OutputSchemaJson = "[]" };

        Assert.Throws<ArgumentException>(() => Collector.Infrastructure.Ai.GeminiRequestFactory.Create(request, new GeminiProviderOptions()));
    }
}
