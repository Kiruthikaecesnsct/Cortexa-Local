import type { CandidateDto, VerdictDto, SeedingOpportunity, SeedingSourceFlagsDto } from '../../core/api/types';
import type { CandidateCategory, CategorizedOpportunity, Maturity, RankedCandidate } from './resultsTypes';

const CANDIDATE_CATEGORIES = new Set<CandidateCategory>(['whitespace', 'defensive', 'adjacent', 'continuation']);

function isCandidateCategory(value: string | undefined): value is CandidateCategory {
  return value !== undefined && CANDIDATE_CATEGORIES.has(value as CandidateCategory);
}

function resolveCategory(opportunity: SeedingOpportunity): CandidateCategory {
  const explicit = opportunity.category?.toLowerCase();
  if (isCandidateCategory(explicit)) return explicit;
  return getCandidateCategory(opportunity.roadmap_alignment);
}

function landscapeIsCorpusOnly(sourceFlags: SeedingSourceFlagsDto | undefined): boolean {
  if (!sourceFlags) return false;
  return sourceFlags.corpus_only || (sourceFlags.degraded_sources?.length ?? 0) > 0;
}

export function getMaturity(score: number): Maturity {
  if (score >= 70) return 'mature';
  if (score >= 40) return 'emerging';
  return 'speculative';
}

export function recommendationFromScore(score: number): 'pursue' | 'investigate' | 'abandon' {
  if (score >= 70) return 'pursue';
  if (score >= 40) return 'investigate';
  return 'abandon';
}

export function getCandidateCategory(roadmapAlignment: string): CandidateCategory {
  const lower = roadmapAlignment.toLowerCase();
  if (lower.includes('whitespace')) return 'whitespace';
  if (lower.includes('defensive')) return 'defensive';
  if (lower.includes('adjacent')) return 'adjacent';
  return 'continuation';
}

function mapServerMaturity(serverMaturity: 'Mature' | 'Emerging' | undefined): Maturity {
  if (serverMaturity === 'Mature') return 'mature';
  if (serverMaturity === 'Emerging') return 'emerging';
  return 'emerging';
}

export function toRankedCandidates(candidates: CandidateDto[], verdicts: VerdictDto[]): RankedCandidate[] {
  const verdictMap = new Map(verdicts.map((v) => [v.candidate_id, v]));

  return candidates
    .map((c) => {
      const verdict = verdictMap.get(c.id);
      const score = c.weighted_score ?? verdict?.patentability_score ?? 0;
      const serverMaturity = c.maturity;

      return {
        id: c.id,
        title: c.title,
        abstract: c.abstract,
        rank: c.rank ?? 0,
        maturity: serverMaturity !== undefined ? mapServerMaturity(serverMaturity) : getMaturity(score),
        patentability_score: score,
        recommendation: verdict?.recommendation ?? recommendationFromScore(score),
        rationale: verdict?.rationale ?? '',
        source_asset_id: c.source_asset_id,
        batch_id: c.batch_id,
        weighted_score: c.weighted_score,
        axes: c.axes,
        agreementFlag: c.agreement_flag,
        citations: c.citations,
        provenanceLinks: c.provenance_links,
        claimDraft: c.claim_draft,
        noveltyHypothesis: c.novelty_hypothesis,
        description: c.description,
        sourceAvailability: c.source_availability,
        sourceStatus: c.source_status,
        evidenceSources: c.evidence_sources,
      };
    })
    .sort((a, b) => {
      const aRankForSort = a.rank === 0 && candidates.find(c => c.id === a.id)?.rank === undefined
        ? Number.MAX_SAFE_INTEGER
        : a.rank;
      const bRankForSort = b.rank === 0 && candidates.find(c => c.id === b.id)?.rank === undefined
        ? Number.MAX_SAFE_INTEGER
        : b.rank;
      return aRankForSort - bRankForSort;
    });
}

export function toCategorizedOpportunities(
  opportunities: SeedingOpportunity[],
  sourceFlags?: SeedingSourceFlagsDto
): CategorizedOpportunity[] {
  const corpusOnly = landscapeIsCorpusOnly(sourceFlags);
  return opportunities.map((o) => ({
    id: o.id,
    title: o.title,
    description: o.description,
    confidence_score: o.confidence_score,
    roadmap_alignment: o.roadmap_alignment,
    category: resolveCategory(o),
    targetConcept: o.target_concept,
    noveltyDelta: o.novelty_delta,
    mechanism: o.mechanism,
    claimStatement: o.claim_statement,
    innovationRationale: o.innovation_rationale,
    priorArtProximity: o.prior_art_proximity,
    groundedIn: o.grounded_in,
    axes: o.axes,
    citations: o.citations,
    sourceAvailability: o.source_availability,
    sourceStatus: o.source_status,
    evidenceSources: o.evidence_sources,
    weightedScore: o.weighted_score,
    candidateId: o.candidate_id,
    evidenceBundleId: o.evidence_bundle_id,
    roundIndex: o.round_index,
    landscapeCorpusOnly: corpusOnly,
  }));
}
