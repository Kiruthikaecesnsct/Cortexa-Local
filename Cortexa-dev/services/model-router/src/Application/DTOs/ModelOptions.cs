using System.Text.Json.Serialization;

namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record ModelOptions(
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] float? Temperature = null,
    [property: JsonPropertyName("stream")] bool Stream = false,
    [property: JsonPropertyName("force_json_output")] bool ForceJsonOutput = false);
