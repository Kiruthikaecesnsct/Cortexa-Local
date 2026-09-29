namespace Cortexa.JobOrchestrator.Application.Contracts;

public static class SagaEventType
{
    public const string BatchCreated = "batch.created";
    public const string IngestionRequested = "ingestion.requested";
    public const string IngestionCompleted = "ingestion.completed";
    public const string ExtractionRequested = "extraction.requested";
    public const string ExtractionCompleted = "extraction.completed";
    public const string ExtractionFailed = "extraction.failed";
    public const string EvidenceRequested = "evidence.requested";
    public const string EvidenceCompleted = "evidence.completed";
    public const string EvidenceFailed = "evidence.failed";
    public const string ScoringRequested = "scoring.requested";
    public const string ScoringCompleted = "scoring.completed";
    public const string ScoringFailed = "scoring.failed";
    public const string HarvestingRequested = "harvesting.requested";
    public const string HarvestingFailed = "harvesting.failed";
    public const string SeedingRequested = "seeding.requested";
    public const string SeedingFailed = "seeding.failed";
    public const string AssetEmbeddingRequested = "asset-embedding.requested";
    public const string AssetEmbeddingCompleted = "asset-embedding.completed";
    public const string DigestRequested = "digest.requested";
    public const string DigestCompleted = "digest.completed";
    public const string LandscapeRequested = "landscape.requested";
    public const string LandscapeCompleted = "landscape.completed";
    public const string IdeationCompleted = "ideation.completed";
    public const string SeedingReportRequested = "seeding-report.requested";
    public const string EngineCompleted = "engine.completed";
}

public static class PayloadKeys
{
    public const string CandidateId = "candidate_id";
    public const string CandidateIds = "candidate_ids";
    public const string CandidateCount = "candidate_count";
    public const string EvidenceBundleId = "evidence_bundle_id";
    public const string BriefId = "brief_id";
    public const string JobId = "job_id";
    public const string DocumentId = "document_id";
    public const string TriggerType = "trigger_type";
    public const string SourcesUsed = "sources_used";
    public const string AiModel = "ai_model";
    public const string ChunkCount = "chunk_count";
    public const string ChunkStart = "chunk_start";
    public const string ChunkEnd = "chunk_end";
    public const string UnitIndex = "unit_index";
    public const string UnitCount = "unit_count";
    public const string ReportTrigger = "report_trigger";
    public const string SeedingMode = "seeding_mode";
}

public static class ReportTriggerValues
{
    public const string Validation = "validation";
}
