using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record SagaDocumentProgressRow
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = RowConstants.SagaDocumentStateQueued;

    [JsonPropertyName("expected_candidate_count")]
    public int ExpectedCandidateCount { get; init; }

    [JsonPropertyName("completed_candidate_ids")]
    public IReadOnlyList<string> CompletedCandidateIds { get; init; } = [];

    [JsonPropertyName("failed_candidate_ids")]
    public IReadOnlyList<string> FailedCandidateIds { get; init; } = [];

    [JsonPropertyName("expected_extraction_units")]
    public int ExpectedExtractionUnits { get; init; }

    [JsonPropertyName("received_extraction_unit_indices")]
    public IReadOnlyList<int> ReceivedExtractionUnitIndices { get; init; } = [];

    [JsonPropertyName("seeded_candidate_ids")]
    public IReadOnlyList<string> SeededCandidateIds { get; init; } = [];

    [JsonPropertyName("completed_seeded_candidate_ids")]
    public IReadOnlyList<string> CompletedSeededCandidateIds { get; init; } = [];

    [JsonPropertyName("failed_seeded_candidate_ids")]
    public IReadOnlyList<string> FailedSeededCandidateIds { get; init; } = [];
}
