using System.Text.Json.Serialization;
using Collector.Domain.Enums;

namespace Collector.Server.Application.Reads;

public sealed record KnowledgeLinkDto
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
