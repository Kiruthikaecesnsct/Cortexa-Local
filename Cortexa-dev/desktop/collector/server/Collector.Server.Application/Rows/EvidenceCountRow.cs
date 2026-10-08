using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record EvidenceCountRow
{
    [JsonPropertyName("candidate_id")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("hit_count")]
    public int HitCount { get; init; }
}
