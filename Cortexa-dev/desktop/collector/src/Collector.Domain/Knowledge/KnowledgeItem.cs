using System.Text.Json.Serialization;
using Collector.Domain.Enums;

namespace Collector.Domain.Knowledge;

public sealed record KnowledgeItem
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
    public KnowledgeSource Source { get; init; } = new();
}
