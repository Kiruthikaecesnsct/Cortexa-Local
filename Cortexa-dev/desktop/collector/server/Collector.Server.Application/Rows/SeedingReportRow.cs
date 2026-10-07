using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record SeedingReportRow : IPipelineRow
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("engine")]
    public required string Engine { get; init; }

    [JsonPropertyName("opportunities")]
    public IReadOnlyList<SeedingOpportunityRow> Opportunities { get; init; } = [];
}
