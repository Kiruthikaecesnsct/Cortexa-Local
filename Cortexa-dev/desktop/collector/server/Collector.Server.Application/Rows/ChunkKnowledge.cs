using System.Text.Json.Serialization;
using Collector.Domain.Enums;
using Collector.Domain.Knowledge;

namespace Collector.Server.Application.Rows;

public sealed record ChunkKnowledge
{
    [JsonPropertyName("kind")]
    public required KnowledgeKind Kind { get; init; }

    [JsonPropertyName("unit_kind")]
    public required UnitKind UnitKind { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("details")]
    public string? Details { get; init; }

    [JsonPropertyName("excerpt")]
    public string? Excerpt { get; init; }

    [JsonPropertyName("source")]
    public required KnowledgeSource Source { get; init; }

    [JsonPropertyName("provider")]
    public required CollectorProvider Provider { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("prompt_version")]
    public required string PromptVersion { get; init; }
}
