using System.Text.Json;
using System.Text.Json.Serialization;

namespace Collector.Infrastructure.Ai;

internal sealed record GroqMessageWire(string Role, string Content);

internal sealed record GroqJsonSchemaWire(string Name, bool Strict, JsonElement Schema);

internal sealed record GroqResponseFormatWire(string Type, GroqJsonSchemaWire JsonSchema);

internal sealed record GroqChatRequestWire(
    string Model,
    List<GroqMessageWire> Messages,
    int MaxCompletionTokens,
    double? Temperature,
    GroqResponseFormatWire ResponseFormat);

internal sealed record GroqChoiceMessageWire(string? Content);

internal sealed record GroqChoiceWire(GroqChoiceMessageWire? Message, string? FinishReason);

internal sealed record GroqChatResponseWire(List<GroqChoiceWire>? Choices, string? Model);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GroqChatRequestWire))]
[JsonSerializable(typeof(GroqChatResponseWire))]
internal sealed partial class GroqJson : JsonSerializerContext;
