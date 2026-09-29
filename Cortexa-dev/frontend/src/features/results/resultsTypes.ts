import type {
  CitationDto,
  EvidenceSourceViewDto,
  GroundedInDto,
  HarvestingResultDto,
  PriorArtProximityDto,
  ProvenanceLinkDto,
  SeedingAxisScoreDto,
  SeedingResultDto,
} from '../../core/api/types';

export type ResultsErrorKind = 'server' | 'network' | 'not_found' | 'forbidden';

export interface ResultsError {
  kind: ResultsErrorKind;
  message: string;
  correlationId?: string;
  errorCode?: string;
}

export type Maturity = 'mature' | 'emerging' | 'speculative';

export type CandidateCategory = 'whitespace' | 'defensive' | 'adjacent' | 'continuation';

export interface RankedCandidate {
  id: string;
  title: string;
  abstract: string;
  rank: number;
  maturity: Maturity;
  // 0-100 scale — do not multiply by 100 for display.
  patentability_score: number;
  recommendation: 'pursue' | 'investigate' | 'abandon';
  rationale: string;
  source_asset_id: string;
  batch_id: string;
  // 0-100 scale — do not multiply by 100 for display.
  weighted_score?: number;
  axes?: Record<string, { axis: string; score: number; refs: string[] }>;
  agreementFlag?: 'full' | 'partial' | 'no_agreement';
  citations?: CitationDto[];
  provenanceLinks?: ProvenanceLinkDto[];
  claimDraft: string;
  noveltyHypothesis: string;
  description?: string;
  sourceAvailability?: Record<string, boolean>;
  sourceStatus?: Record<string, string>;
  evidenceSources?: EvidenceSourceViewDto[];
}

export interface CategorizedOpportunity {
  id: string;
  title: string;
  description: string;
  // 0-100 scale — do not multiply by 100 for display.
  confidence_score: number;
  roadmap_alignment: string;
  category: CandidateCategory;
  // Deep-seeding v2 fields carried through the store (BUG163 — dropped otherwise).
  targetConcept?: string;
  noveltyDelta?: string;
  mechanism?: string;
  claimStatement?: string;
  innovationRationale?: string;
  priorArtProximity?: PriorArtProximityDto[];
  groundedIn?: GroundedInDto;
  axes?: Record<string, SeedingAxisScoreDto>;
  citations?: CitationDto[];
  sourceAvailability?: Record<string, boolean>;
  sourceStatus?: Record<string, string>;
  evidenceSources?: EvidenceSourceViewDto[];
  weightedScore?: number;
  candidateId?: string;
  evidenceBundleId?: string;
  roundIndex?: number;
  // Batch-level landscape signal, stamped per-opportunity so the detail page can
  // give an honest prior-art empty state without re-fetching batch context.
  landscapeCorpusOnly?: boolean;
}

export interface ResultsData {
  harvesting: HarvestingResultDto;
  seeding: SeedingResultDto;
}
