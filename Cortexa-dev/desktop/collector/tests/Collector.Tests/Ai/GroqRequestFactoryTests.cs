using System.Text.Json.Nodes;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class GroqRequestFactoryTests
{
    private const int ConfiguredMaxTokens = 1234;
    private const double ConfiguredTemperature = 0.4;

    [Fact]
    public async Task Create_Always_SendsSystemAndUserMessages()
    {
        var (root, _) = await GroqWire.CaptureAsync(GroqWire.Request, new GroqProviderOptions { MaxRetries = 0 });

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("system instructions", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("user message", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Create_Always_SendsModelAndTokenLimit()
    {
        var options = new GroqProviderOptions { Model = "groq-model-x", MaxOutputTokens = ConfiguredMaxTokens, MaxRetries = 0 };

        var (root, request) = await GroqWire.CaptureAsync(GroqWire.Request, options);

        Assert.Equal("groq-model-x", root.GetProperty("model").GetString());
        Assert.Equal(ConfiguredMaxTokens, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.EndsWith("/chat/completions", request.Uri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_RequestOverridesModel_IsUsedInsteadOfSettings()
    {
        var request = GroqWire.Request with { Model = "groq-override" };

        var (root, _) = await GroqWire.CaptureAsync(request, new GroqProviderOptions { Model = "groq-default", MaxRetries = 0 });

        Assert.Equal("groq-override", root.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Create_Always_SendsJsonSchemaResponseFormat()
    {
        var (root, _) = await GroqWire.CaptureAsync(GroqWire.Request, new GroqProviderOptions { MaxRetries = 0 });

        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var schema = format.GetProperty("json_schema");
        Assert.False(schema.GetProperty("strict").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(schema.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Create_SharedKnowledgeSchema_IsSentUnchanged()
    {
        var schema = KnowledgePipeline.Prompt.SchemaJson;
        var request = GroqWire.Request with { OutputSchemaJson = schema };

        var (root, _) = await GroqWire.CaptureAsync(request, new GroqProviderOptions { MaxRetries = 0 });

        var sent = root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(schema), JsonNode.Parse(sent.GetRawText())));
    }

    [Fact]
    public async Task Create_TemperatureSet_IsSent()
    {
        var (root, _) = await GroqWire.CaptureAsync(GroqWire.Request, new GroqProviderOptions { Temperature = ConfiguredTemperature, MaxRetries = 0 });

        Assert.Equal(ConfiguredTemperature, root.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task Create_Always_SendsKeyOnlyInAuthorizationHeader()
    {
        var (_, request) = await GroqWire.CaptureAsync(GroqWire.Request, new GroqProviderOptions { MaxRetries = 0 });

        Assert.Equal($"Bearer {GroqWire.ApiKey}", request.Headers["Authorization"]);
        Assert.DoesNotContain(GroqWire.ApiKey, request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_SchemaNotAnObject_Throws()
    {
        var request = new AiRequest { SystemText = "s", UserText = "u", OutputSchemaJson = "[]" };

        Assert.Throws<ArgumentException>(() => GroqRequestFactory.Create(request, new GroqProviderOptions()));
    }
}
