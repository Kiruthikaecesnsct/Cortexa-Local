using System.Text.Json.Serialization;

namespace Cortexa.JobOrchestrator.Application.Models;

public sealed class HarvestingAxisScoreDto
{
    [JsonPropertyName("axis")] public string Axis { get; set; } = string.Empty;
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("refs")] public List<string> Refs { get; set; } = [];
}

public sealed class CitationDto
{
    [JsonPropertyName("ref")] public string Ref { get; set; } = string.Empty;
    [JsonPropertyName("source_type")] public string SourceType { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("patent_id")] public string? PatentId { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("similarity")] public double Similarity { get; set; }
}

public sealed class PageDimensionDto
{
    [JsonPropertyName("page_number")] public int PageNumber { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }
}

public sealed class HighlightRectDto
{
    [JsonPropertyName("page_number")] public int PageNumber { get; set; }
    [JsonPropertyName("x0")] public double X0 { get; set; }
    [JsonPropertyName("x1")] public double X1 { get; set; }
    [JsonPropertyName("top")] public double Top { get; set; }
    [JsonPropertyName("bottom")] public double Bottom { get; set; }
}

public sealed class LineRangeDto
{
    [JsonPropertyName("start_line")] public int StartLine { get; set; }
    [JsonPropertyName("end_line")] public int EndLine { get; set; }
}

public sealed class ProvenanceLinkDto
{
    [JsonPropertyName("document_id")] public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("locator")] public string Locator { get; set; } = string.Empty;
    [JsonPropertyName("source_kind")] public string SourceKind { get; set; } = string.Empty;
    [JsonPropertyName("hit_url")] public string? HitUrl { get; set; }
    [JsonPropertyName("chunk_id")] public string? ChunkId { get; set; }
    [JsonPropertyName("source_chunk_index")] public int? SourceChunkIndex { get; set; }
    [JsonPropertyName("page_number")] public int? PageNumber { get; set; }
    [JsonPropertyName("section_hint")] public string? SectionHint { get; set; }
    [JsonPropertyName("span_start")] public int? SpanStart { get; set; }
    [JsonPropertyName("span_end")] public int? SpanEnd { get; set; }
    [JsonPropertyName("excerpt")] public string? Excerpt { get; set; }
    [JsonPropertyName("preview_kind")] public string? PreviewKind { get; set; }
    [JsonPropertyName("page_dimensions")] public List<PageDimensionDto>? PageDimensions { get; set; }
    [JsonPropertyName("highlight_rects")] public List<HighlightRectDto>? HighlightRects { get; set; }
    [JsonPropertyName("clean_excerpt")] public string? CleanExcerpt { get; set; }
    [JsonPropertyName("file_path")] public string? FilePath { get; set; }
    [JsonPropertyName("line_range")] public LineRangeDto? LineRange { get; set; }
}

public sealed class CandidateResultDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("abstract")] public string Abstract { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("claim_draft")] public string ClaimDraft { get; set; } = string.Empty;
    [JsonPropertyName("novelty_hypothesis")] public string NoveltyHypothesis { get; set; } = string.Empty;
    [JsonPropertyName("source_asset_id")] public string SourceAssetId { get; set; } = string.Empty;
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = string.Empty;
    [JsonPropertyName("candidate_id")] public string CandidateId { get; set; } = string.Empty;
    [JsonPropertyName("maturity")] public string Maturity { get; set; } = string.Empty;
    [JsonPropertyName("rank")] public int Rank { get; set; }
    [JsonPropertyName("weighted_score")] public double WeightedScore { get; set; }
    [JsonPropertyName("axes")] public Dictionary<string, HarvestingAxisScoreDto> Axes { get; set; } = new();
    [JsonPropertyName("agreement_flag")] public string AgreementFlag { get; set; } = string.Empty;
    [JsonPropertyName("citations")] public List<CitationDto> Citations { get; set; } = [];
    [JsonPropertyName("provenance_links")] public List<ProvenanceLinkDto> ProvenanceLinks { get; set; } = [];
    [JsonPropertyName("source_availability")] public Dictionary<string, bool> SourceAvailability { get; set; } = new();
    [JsonPropertyName("source_status")] public Dictionary<string, string> SourceStatus { get; set; } = new();
}

public sealed class VerdictResultDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("candidate_id")] public string CandidateId { get; set; } = string.Empty;
    [JsonPropertyName("patentability_score")] public double PatentabilityScore { get; set; }
    [JsonPropertyName("rationale")] public string Rationale { get; set; } = string.Empty;
    [JsonPropertyName("recommendation")] public string Recommendation { get; set; } = string.Empty;
    [JsonPropertyName("drafted_claim")] public string? DraftedClaim { get; set; }
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = string.Empty;
}

public sealed class HarvestingResultRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = string.Empty;
    [JsonPropertyName("document_id")] public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("engine")] public string Engine { get; set; } = string.Empty;
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = string.Empty;
    [JsonPropertyName("candidates")] public List<CandidateResultDto> Candidates { get; set; } = [];
}

public sealed class ReportHeaderDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = string.Empty;
    [JsonPropertyName("document_id")] public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("engine")] public string Engine { get; set; } = string.Empty;
    [JsonPropertyName("doc_type")] public string DocType { get; set; } = string.Empty;
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = string.Empty;
    [JsonPropertyName("candidate_count")] public int CandidateCount { get; set; }
}

public sealed class ReportCandidateDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = string.Empty;
    [JsonPropertyName("engine")] public string Engine { get; set; } = string.Empty;
    [JsonPropertyName("doc_type")] public string DocType { get; set; } = string.Empty;
    [JsonPropertyName("report_id")] public string ReportId { get; set; } = string.Empty;
    [JsonPropertyName("candidate_id")] public string CandidateId { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("claim_text")] public string ClaimText { get; set; } = string.Empty;
    [JsonPropertyName("claim_draft")] public string ClaimDraft { get; set; } = string.Empty;
    [JsonPropertyName("maturity")] public string Maturity { get; set; } = string.Empty;
    [JsonPropertyName("rank")] public int Rank { get; set; }
    [JsonPropertyName("weighted_score")] public double WeightedScore { get; set; }
    [JsonPropertyName("axes")] public Dictionary<string, HarvestingAxisScoreDto> Axes { get; set; } = new();
    [JsonPropertyName("agreement_flag")] public string AgreementFlag { get; set; } = string.Empty;
    [JsonPropertyName("citations")] public List<CitationDto> Citations { get; set; } = [];
    [JsonPropertyName("provenance_links")] public List<ProvenanceLinkDto> ProvenanceLinks { get; set; } = [];
    [JsonPropertyName("source_availability")] public Dictionary<string, bool> SourceAvailability { get; set; } = new();
    [JsonPropertyName("source_status")] public Dictionary<string, string> SourceStatus { get; set; } = new();
    [JsonPropertyName("evidence_sources")] public List<object> EvidenceSources { get; set; } = [];
}

public sealed class ReportEnvelopeDto
{
    [JsonPropertyName("doc_type")] public string? DocType { get; set; }
    [JsonPropertyName("engine")] public string? Engine { get; set; }
    [JsonPropertyName("candidates")] public List<CandidateResultDto>? Candidates { get; set; }
}

public sealed class SeedingOpportunityDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("confidence_score")] public double ConfidenceScore { get; set; }
    [JsonPropertyName("roadmap_alignment")] public string RoadmapAlignment { get; set; } = string.Empty;
}

public sealed class SeedingReportRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = string.Empty;
    [JsonPropertyName("engine")] public string Engine { get; set; } = string.Empty;
    [JsonPropertyName("opportunities")] public List<SeedingOpportunityDto> Opportunities { get; set; } = [];
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = string.Empty;
}

