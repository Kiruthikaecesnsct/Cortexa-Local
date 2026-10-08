using System.Text.Json.Serialization;
using Collector.Domain.Enums;

namespace Collector.Domain.History;

public sealed record LinkedKnowledgeItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public required KnowledgeKind Kind { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
