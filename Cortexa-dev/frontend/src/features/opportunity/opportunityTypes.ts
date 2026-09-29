export type AxisName = 'novelty' | 'non_obviousness' | 'utility' | 'enablement' | 'claim_clarity' | 'Novelty' | 'Inventiveness' | 'Commercial' | 'Strategic' | 'Patentability';
export type EvidenceSourceType = 'patent_api' | 'vector_corpus' | 'llm_deep_research';
export type Recommendation = 'pursue' | 'investigate' | 'abandon';
export type OpportunitySource = 'harvesting' | 'seeding';
export type OpportunityMaturity = 'mature' | 'emerging' | 'speculative';
export type OpportunityAgreement = 'full' | 'partial' | 'no_agreement';
export type OpportunityCategory = 'whitespace' | 'defensive' | 'adjacent' | 'continuation';

export interface ResolvedCitation {
  ref: string;
  title?: string;
  url?: string;
  patentId?: string;
  similarity?: number;
  sourceType?: string;
}

export interface AxisScore {
  axis: AxisName;
  score: number;
  reasoning?: string;
  citations: ResolvedCitation[];
}

export type EvidenceState = 'active_cited' | 'active_uncited' | 'unavailable' | 'content_filtered' | 'source_error' | 'source_timeout';

export interface EvidenceHit {
  title: string;
  patentId?: string;
  url?: string;
  similarity?: number;
  jurisdiction?: string;
}

export interface EvidenceSource {
  sourceType: EvidenceSourceType;
  available: boolean;
  state: EvidenceState;
  confidence: number;
  citations: ResolvedCitation[];
  summary: string;
  hits?: EvidenceHit[];
  reasoning?: string[];
  degraded?: boolean;
  degradedSources?: string[];
}

export type PreviewKind = 'pdf' | 'code' | 'none';

export interface PageDimension {
  pageNumber: number;
  width: number;
  height: number;
}

export interface HighlightRect {
  pageNumber: number;
  x0: number;
  x1: number;
  top: number;
  bottom: number;
}

export interface LineRange {
  startLine: number;
  endLine: number;
}

export interface Provenance {
  sourceDocumentId: string;
  sourceFilename?: string | null;
  pageNumber?: number | null;
  spanStart?: number | null;
  spanEnd?: number | null;
  excerptText?: string | null;
  locator?: string;
  hitUrl?: string;
  sourceKind?: string;
  sectionHint?: string;
  chunkIndex?: number;
  chunkId?: string;
  previewKind?: PreviewKind;
  pageDimensions?: PageDimension[] | null;
  highlightRects?: HighlightRect[] | null;
  cleanExcerpt?: string | null;
  filePath?: string | null;
  lineRange?: LineRange | null;
}

export interface StatCards {
  similarPatents?: { count: number; sourceCount: number };
  claimSeeds?: { count: number; total: number };
  commercialPotential?: { level: 'high' | 'medium' | 'low'; industry: string };
}

export interface OpportunityDetail {
  candidateId: string;
  title: string;
  abstract: string;
  claimDraft: string;
  overallScore: number;
  recommendation: Recommendation;
  axisScores: AxisScore[];
  evidenceSources: EvidenceSource[];
  provenance?: Provenance;
  statCards?: StatCards;
  source: OpportunitySource;
  description?: string;
  roadmapAlignment?: string;
  confidence?: number;
  rank?: number;
  maturity?: OpportunityMaturity;
  agreementFlag?: OpportunityAgreement;
  rationale?: string;
  noveltyHypothesis?: string;
  category?: OpportunityCategory;
  degradeNote?: string | null;
  // Deep-seeding v2 presentation fields (seeding branch only).
  noveltyDelta?: string;
  mechanism?: string;
  targetConcept?: string;
  innovationRationale?: string;
  groundedIn?: GroundedIn;
  priorArtProximity?: PriorArtItem[];
  landscapeCorpusOnly?: boolean;
}

export interface GroundedExcerpt {
  chunkId: string;
  sectionLabel: string;
  text: string;
}

export interface GroundedIn {
  chunkIds: string[];
  excerpts: GroundedExcerpt[];
}

export interface PriorArtItem {
  reference: string;
  title: string;
  url: string;
  source: string;
  // 0-1 scale.
  relevanceScore: number;
  note: string;
  isCorpus: boolean;
}

export type OpportunityDetailErrorKind = 'server' | 'network' | 'not_found' | 'forbidden';

export interface OpportunityDetailError {
  kind: OpportunityDetailErrorKind;
  message: string;
  correlationId?: string;
  errorCode?: string;
}
