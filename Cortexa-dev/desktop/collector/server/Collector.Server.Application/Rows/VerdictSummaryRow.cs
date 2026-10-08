using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record VerdictSummaryRow
{
    [JsonPropertyName("candidate_id")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("composite_score")]
    public double CompositeScore { get; init; }

    [JsonPropertyName("patentability")]
    public double? Patentability { get; init; }
}
