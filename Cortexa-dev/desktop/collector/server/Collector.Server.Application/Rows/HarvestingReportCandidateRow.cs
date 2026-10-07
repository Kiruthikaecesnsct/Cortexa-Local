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

    [JsonPropertyName("provenance_links")]
    public IReadOnlyList<ProvenanceLinkRow> ProvenanceLinks { get; init; } = [];
}
