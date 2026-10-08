using System.Text.Json.Serialization;

namespace Collector.Domain.History;

public sealed record BatchCandidate
{
    [JsonPropertyName("candidate_id")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("engine")]
    public required string Engine { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("evidence_count")]
    public required int EvidenceCount { get; init; }

    [JsonPropertyName("score")]
    public double? Score { get; init; }

    [JsonPropertyName("patentability")]
    public int? Patentability { get; init; }

    [JsonPropertyName("knowledge_links")]
    public required IReadOnlyList<CandidateKnowledgeLink> KnowledgeLinks { get; init; }
}
