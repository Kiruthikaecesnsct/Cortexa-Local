using System.Text.Json.Serialization;

namespace Cortexa.ModelRouter.Infrastructure.Providers.Gemini.Mapping;

internal sealed record GeminiPart(
    [property: JsonPropertyName("text")] string Text);

internal sealed record GeminiContent(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

internal sealed record GeminiGenerationConfig(
    [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens,
    [property: JsonPropertyName("temperature")] float? Temperature,
    [property: JsonPropertyName("responseMimeType")] string? ResponseMimeType);

internal sealed record GeminiRequest(
    [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
    [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig? GenerationConfig);

internal sealed record GeminiUsageMetadata(
    [property: JsonPropertyName("promptTokenCount")] int PromptTokenCount,
    [property: JsonPropertyName("candidatesTokenCount")] int CandidatesTokenCount,
    [property: JsonPropertyName("totalTokenCount")] int TotalTokenCount);

internal sealed record GeminiCandidate(
    [property: JsonPropertyName("content")] GeminiContent? Content,
    [property: JsonPropertyName("finishReason")] string? FinishReason);

internal sealed record GeminiResponse(
    [property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates,
    [property: JsonPropertyName("usageMetadata")] GeminiUsageMetadata? UsageMetadata,
    [property: JsonPropertyName("modelVersion")] string? ModelVersion);

internal sealed record GeminiErrorDetail(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("status")] string? Status);

internal sealed record GeminiErrorResponse(
    [property: JsonPropertyName("error")] GeminiErrorDetail? Error);
