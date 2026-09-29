export interface AxisScore {
  axis: string;
  score: number;
  refs: string[];
}

export type AgreementFlag = 'full' | 'partial' | 'no_agreement';

// Mirrors harvesting's CitationView/ProvenanceLinkView (harvesting_result_response.py).
export interface CitationDto {
  ref: string;
  source_type: string;
  title: string;
  patent_id?: string | null;
  url?: string;
  similarity?: number;
}

export type PreviewKindDto = 'pdf' | 'code' | 'none';

export interface PageDimensionDto {
  page_number: number;
  width: number;
  height: number;
}

export interface HighlightRectDto {
  page_number: number;
  x0: number;
  x1: number;
  top: number;
  bottom: number;
}

export interface LineRangeDto {
  start_line: number;
  end_line: number;
}

export interface ProvenanceLinkDto {
  document_id: string;
  locator?: string;
  source_kind?: string;
  hit_url?: string | null;
  chunk_id?: string;
  source_chunk_index?: number;
  page_number?: number | null;
  section_hint?: string;
  span_start?: number;
  span_end?: number;
  excerpt?: string;
  preview_kind?: PreviewKindDto;
  page_dimensions?: PageDimensionDto[] | null;
  highlight_rects?: HighlightRectDto[] | null;
  clean_excerpt?: string | null;
  file_path?: string | null;
  line_range?: LineRangeDto | null;
}

// Per-source hit as surfaced on EvidenceSourceViewDto (BUG183) — independent of
// which refs the scoring model chose to cite in an axis.
export interface EvidenceHitDto {
  title: string;
  patent_id?: string | null;
  url?: string;
  similarity?: number;
  jurisdiction?: string;
}

// Per-source view on a candidate/opportunity — the source's OWN verifiable hits
// (patent/corpus) or reasoning (LLM), decoupled from scorer-cited axis refs (BUG183).
export interface EvidenceSourceViewDto {
  source_type: EvidenceSourceType;
  available: boolean;
  status: string;
  hits?: EvidenceHitDto[];
  confidence?: number;
  reasoning?: string[];
  degraded?: boolean;
  degraded_sources?: string[];
}

export interface CandidateDto {
  id: string;
  title: string;
  abstract: string;
  claim_draft: string;
  novelty_hypothesis: string;
  source_asset_id: string;
  batch_id: string;
  created_at: string;
  candidate_id?: string;
  description?: string;
  maturity?: 'Mature' | 'Emerging';
  rank?: number;
  // 0-100 scale — do not multiply by 100 for display.
  weighted_score?: number;
  axes?: Record<string, AxisScore>;
  agreement_flag?: AgreementFlag;
  citations?: CitationDto[];
  provenance_links?: ProvenanceLinkDto[];
  source_availability?: Record<string, boolean>;
  source_status?: Record<string, string>;
  evidence_sources?: EvidenceSourceViewDto[];
}

export interface EvidenceBundleDto {
  id: string;
  candidate_id: string;
  patent_api_results: PatentApiResult[];
  corpus_results: CorpusResult[];
  llm_analysis: string;
  batch_id: string;
  created_at: string;
}

interface PatentApiResult {
  patent_number: string;
  title: string;
  relevance_score: number;
  url: string;
}

interface CorpusResult {
  document_id: string;
  title: string;
  similarity_score: number;
  excerpt: string;
}

export interface VerdictDto {
  id: string;
  candidate_id: string;
  // 0-100 scale — do not multiply by 100 for display.
  patentability_score: number;
  rationale: string;
  recommendation: 'pursue' | 'investigate' | 'abandon';
  batch_id: string;
  created_at: string;
}

// Prior-art proximity item — corpus matches carry url:"" and source:"corpus";
// live matches carry a real source ("USPTO"/"EPO") and a real url.
export interface PriorArtProximityDto {
  reference: string;
  title: string;
  url: string;
  source: string;
  // 0-1 scale.
  relevance_score: number;
  note: string;
}

export interface GroundedExcerptDto {
  chunk_id: string;
  section_label: string;
  text: string;
}

export interface GroundedInDto {
  chunk_ids: string[];
  excerpts: GroundedExcerptDto[];
}

// Per-axis score entry as emitted by the seeding v2 report — keyed by axis name.
export interface SeedingAxisScoreDto {
  axis?: string;
  score: number;
  refs: string[];
}

export interface SeedingSourceFlagsDto {
  evidence_reachable: boolean;
  corpus_only: boolean;
  live_sources_ok: string[];
  degraded_sources: string[];
}

export interface SeedingLandscapeDto {
  landscape_id: string;
  schema_version: string;
  source_flags: SeedingSourceFlagsDto;
}

export interface ConceptMapEntryDto {
  concept: string;
  density: number;
  corpus_axis: number | string;
  live_axis: number | string;
  opportunity_ids: string[];
  whitespace: boolean;
}

export interface SeedingResultDto {
  id: string;
  batch_id: string;
  opportunities: SeedingOpportunity[];
  created_at: string;
  // Deep-seeding v2 batch-level fields (absent on legacy reports).
  engine?: string;
  document_id?: string;
  explanation?: string;
  is_empty?: boolean;
  landscape?: SeedingLandscapeDto | null;
  source_flags?: SeedingSourceFlagsDto;
  concept_map?: ConceptMapEntryDto[];
  seeding_mode?: 'legacy' | 'deep';
}

export interface SeedingOpportunity {
  id: string;
  title: string;
  description: string;
  // 0-100 scale — do not multiply by 100 for display.
  confidence_score: number;
  roadmap_alignment: string;
  // Deep-seeding v2 per-opportunity fields (all optional; absent on legacy reports).
  target_concept?: string;
  prior_art_proximity?: PriorArtProximityDto[];
  novelty_delta?: string;
  mechanism?: string;
  claim_statement?: string;
  grounded_in?: GroundedInDto;
  category?: string;
  axes?: Record<string, SeedingAxisScoreDto>;
  citations?: CitationDto[];
  source_availability?: Record<string, boolean>;
  source_status?: Record<string, string>;
  round_index?: number;
  candidate_id?: string;
  evidence_bundle_id?: string;
  // 0-100 scale.
  weighted_score?: number;
  innovation_rationale?: string;
  evidence_sources?: EvidenceSourceViewDto[];
}

export interface HarvestingResultDto {
  id: string;
  batch_id: string;
  candidates: CandidateDto[];
  verdicts: VerdictDto[];
  summary: string;
  created_at: string;
}

export type BatchStatusValue = 'Pending' | 'Running' | 'Completed' | 'PartiallyFailed' | 'Failed';

// Mirrors GET /orchestrator/batches/{batch_id} from the v1 contract (US016).
export interface BatchStatusDto {
  batch_id: string;
  status: BatchStatusValue;
  document_count: number;
  completed_count: number;
  failed_count: number;
  created_at: string;
  updated_at: string;
}

export type AxisName = 'novelty' | 'non_obviousness' | 'utility' | 'enablement' | 'claim_clarity';
export type EvidenceSourceType = 'patent_api' | 'vector_corpus' | 'llm_deep_research';

export interface HarvestingAxisScoreDto {
  axis: string;
  score: number;
  refs: string[];
}

export interface OpportunityDetailDto {
  id: string;
  title: string;
  abstract: string;
  description?: string;
  claim_draft: string;
  novelty_hypothesis: string;
  source_asset_id: string;
  batch_id: string;
  created_at: string;
  candidate_id?: string;
  maturity?: string;
  rank?: number;
  weighted_score?: number;
  axes?: Record<string, HarvestingAxisScoreDto>;
  agreement_flag?: AgreementFlag;
  citations?: CitationDto[];
  provenance_links?: ProvenanceLinkDto[];
  source_availability?: Record<string, boolean>;
  source_status?: Record<string, string>;
  evidence_sources?: EvidenceSourceViewDto[];
  patentability_score?: number;
  recommendation?: 'pursue' | 'investigate' | 'abandon';
  rationale?: string;
}
