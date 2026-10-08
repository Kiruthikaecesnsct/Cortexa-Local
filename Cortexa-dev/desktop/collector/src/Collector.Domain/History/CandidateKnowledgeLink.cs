using System.Text.Json.Serialization;

namespace Collector.Domain.History;

public sealed record CandidateKnowledgeLink
{
    [JsonPropertyName("knowledge_item")]
    public required LinkedKnowledgeItem KnowledgeItem { get; init; }

    [JsonPropertyName("source")]
    public required CandidateSource Source { get; init; }
}
