using System.Text.Json.Serialization;

namespace Collector.Server.Application.Reads;

public sealed record BatchResultDto
{
    [JsonPropertyName("engine")]
    public required string Engine { get; init; }

    [JsonPropertyName("knowledge_item")]
    public required KnowledgeLinkDto KnowledgeItem { get; init; }

    [JsonPropertyName("source")]
    public required SourceDto Source { get; init; }
}
