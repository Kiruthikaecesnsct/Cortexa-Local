using System.Text.Json.Serialization;

namespace Cortexa.ModelRouter.Infrastructure.Providers.Anthropic.Mapping;

internal sealed record AnthropicMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record AnthropicRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages,
    [property: JsonPropertyName("stream")] bool? Stream);

internal sealed record AnthropicUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens);

internal sealed record AnthropicContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text);

internal sealed record AnthropicResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("content")] IReadOnlyList<AnthropicContentBlock> Content,
    [property: JsonPropertyName("usage")] AnthropicUsage Usage);

internal sealed record AnthropicStreamDelta(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("output_tokens")] int? OutputTokens);

internal sealed record AnthropicStreamUsage(
    [property: JsonPropertyName("output_tokens")] int OutputTokens);

internal sealed record AnthropicStreamMessage(
    [property: JsonPropertyName("usage")] AnthropicUsage? Usage);

internal sealed record AnthropicStreamEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("delta")] AnthropicStreamDelta? Delta,
    [property: JsonPropertyName("usage")] AnthropicStreamUsage? Usage,
    [property: JsonPropertyName("index")] int? Index,
    [property: JsonPropertyName("message")] AnthropicStreamMessage? Message);
