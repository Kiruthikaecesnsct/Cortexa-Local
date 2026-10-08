using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record HarvestingReportCandidateRow : IPipelineRow
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("engine")]
    public required string Engine { get; init; }

    [JsonPropertyName("doc_type")]
    public required string DocType { get; init; }

    [JsonPropertyName("candidate_id")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("maturity")]
    public required string Maturity { get; init; }

    [JsonPropertyName("weighted_score")]
    public double? WeightedScore { get; init; }

    [JsonPropertyName("axes")]
    public IReadOnlyDictionary<string, AxisScoreRow> Axes { get; init; } = new Dictionary<string, AxisScoreRow>();

    [JsonPropertyName("provenance_links")]
    public IReadOnlyList<ProvenanceLinkRow> ProvenanceLinks { get; init; } = [];
}
