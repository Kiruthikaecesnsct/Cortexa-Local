using System.Text.Json.Serialization;

namespace Cortexa.ModelRouter.Infrastructure.Providers.Foundry.Mapping;

internal sealed record FoundryMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record FoundryResponseFormat(
    [property: JsonPropertyName("type")] string Type);

internal sealed record FoundryRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<FoundryMessage> Messages,
    // Newer Azure OpenAI models (gpt-5.x) require max_completion_tokens; max_tokens is rejected with 400.
    [property: JsonPropertyName("max_completion_tokens")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaxCompletionTokens,
    [property: JsonPropertyName("max_tokens")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaxTokens,
    [property: JsonPropertyName("temperature")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    float? Temperature,
    [property: JsonPropertyName("stream")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Stream,
    [property: JsonPropertyName("reasoning_effort")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReasoningEffort = null,
    [property: JsonPropertyName("response_format")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FoundryResponseFormat? ResponseFormat = null);

internal sealed record FoundryUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

internal sealed record FoundryResponseMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null);

internal sealed record FoundryChoice(
    [property: JsonPropertyName("message")] FoundryResponseMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

internal sealed record FoundryResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<FoundryChoice> Choices,
    [property: JsonPropertyName("usage")] FoundryUsage Usage);

internal sealed record FoundryStreamDelta(
    [property: JsonPropertyName("content")] string? Content);

internal sealed record FoundryStreamChoice(
    [property: JsonPropertyName("delta")] FoundryStreamDelta? Delta,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

internal sealed record FoundryStreamChunk(
    [property: JsonPropertyName("choices")] IReadOnlyList<FoundryStreamChoice> Choices,
    [property: JsonPropertyName("usage")] FoundryUsage? Usage);

internal sealed record FoundryContentFilterDetail(
    [property: JsonPropertyName("filtered")] bool Filtered,
    [property: JsonPropertyName("detected")] bool Detected);

internal sealed record FoundryInnerError(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("content_filter_result")] Dictionary<string, FoundryContentFilterDetail>? ContentFilterResult);

internal sealed record FoundryErrorResponse(
    [property: JsonPropertyName("error")] FoundryErrorBody? Error);

internal sealed record FoundryErrorBody(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("param")] string? Param,
    [property: JsonPropertyName("innererror")] FoundryInnerError? InnerError);
