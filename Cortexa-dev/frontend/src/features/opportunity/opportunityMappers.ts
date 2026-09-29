import type { CitationDto, EvidenceHitDto, EvidenceSourceViewDto, GroundedInDto, HighlightRectDto, LineRangeDto, PageDimensionDto, PriorArtProximityDto, ProvenanceLinkDto, SeedingAxisScoreDto } from '../../core/api/types';
import type { CategorizedOpportunity, RankedCandidate } from '../results/resultsTypes';
import type { AxisName, AxisScore, EvidenceHit, EvidenceSource, EvidenceSourceType, GroundedIn, HighlightRect, LineRange, OpportunityDetail, PageDimension, PriorArtItem, Provenance, Recommendation, ResolvedCitation, EvidenceState } from './opportunityTypes';

const RESULTS_AXIS_ORDER: AxisName[] = [
  'Novelty',
  'Inventiveness',
  'Commercial',
  'Strategic',
  'Patentability',
];

const RESULTS_AXIS_NAME_SET = new Set<string>(RESULTS_AXIS_ORDER);

function isResultsAxisName(value: string): value is AxisName {
  return RESULTS_AXIS_NAME_SET.has(value);
}

function buildCitationIndex(citationDtos: CitationDto[] | undefined): Map<string, CitationDto> {
  const index = new Map<string, CitationDto>();
  if (!citationDtos) return index;
  for (const c of citationDtos) {
    index.set(c.ref, c);
  }
  return index;
}

function resolveRef(ref: string, index: Map<string, CitationDto>): ResolvedCitation {
  const citation = index.get(ref);
  if (!citation) return { ref };
  return {
    ref: citation.ref,
    title: citation.title || undefined,
    url: citation.url || undefined,
    patentId: citation.patent_id || undefined,
    similarity: citation.similarity,
    sourceType: citation.source_type,
  };
}

function deriveRecommendationFromScore(score: number): Recommendation {
  if (score >= 70) return 'pursue';
  if (score >= 40) return 'investigate';
  return 'abandon';
}

function meanSimilarity(items: Array<{ similarity?: number }>): number | undefined {
  const validScores = items.map((c) => c.similarity).filter((s): s is number => s != null);
  if (validScores.length === 0) return undefined;
  return validScores.reduce((sum, s) => sum + s, 0) / validScores.length;
}

// BUG183: no more blanket "available" fallback — a source with no real
// similarity data reports 0 confidence rather than a fabricated percentage.
function computeEvidenceConfidence(citations: Array<{ similarity?: number }>, fallback = 0): number {
  const mean = meanSimilarity(citations);
  return mean ?? fallback;
}

function formatEvidenceSummary(state: EvidenceState, citationCount: number, sourceType: EvidenceSourceType): string {
  if (state === 'content_filtered') {
    return "This source ran, but the model provider's content filter refused the request. It's excluded from this score — not missing, and not a zero result.";
  }
  if (state === 'source_error') {
    return 'This source ran but failed with an error before returning results. It\'s excluded from this score — not missing, and not a zero result.';
  }
  if (state === 'source_timeout') {
    return 'This source ran but timed out before returning results. It\'s excluded from this score — not missing, and not a zero result.';
  }
  if (state === 'unavailable') return 'No results from this source.';
  if (state === 'active_uncited') return 'Active — corroborating hit not among cited refs.';
  const labels: Record<EvidenceSourceType, string> = {
    patent_api: 'Patent API',
    vector_corpus: 'Corpus Vector Search',
    llm_deep_research: 'LLM Deep Research',
  };
  const label = labels[sourceType];
  return `${citationCount} citation${citationCount === 1 ? '' : 's'} from ${label}.`;
}

interface EvidenceAvailability {
  state: EvidenceState;
  available: boolean;
}

type FixedEvidenceStatus = 'filtered' | 'empty' | 'error' | 'timeout';

const FIXED_STATUS_RESULT: Record<FixedEvidenceStatus, EvidenceAvailability> = {
  filtered: { state: 'content_filtered', available: true },
  empty: { state: 'unavailable', available: true },
  error: { state: 'source_error', available: false },
  timeout: { state: 'source_timeout', available: false },
};

function isFixedEvidenceStatus(status: string | undefined): status is FixedEvidenceStatus {
  return !!status && status in FIXED_STATUS_RESULT;
}

function deriveActiveState(citedCount: number): EvidenceState {
  return citedCount >= 1 ? 'active_cited' : 'active_uncited';
}

function deriveLegacyContributedState(contributed: boolean, citedCount: number): EvidenceAvailability {
  const state: EvidenceState = contributed ? deriveActiveState(citedCount) : 'unavailable';
  return { state, available: contributed };
}

function deriveEvidenceStateAndAvailability(
  status: string | undefined,
  contributed: boolean,
  citedCount: number
): EvidenceAvailability {
  if (isFixedEvidenceStatus(status)) return FIXED_STATUS_RESULT[status];
  if (status === 'active') return { state: deriveActiveState(citedCount), available: true };
  return deriveLegacyContributedState(contributed, citedCount);
}

function buildEvidenceSource(
  sourceType: EvidenceSourceType,
  citations: Array<{ ref: string; source_type: string; title?: string; patent_id?: string | null; url?: string; similarity?: number }>,
  contributed: boolean,
  status: string | undefined
): EvidenceSource {
  const citedCount = citations.length;
  const { state, available } = deriveEvidenceStateAndAvailability(status, contributed, citedCount);
  const confidence = computeEvidenceConfidence(citations);
  const summary = formatEvidenceSummary(state, citedCount, sourceType);

  return {
    sourceType,
    available,
    state,
    confidence,
    citations: citations.map((c) => ({
      ref: c.ref,
      title: c.title || undefined,
      url: c.url || undefined,
      patentId: c.patent_id || undefined,
      similarity: c.similarity,
      sourceType: c.source_type,
    })),
    summary,
  };
}

function buildEvidenceSources(
  citationDtos: Array<{ ref: string; source_type: string; similarity?: number }> | undefined,
  sourceAvailability: Record<string, boolean> | undefined,
  sourceStatus: Record<string, string> | undefined
): EvidenceSource[] {
  const allCitations = citationDtos ?? [];
  const availabilityMap = sourceAvailability ?? {};
  const statusMap = sourceStatus ?? {};
  const groupByType = (type: EvidenceSourceType) =>
    allCitations.filter((c) => c.source_type === type);

  return [
    buildEvidenceSource('patent_api', groupByType('patent_api'), availabilityMap['patent_api'] ?? false, statusMap['patent_api']),
    buildEvidenceSource('vector_corpus', groupByType('vector_corpus'), availabilityMap['vector_corpus'] ?? false, statusMap['vector_corpus']),
    buildEvidenceSource('llm_deep_research', groupByType('llm_deep_research'), availabilityMap['llm_deep_research'] ?? false, statusMap['llm_deep_research']),
  ];
}

const EVIDENCE_SOURCE_ORDER: EvidenceSourceType[] = ['patent_api', 'vector_corpus', 'llm_deep_research'];

const EVIDENCE_SOURCE_NOTE_LABEL: Record<EvidenceSourceType, string> = {
  patent_api: 'Live Patent API',
  vector_corpus: 'Corpus Vector Search',
  llm_deep_research: 'LLM Deep Research',
};

function isContributingStatus(status: string | undefined): boolean {
  return status === 'active' || status === 'empty';
}

function degradeReasonForSource(sourceType: EvidenceSourceType, status: string | undefined): string | null {
  const label = EVIDENCE_SOURCE_NOTE_LABEL[sourceType];
  if (status === 'filtered') return `${label} was blocked by the provider's content policy`;
  if (status === 'error') return `${label} failed due to an error`;
  if (status === 'timeout') return `${label} timed out`;
  return null;
}

export function joinReasonsWithAnd(reasons: string[]): string {
  if (reasons.length === 0) return '';
  if (reasons.length === 1) return reasons[0]!;
  if (reasons.length === 2) return `${reasons[0]} and ${reasons[1]}`;
  return `${reasons.slice(0, -1).join(', ')}, and ${reasons[reasons.length - 1]}`;
}

function buildDegradeNote(sourceStatus: Record<string, string> | undefined): string | null {
  if (!sourceStatus) return null;
  const contributingCount = EVIDENCE_SOURCE_ORDER.filter((t) => isContributingStatus(sourceStatus[t])).length;
  if (contributingCount >= 3) return null;
  const reasons = EVIDENCE_SOURCE_ORDER
    .filter((t) => !isContributingStatus(sourceStatus[t]))
    .map((t) => degradeReasonForSource(t, sourceStatus[t]))
    .filter((r): r is string => r !== null);
  if (reasons.length === 0) return null;
  return `Scored on ${contributingCount} of 3 sources — ${joinReasonsWithAnd(reasons)}.`;
}

// BUG183 — per-source evidence views (evidence_sources). A card's body is driven
// by what the source HAS (its own hits/reasoning), not by which refs the scoring
// model happened to cite in an axis.
function mapEvidenceHit(hit: EvidenceHitDto): EvidenceHit {
  return {
    title: hit.title,
    patentId: hit.patent_id || undefined,
    url: hit.url,
    similarity: hit.similarity,
    jurisdiction: hit.jurisdiction,
  };
}

function hasDisplayableEvidence(hits: EvidenceHit[], reasoning: string[]): boolean {
  return hits.length > 0 || reasoning.length > 0;
}

// Displayable evidence always wins over a "fixed" status string. The LLM view in
// particular derives status from hit count only (hits come from finding.citations,
// not finding.findings) — a finding with reasoning but no citations legitimately
// arrives as status "empty" with a populated reasoning array. That must still
// render as Available with its reasoning, never as "ran, returned 0 matches".
function deriveViewEvidenceStateAndAvailability(
  status: string | undefined,
  available: boolean,
  hits: EvidenceHit[],
  reasoning: string[]
): EvidenceAvailability {
  if (hasDisplayableEvidence(hits, reasoning)) return { state: 'active_cited', available: true };
  if (isFixedEvidenceStatus(status)) return FIXED_STATUS_RESULT[status];
  if (!available) return { state: 'unavailable', available: false };
  return { state: 'active_uncited', available: true };
}

function computeViewConfidence(
  sourceType: EvidenceSourceType,
  hits: EvidenceHit[],
  reasoning: string[],
  dtoConfidence: number | undefined
): number {
  if (!hasDisplayableEvidence(hits, reasoning)) return 0;
  if (sourceType === 'llm_deep_research') return dtoConfidence ?? 0;
  return meanSimilarity(hits) ?? dtoConfidence ?? 0;
}

function buildEvidenceSourceFromView(view: EvidenceSourceViewDto): EvidenceSource {
  const hits = (view.hits ?? []).map(mapEvidenceHit);
  const reasoning = view.reasoning ?? [];
  const { state, available } = deriveViewEvidenceStateAndAvailability(view.status, view.available, hits, reasoning);
  const confidence = computeViewConfidence(view.source_type, hits, reasoning, view.confidence);
  const summary = formatEvidenceSummary(state, hits.length, view.source_type);

  return {
    sourceType: view.source_type,
    available,
    state,
    confidence,
    citations: [],
    summary,
    hits,
    reasoning,
    degraded: view.degraded ?? false,
    degradedSources: view.degraded_sources ?? [],
  };
}

function buildEvidenceSourcesFromViews(views: EvidenceSourceViewDto[]): EvidenceSource[] {
  const byType = new Map(views.map((v) => [v.source_type, v]));
  return EVIDENCE_SOURCE_ORDER.map((type) => {
    const view = byType.get(type);
    return view ? buildEvidenceSourceFromView(view) : buildEvidenceSource(type, [], false, undefined);
  });
}

function hasEvidenceSourceViews(views: EvidenceSourceViewDto[] | undefined): views is EvidenceSourceViewDto[] {
  return !!views && views.length > 0;
}

// Backward compatible: old reports without evidence_sources fall back to the
// citation-derived cards (pre-BUG183 behavior).
function resolveEvidenceSources(
  views: EvidenceSourceViewDto[] | undefined,
  citations: CitationDto[] | undefined,
  sourceAvailability: Record<string, boolean> | undefined,
  sourceStatus: Record<string, string> | undefined
): EvidenceSource[] {
  if (hasEvidenceSourceViews(views)) return buildEvidenceSourcesFromViews(views);
  return buildEvidenceSources(citations, sourceAvailability, sourceStatus);
}

function parseLocatorSpan(locator: string): { spanStart?: number; spanEnd?: number } {
  const match = /^chars:(\d+)-(\d+)$/.exec(locator);
  if (!match) return {};
  return { spanStart: parseInt(match[1]!, 10), spanEnd: parseInt(match[2]!, 10) };
}

function mapAxesRecord(
  axes: Record<string, SeedingAxisScoreDto> | undefined,
  citations: CitationDto[] | undefined
): AxisScore[] {
  const axisEntries = axes ? Object.entries(axes) : [];
  const citationIndex = buildCitationIndex(citations);
  return axisEntries
    .map(([key, a]) => ({ axisName: a.axis ?? key, score: a.score, refs: a.refs }))
    .filter((a) => isResultsAxisName(a.axisName))
    .map((a) => ({
      axis: a.axisName as AxisName,
      score: a.score,
      citations: a.refs.map((ref) => resolveRef(ref, citationIndex)),
    }))
    .sort((a, b) => RESULTS_AXIS_ORDER.indexOf(a.axis) - RESULTS_AXIS_ORDER.indexOf(b.axis));
}

function mapCandidateAxes(candidate: RankedCandidate): AxisScore[] {
  return mapAxesRecord(candidate.axes, candidate.citations);
}

function isCorpusPriorArt(item: PriorArtProximityDto): boolean {
  return item.source === 'corpus' || !item.url;
}

function mapPriorArt(items: PriorArtProximityDto[] | undefined): PriorArtItem[] | undefined {
  if (!items) return undefined;
  return items.map((i) => ({
    reference: i.reference,
    title: i.title,
    url: i.url,
    source: i.source,
    relevanceScore: i.relevance_score,
    note: i.note,
    isCorpus: isCorpusPriorArt(i),
  }));
}

function mapGroundedIn(grounded: GroundedInDto | undefined): GroundedIn | undefined {
  if (!grounded) return undefined;
  return {
    chunkIds: grounded.chunk_ids ?? [],
    excerpts: (grounded.excerpts ?? []).map((e) => ({
      chunkId: e.chunk_id,
      sectionLabel: e.section_label,
      text: e.text,
    })),
  };
}

function hasEvidenceData(opportunity: CategorizedOpportunity): boolean {
  return !!opportunity.citations || !!opportunity.sourceAvailability || !!opportunity.sourceStatus || !!opportunity.evidenceSources;
}

function mapPageDimensions(dims: PageDimensionDto[] | null | undefined): PageDimension[] | null {
  if (!dims) return null;
  return dims.map((d) => ({ pageNumber: d.page_number, width: d.width, height: d.height }));
}

function mapHighlightRects(rects: HighlightRectDto[] | null | undefined): HighlightRect[] | null {
  if (!rects) return null;
  return rects.map((r) => ({ pageNumber: r.page_number, x0: r.x0, x1: r.x1, top: r.top, bottom: r.bottom }));
}

function mapLineRange(range: LineRangeDto | null | undefined): LineRange | null {
  if (!range) return null;
  return { startLine: range.start_line, endLine: range.end_line };
}

function mapCandidateProvenance(candidate: RankedCandidate): Provenance | undefined {
  const firstLink: ProvenanceLinkDto | undefined = candidate.provenanceLinks?.[0];
  if (!firstLink) return undefined;
  const spanParsed = firstLink.locator ? parseLocatorSpan(firstLink.locator) : {};
  return {
    sourceDocumentId: firstLink.document_id,
    locator: firstLink.locator,
    hitUrl: firstLink.hit_url ?? undefined,
    sourceKind: firstLink.source_kind,
    spanStart: firstLink.span_start ?? spanParsed.spanStart,
    spanEnd: firstLink.span_end ?? spanParsed.spanEnd,
    excerptText: firstLink.excerpt,
    pageNumber: firstLink.page_number,
    sectionHint: firstLink.section_hint,
    chunkIndex: firstLink.source_chunk_index,
    chunkId: firstLink.chunk_id,
    previewKind: firstLink.preview_kind ?? 'none',
    pageDimensions: mapPageDimensions(firstLink.page_dimensions),
    highlightRects: mapHighlightRects(firstLink.highlight_rects),
    cleanExcerpt: firstLink.clean_excerpt,
    filePath: firstLink.file_path,
    lineRange: mapLineRange(firstLink.line_range),
  };
}

export function candidateToDetail(candidate: RankedCandidate): OpportunityDetail {
  return {
    candidateId: candidate.id,
    title: candidate.title,
    abstract: candidate.abstract,
    claimDraft: candidate.claimDraft,
    overallScore: candidate.patentability_score,
    recommendation: candidate.recommendation,
    source: 'harvesting',
    axisScores: mapCandidateAxes(candidate),
    evidenceSources: resolveEvidenceSources(candidate.evidenceSources, candidate.citations, candidate.sourceAvailability, candidate.sourceStatus),
    provenance: mapCandidateProvenance(candidate),
    statCards: undefined,
    rank: candidate.rank,
    maturity: candidate.maturity,
    agreementFlag: candidate.agreementFlag,
    rationale: candidate.rationale,
    noveltyHypothesis: candidate.noveltyHypothesis,
    degradeNote: buildDegradeNote(candidate.sourceStatus),
  };
}

export function opportunityToDetail(opportunity: CategorizedOpportunity): OpportunityDetail {
  const hasEvidence = hasEvidenceData(opportunity);
  return {
    candidateId: opportunity.id,
    title: opportunity.title,
    abstract: opportunity.description,
    claimDraft: opportunity.claimStatement ?? '',
    overallScore: opportunity.confidence_score,
    recommendation: deriveRecommendationFromScore(opportunity.confidence_score),
    source: 'seeding',
    axisScores: opportunity.axes ? mapAxesRecord(opportunity.axes, opportunity.citations) : [],
    evidenceSources: hasEvidence
      ? resolveEvidenceSources(opportunity.evidenceSources, opportunity.citations, opportunity.sourceAvailability, opportunity.sourceStatus)
      : [],
    provenance: undefined,
    statCards: undefined,
    description: opportunity.description,
    roadmapAlignment: opportunity.roadmap_alignment,
    confidence: opportunity.confidence_score,
    category: opportunity.category,
    degradeNote: hasEvidence ? buildDegradeNote(opportunity.sourceStatus) : null,
    noveltyDelta: opportunity.noveltyDelta,
    mechanism: opportunity.mechanism,
    targetConcept: opportunity.targetConcept,
    innovationRationale: opportunity.innovationRationale,
    groundedIn: mapGroundedIn(opportunity.groundedIn),
    priorArtProximity: mapPriorArt(opportunity.priorArtProximity),
    landscapeCorpusOnly: opportunity.landscapeCorpusOnly,
  };
}
