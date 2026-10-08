using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record SeedingOpportunityRow
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("candidate_id")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("weighted_score")]
    public double? WeightedScore { get; init; }

    [JsonPropertyName("axes")]
    public IReadOnlyDictionary<string, AxisScoreRow> Axes { get; init; } = new Dictionary<string, AxisScoreRow>();

    [JsonPropertyName("grounded_in")]
    public SeedingGroundingRow? GroundedIn { get; init; }
}
