using System.Text.Json.Serialization;
using Collector.Domain.Enums;

namespace Collector.Domain.Upload;

public sealed record CollectorInfo
{
    [JsonPropertyName("app_version")]
    public required string AppVersion { get; init; }

    [JsonPropertyName("provider")]
    public required CollectorProvider Provider { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("prompt_version")]
    public required string PromptVersion { get; init; }
}
