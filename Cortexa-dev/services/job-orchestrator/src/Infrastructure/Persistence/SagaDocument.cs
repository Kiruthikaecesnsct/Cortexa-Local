using System.Text.Json.Serialization;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class SagaDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("documents")]
    public List<DocumentProgressRecord> Documents { get; set; } = [];

    [JsonPropertyName("wants_harvesting")]
    public bool WantsHarvesting { get; set; }

    [JsonPropertyName("wants_seeding")]
    public bool WantsSeeding { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    [JsonPropertyName("failed_at")]
    public DateTimeOffset? FailedAt { get; set; }

    [JsonPropertyName("cancelled_at")]
    public DateTimeOffset? CancelledAt { get; set; }

    [JsonPropertyName("active_document_ids")]
    public List<string> ActiveDocumentIds { get; set; } = [];

    [JsonPropertyName("queued_document_ids")]
    public List<string> QueuedDocumentIds { get; set; } = [];

    [JsonPropertyName("active_count")]
    public int ActiveCount { get; set; }

    [JsonPropertyName("completed_count")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("batch_name")]
    public string? BatchName { get; set; }

    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    [JsonPropertyName("ai_model")]
    public string? AiModel { get; set; }

    [JsonPropertyName("seed_corpus_domain")]
    public string? SeedCorpusDomain { get; set; }

    [JsonPropertyName("git_repo_url")]
    public string? GitRepoUrl { get; set; }

    [JsonPropertyName("git_pat_secret_name")]
    public string? GitPatSecretName { get; set; }

    [JsonPropertyName("git_branch")]
    public string? GitBranch { get; set; }

    [JsonPropertyName("git_host")]
    public string? GitHost { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("total_document_count")]
    public int TotalDocumentCount { get; set; }

    [JsonPropertyName("org_id")]
    public string? OrgId { get; set; }

    [JsonPropertyName("owner_user_id")]
    public string? OwnerUserId { get; set; }

    [JsonPropertyName("extraction_model")]
    public string? ExtractionModel { get; set; }

    [JsonPropertyName("primary_evidence_model")]
    public string? PrimaryEvidenceModel { get; set; }

    [JsonPropertyName("scoring_model")]
    public string? ScoringModel { get; set; }

    [JsonPropertyName("seeding_model")]
    public string? SeedingModel { get; set; }

    [JsonPropertyName("seeding_mode")]
    public string? SeedingMode { get; set; }

    [JsonPropertyName("evidence_completed_count")]
    public int EvidenceCompletedCount { get; set; }

    [JsonPropertyName("evidence_source_live_counts")]
    public Dictionary<string, int> EvidenceSourceLiveCounts { get; set; } = [];

    [JsonPropertyName("counted_evidence_candidate_ids")]
    public List<string> CountedEvidenceCandidateIds { get; set; } = [];

    [JsonPropertyName("expected_asset_embedding_units")]
    public int ExpectedAssetEmbeddingUnits { get; set; }

    [JsonPropertyName("completed_asset_embedding_units")]
    public int CompletedAssetEmbeddingUnits { get; set; }

    [JsonPropertyName("recorded_asset_embedding_unit_keys")]
    public List<string> RecordedAssetEmbeddingUnitKeys { get; set; } = [];

    [JsonPropertyName("recorded_digest_requested_document_ids")]
    public List<string> RecordedDigestRequestedDocumentIds { get; set; } = [];

    [JsonPropertyName("recorded_digest_completed_document_ids")]
    public List<string> RecordedDigestCompletedDocumentIds { get; set; } = [];

    [JsonPropertyName("recorded_landscape_requested_document_ids")]
    public List<string> RecordedLandscapeRequestedDocumentIds { get; set; } = [];

    [JsonPropertyName("recorded_landscape_completed_document_ids")]
    public List<string> RecordedLandscapeCompletedDocumentIds { get; set; } = [];
}

public sealed class DocumentProgressRecord
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    [JsonPropertyName("expected_candidate_count")]
    public int ExpectedCandidateCount { get; set; }

    [JsonPropertyName("completed_candidate_ids")]
    public List<string> CompletedCandidateIds { get; set; } = [];

    [JsonPropertyName("failed_candidate_ids")]
    public List<string> FailedCandidateIds { get; set; } = [];

    [JsonPropertyName("expected_extraction_units")]
    public int ExpectedExtractionUnits { get; set; }

    [JsonPropertyName("received_extraction_unit_indices")]
    public List<int> ReceivedExtractionUnitIndices { get; set; } = [];

    [JsonPropertyName("seeded_candidate_ids")]
    public List<string> SeededCandidateIds { get; set; } = [];

    [JsonPropertyName("completed_seeded_candidate_ids")]
    public List<string> CompletedSeededCandidateIds { get; set; } = [];

    [JsonPropertyName("failed_seeded_candidate_ids")]
    public List<string> FailedSeededCandidateIds { get; set; } = [];
}
