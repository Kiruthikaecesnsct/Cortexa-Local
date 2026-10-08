using System.Text.Json.Serialization;

namespace Collector.Domain.History;

public sealed record BatchSummary
{
    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("batch_name")]
    public required string BatchName { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("stage")]
    [JsonConverter(typeof(JsonStringEnumConverter<BatchStage>))]
    public required BatchStage Stage { get; init; }

    [JsonPropertyName("extraction_completed_count")]
    public required int ExtractionCompletedCount { get; init; }

    [JsonPropertyName("extraction_total_count")]
    public required int ExtractionTotalCount { get; init; }

    [JsonPropertyName("evidence_completed_count")]
    public required int EvidenceCompletedCount { get; init; }

    [JsonPropertyName("embedding_completed_count")]
    public required int EmbeddingCompletedCount { get; init; }

    [JsonPropertyName("embedding_total_count")]
    public required int EmbeddingTotalCount { get; init; }

    [JsonPropertyName("harvesting_completed_count")]
    public required int HarvestingCompletedCount { get; init; }

    [JsonPropertyName("harvesting_total_count")]
    public required int HarvestingTotalCount { get; init; }

    [JsonPropertyName("seeding_completed_count")]
    public required int SeedingCompletedCount { get; init; }

    [JsonPropertyName("seeding_total_count")]
    public required int SeedingTotalCount { get; init; }
}
