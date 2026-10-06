using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record SagaRow : IPipelineRow
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = RowConstants.SagaStateInProgress;

    [JsonPropertyName("documents")]
    public IReadOnlyList<SagaDocumentProgressRow> Documents { get; init; } = [];

    [JsonPropertyName("wants_harvesting")]
    public required bool WantsHarvesting { get; init; }

    [JsonPropertyName("wants_seeding")]
    public required bool WantsSeeding { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; } = RowConstants.InitialSagaVersion;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = RowConstants.SagaSchemaVersion;

    [JsonPropertyName("active_document_ids")]
    public IReadOnlyList<string> ActiveDocumentIds { get; init; } = [];

    [JsonPropertyName("queued_document_ids")]
    public IReadOnlyList<string> QueuedDocumentIds { get; init; } = [];

    [JsonPropertyName("active_count")]
    public int ActiveCount { get; init; }

    [JsonPropertyName("completed_count")]
    public int CompletedCount { get; init; }

    [JsonPropertyName("batch_name")]
    public required string BatchName { get; init; }

    [JsonPropertyName("engine")]
    public required string Engine { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("total_document_count")]
    public required int TotalDocumentCount { get; init; }

    [JsonPropertyName("org_id")]
    public required string OrgId { get; init; }

    [JsonPropertyName("owner_user_id")]
    public required string OwnerUserId { get; init; }

    [JsonPropertyName("extraction_model")]
    public required string ExtractionModel { get; init; }

    [JsonPropertyName("primary_evidence_model")]
    public required string PrimaryEvidenceModel { get; init; }

    [JsonPropertyName("scoring_model")]
    public required string ScoringModel { get; init; }

    [JsonPropertyName("seeding_model")]
    public required string SeedingModel { get; init; }

    [JsonPropertyName("seeding_mode")]
    public required string SeedingMode { get; init; }

    [JsonPropertyName("evidence_completed_count")]
    public int EvidenceCompletedCount { get; init; }

    [JsonPropertyName("evidence_source_live_counts")]
    public IReadOnlyDictionary<string, int> EvidenceSourceLiveCounts { get; init; } = new Dictionary<string, int>();

    [JsonPropertyName("counted_evidence_candidate_ids")]
    public IReadOnlyList<string> CountedEvidenceCandidateIds { get; init; } = [];

    [JsonPropertyName("expected_asset_embedding_units")]
    public int ExpectedAssetEmbeddingUnits { get; init; }

    [JsonPropertyName("completed_asset_embedding_units")]
    public int CompletedAssetEmbeddingUnits { get; init; }

    [JsonPropertyName("recorded_asset_embedding_unit_keys")]
    public IReadOnlyList<string> RecordedAssetEmbeddingUnitKeys { get; init; } = [];

    [JsonPropertyName("recorded_digest_requested_document_ids")]
    public IReadOnlyList<string> RecordedDigestRequestedDocumentIds { get; init; } = [];

    [JsonPropertyName("recorded_digest_completed_document_ids")]
    public IReadOnlyList<string> RecordedDigestCompletedDocumentIds { get; init; } = [];

    [JsonPropertyName("recorded_landscape_requested_document_ids")]
    public IReadOnlyList<string> RecordedLandscapeRequestedDocumentIds { get; init; } = [];

    [JsonPropertyName("recorded_landscape_completed_document_ids")]
    public IReadOnlyList<string> RecordedLandscapeCompletedDocumentIds { get; init; } = [];
}
