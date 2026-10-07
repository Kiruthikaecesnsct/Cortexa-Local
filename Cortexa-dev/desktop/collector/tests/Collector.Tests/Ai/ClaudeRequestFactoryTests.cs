using System.Text.Json;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;

namespace Collector.Tests.Ai;

public sealed class ClaudeRequestFactoryTests
{
    private const string SystemText = "system instructions";
    private const string UserText = "user message";
    private const string Schema = "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\"}}}";
    private const double ConfiguredTemperature = 0.25;
    private const int ConfiguredMaxTokens = 1234;

    private static readonly AiRequest Request = new() { SystemText = SystemText, UserText = UserText, OutputSchemaJson = Schema };

    [Fact]
    public async Task Create_Always_SystemBlockIsCachedEphemeral()
    {
        using var body = await AnthropicWire.CaptureBodyAsync(Request, new AiProviderOptions());

        var block = body.RootElement.GetProperty("system")[0];

        Assert.Equal(SystemText, block.GetProperty("text").GetString());
        Assert.Equal("ephemeral", block.GetProperty("cache_control").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Create_Always_SendsModelTokensAndUserMessage()
    {
        var options = new AiProviderOptions { Model = "claude-model-x", MaxOutputTokens = ConfiguredMaxTokens };

        using var body = await AnthropicWire.CaptureBodyAsync(Request, options);

        Assert.Equal("claude-model-x", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(ConfiguredMaxTokens, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(UserText, body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Create_Always_SendsEffortAndJsonSchemaOutputConfig()
    {
        using var body = await AnthropicWire.CaptureBodyAsync(Request, new AiProviderOptions { Effort = "high" });

        var config = body.RootElement.GetProperty("output_config");

        Assert.Equal("high", config.GetProperty("effort").GetString());
        Assert.Equal("json_schema", config.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("object", config.GetProperty("format").GetProperty("schema").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(AiProviderOptions.ThinkingAdaptive, "adaptive")]
    [InlineData(AiProviderOptions.ThinkingDisabled, "disabled")]
    public async Task Create_ThinkingMode_IsSentOnTheWire(string mode, string expectedType)
    {
        using var body = await AnthropicWire.CaptureBodyAsync(Request, new AiProviderOptions { Thinking = mode });

        Assert.Equal(expectedType, body.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Create_BetweenToolsMode_SendsAThinkingConfig()
    {
        using var body = await AnthropicWire.CaptureBodyAsync(Request, new AiProviderOptions { Thinking = AiProviderOptions.ThinkingBetweenTools });

        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("thinking").ValueKind);
    }

    [Fact]
    public async Task Create_EmptyThinkingMode_OmitsThinking()
    {
        using var body = await AnthropicWire.CaptureBodyAsync(Request, new AiProviderOptions { Thinking = string.Empty });

        Assert.False(body.RootElement.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task Create_NullTemperature_OmitsTemperature()
    {
        using var body = await AnthropicWire.CaptureBodyAsync(Request, new AiProviderOptions { Temperature = null });

        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Create_ConfiguredTemperature_IsSent()
    {
        using var body = await AnthropicWire.CaptureBodyAsync(Request, new AiProviderOptions { Temperature = ConfiguredTemperature });

        Assert.Equal(ConfiguredTemperature, body.RootElement.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public void Create_SchemaIsNotAnObject_Throws()
    {
        var request = Request with { OutputSchemaJson = "null" };

        var exception = Assert.ThrowsAny<Exception>(() => AnthropicWire.CreateParams(request, new AiProviderOptions()));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }
}
