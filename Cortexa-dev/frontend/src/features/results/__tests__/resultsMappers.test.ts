import { describe, expect, it } from 'vitest';
import type { CandidateDto, SeedingOpportunity, VerdictDto } from '../../../core/api/types';
import {
  getCandidateCategory,
  getMaturity,
  recommendationFromScore,
  toCategorizedOpportunities,
  toRankedCandidates,
} from '../resultsMappers';

describe('getMaturity', () => {
  it('returns mature for score >= 70', () => {
    expect(getMaturity(70)).toBe('mature');
    expect(getMaturity(100)).toBe('mature');
    expect(getMaturity(85)).toBe('mature');
  });

  it('returns emerging for score >= 40 and < 70', () => {
    expect(getMaturity(40)).toBe('emerging');
    expect(getMaturity(69)).toBe('emerging');
    expect(getMaturity(55)).toBe('emerging');
  });

  it('returns speculative for score < 40', () => {
    expect(getMaturity(39)).toBe('speculative');
    expect(getMaturity(0)).toBe('speculative');
  });
});

describe('recommendationFromScore', () => {
  it('returns pursue for score >= 70', () => {
    expect(recommendationFromScore(70)).toBe('pursue');
    expect(recommendationFromScore(100)).toBe('pursue');
  });

  it('returns investigate for score between 40 and 69', () => {
    expect(recommendationFromScore(40)).toBe('investigate');
    expect(recommendationFromScore(69)).toBe('investigate');
  });

  it('returns abandon for score < 40', () => {
    expect(recommendationFromScore(39)).toBe('abandon');
    expect(recommendationFromScore(0)).toBe('abandon');
  });
});

describe('getCandidateCategory', () => {
  it('returns whitespace when alignment contains whitespace', () => {
    expect(getCandidateCategory('whitespace opportunity')).toBe('whitespace');
    expect(getCandidateCategory('WHITESPACE gap')).toBe('whitespace');
  });

  it('returns defensive when alignment contains defensive', () => {
    expect(getCandidateCategory('defensive patent strategy')).toBe('defensive');
  });

  it('returns adjacent when alignment contains adjacent', () => {
    expect(getCandidateCategory('adjacent market expansion')).toBe('adjacent');
  });

  it('returns continuation for unmatched alignment', () => {
    expect(getCandidateCategory('future roadmap plans')).toBe('continuation');
    expect(getCandidateCategory('')).toBe('continuation');
  });

  it('is case-insensitive', () => {
    expect(getCandidateCategory('DEFENSIVE')).toBe('defensive');
    expect(getCandidateCategory('Adjacent')).toBe('adjacent');
  });
});

function makeCandidate(id: string, overrides?: Partial<CandidateDto>): CandidateDto {
  return {
    id,
    title: `Title ${id}`,
    abstract: '',
    claim_draft: '',
    novelty_hypothesis: '',
    source_asset_id: 'src',
    batch_id: 'batch-1',
    created_at: '',
    ...overrides,
  };
}

function makeVerdict(candidateId: string, score: number): VerdictDto {
  return {
    id: `v-${candidateId}`,
    candidate_id: candidateId,
    patentability_score: score,
    recommendation: 'pursue',
    rationale: 'Good',
    batch_id: 'batch-1',
    created_at: '',
  };
}

describe('toRankedCandidates', () => {
  it('uses server-provided rank and sorts by rank ascending', () => {
    const candidates = [
      makeCandidate('a', { rank: 2 }),
      makeCandidate('b', { rank: 1 }),
      makeCandidate('c', { rank: 3 }),
    ];
    const verdicts = [makeVerdict('a', 50), makeVerdict('b', 90), makeVerdict('c', 30)];

    const result = toRankedCandidates(candidates, verdicts);

    expect(result[0]!.id).toBe('b');
    expect(result[0]!.rank).toBe(1);
    expect(result[1]!.id).toBe('a');
    expect(result[1]!.rank).toBe(2);
    expect(result[2]!.id).toBe('c');
    expect(result[2]!.rank).toBe(3);
  });

  it('preserves server rank even when score is low', () => {
    const candidates = [
      makeCandidate('a', { rank: 1 }),
      makeCandidate('b', { rank: 2 }),
    ];
    const verdicts = [makeVerdict('a', 20), makeVerdict('b', 90)];

    const result = toRankedCandidates(candidates, verdicts);

    expect(result[0]!.id).toBe('a');
    expect(result[0]!.rank).toBe(1);
    expect(result[0]!.patentability_score).toBe(20);
  });

  it('maps server maturity Mature to mature', () => {
    const result = toRankedCandidates(
      [makeCandidate('a', { rank: 1, maturity: 'Mature' })],
      [makeVerdict('a', 75)]
    );
    expect(result[0]!.maturity).toBe('mature');
  });

  it('maps server maturity Emerging to emerging', () => {
    const result = toRankedCandidates(
      [makeCandidate('a', { rank: 1, maturity: 'Emerging' })],
      [makeVerdict('a', 75)]
    );
    expect(result[0]!.maturity).toBe('emerging');
  });

  it('uses server maturity regardless of score', () => {
    const result = toRankedCandidates(
      [makeCandidate('a', { rank: 1, maturity: 'Emerging' })],
      [makeVerdict('a', 90)]
    );
    expect(result[0]!.maturity).toBe('emerging');
  });

  it('derives maturity from score when server maturity is absent', () => {
    const result = toRankedCandidates(
      [makeCandidate('a', { rank: 1 })],
      [makeVerdict('a', 75)]
    );
    expect(result[0]!.maturity).toBe('mature');
  });

  it('assigns score 0 and speculative maturity when no matching verdict and no server maturity', () => {
    const result = toRankedCandidates([makeCandidate('orphan', { rank: 1 })], []);
    expect(result[0]!.patentability_score).toBe(0);
    expect(result[0]!.maturity).toBe('speculative');
    expect(result[0]!.recommendation).toBe('abandon');
  });

  it('sources weighted_score over the verdict patentability_score when both are present', () => {
    const result = toRankedCandidates(
      [makeCandidate('a', { rank: 1, weighted_score: 88.5 })],
      [makeVerdict('a', 20)]
    );
    expect(result[0]!.patentability_score).toBe(88.5);
  });

  it('falls back to verdict patentability_score when weighted_score is absent', () => {
    const result = toRankedCandidates(
      [makeCandidate('a', { rank: 1 })],
      [makeVerdict('a', 63)]
    );
    expect(result[0]!.patentability_score).toBe(63);
  });

  it('falls back to recommendationFromScore when verdict is absent', () => {
    const result = toRankedCandidates(
      [makeCandidate('a', { rank: 1, weighted_score: 80 })],
      []
    );
    expect(result[0]!.recommendation).toBe('pursue');
  });

  it('carries over weighted_score, axes, agreement_flag, citations, provenance_links from server', () => {
    const axes = { Novelty: { axis: 'Novelty', score: 85, refs: ['ref1'] } };
    const citations = [
      { ref: 'E1', source_type: 'patent_api', title: 'Citation 1', patent_id: 'US123', url: '', similarity: 0.9 },
    ];
    const provenanceLinks = [{ document_id: 'doc-1', locator: 'p3', source_kind: 'pdf' }];
    const result = toRankedCandidates(
      [
        makeCandidate('a', {
          rank: 1,
          maturity: 'Mature',
          weighted_score: 88.5,
          axes,
          agreement_flag: 'full',
          citations,
          provenance_links: provenanceLinks,
        }),
      ],
      [makeVerdict('a', 85)]
    );

    expect(result[0]!.weighted_score).toBe(88.5);
    expect(result[0]!.axes).toEqual(axes);
    expect(result[0]!.agreementFlag).toBe('full');
    expect(result[0]!.citations).toEqual(citations);
    expect(result[0]!.provenanceLinks).toEqual(provenanceLinks);
  });

  it('carries evidence_sources through to evidenceSources (BUG183)', () => {
    const evidenceSources = [
      { source_type: 'patent_api' as const, available: true, status: 'active', hits: [{ title: 'US Patent', similarity: 0.9 }] },
    ];
    const result = toRankedCandidates(
      [makeCandidate('a', { rank: 1, evidence_sources: evidenceSources })],
      [makeVerdict('a', 85)]
    );

    expect(result[0]!.evidenceSources).toEqual(evidenceSources);
  });

  it('returns empty array for empty input', () => {
    expect(toRankedCandidates([], [])).toHaveLength(0);
  });

  it('sorts candidate with undefined rank after candidates with defined ranks', () => {
    const candidates = [
      makeCandidate('no-rank', { rank: undefined }),
      makeCandidate('a', { rank: 2 }),
      makeCandidate('b', { rank: 1 }),
    ];
    const verdicts = [
      makeVerdict('no-rank', 95),
      makeVerdict('a', 50),
      makeVerdict('b', 60),
    ];

    const result = toRankedCandidates(candidates, verdicts);

    expect(result[0]!.id).toBe('b');
    expect(result[0]!.rank).toBe(1);
    expect(result[1]!.id).toBe('a');
    expect(result[1]!.rank).toBe(2);
    expect(result[2]!.id).toBe('no-rank');
    expect(result[2]!.rank).toBe(0);
    expect(result[2]!.patentability_score).toBe(95);
  });
});

function makeOpportunity(id: string, alignment: string): SeedingOpportunity {
  return { id, title: `Opp ${id}`, description: '', confidence_score: 80, roadmap_alignment: alignment };
}

describe('toCategorizedOpportunities', () => {
  it('maps category from roadmap_alignment', () => {
    const opps = [
      makeOpportunity('1', 'whitespace in sensor market'),
      makeOpportunity('2', 'defensive blocking strategy'),
    ];

    const result = toCategorizedOpportunities(opps);

    expect(result[0]!.category).toBe('whitespace');
    expect(result[1]!.category).toBe('defensive');
  });

  it('returns empty array for empty input', () => {
    expect(toCategorizedOpportunities([])).toHaveLength(0);
  });

  it('prefers the explicit server category over the roadmap heuristic', () => {
    const opp: SeedingOpportunity = {
      ...makeOpportunity('1', 'adjacent extension'),
      category: 'defensive',
    };
    const result = toCategorizedOpportunities([opp]);
    expect(result[0]!.category).toBe('defensive');
  });

  it('falls back to the roadmap heuristic when server category is unknown', () => {
    const opp: SeedingOpportunity = {
      ...makeOpportunity('1', 'whitespace gap'),
      category: 'not-a-real-category',
    };
    const result = toCategorizedOpportunities([opp]);
    expect(result[0]!.category).toBe('whitespace');
  });

  it('carries v2 fields through the store shape', () => {
    const opp: SeedingOpportunity = {
      ...makeOpportunity('1', 'roadmap'),
      novelty_delta: 'delta',
      mechanism: 'mech',
      claim_statement: 'claim',
      target_concept: 'concept',
      prior_art_proximity: [{ reference: 'US1', title: 'T', url: 'https://x', source: 'USPTO', relevance_score: 0.5, note: '' }],
      grounded_in: { chunk_ids: ['c1'], excerpts: [{ chunk_id: 'c1', section_label: 'S', text: 'txt' }] },
      axes: { Novelty: { score: 80, refs: ['E1'] } },
      citations: [{ ref: 'E1', source_type: 'patent_api', title: 'T', url: 'https://x', similarity: 0.5 }],
      source_status: { patent_api: 'active' },
    };
    const result = toCategorizedOpportunities([opp]);
    expect(result[0]!.noveltyDelta).toBe('delta');
    expect(result[0]!.claimStatement).toBe('claim');
    expect(result[0]!.groundedIn?.chunk_ids).toEqual(['c1']);
    expect(result[0]!.priorArtProximity).toHaveLength(1);
    expect(result[0]!.axes?.Novelty?.score).toBe(80);
  });

  it('stamps landscapeCorpusOnly from batch source flags', () => {
    const opps = [makeOpportunity('1', 'roadmap')];
    const corpusOnly = toCategorizedOpportunities(opps, {
      evidence_reachable: true,
      corpus_only: true,
      live_sources_ok: [],
      degraded_sources: [],
    });
    const live = toCategorizedOpportunities(opps, {
      evidence_reachable: true,
      corpus_only: false,
      live_sources_ok: ['USPTO'],
      degraded_sources: [],
    });
    expect(corpusOnly[0]!.landscapeCorpusOnly).toBe(true);
    expect(live[0]!.landscapeCorpusOnly).toBe(false);
  });

  it('treats degraded live sources as a corpus-only landscape', () => {
    const result = toCategorizedOpportunities([makeOpportunity('1', 'roadmap')], {
      evidence_reachable: true,
      corpus_only: false,
      live_sources_ok: [],
      degraded_sources: ['patent_api'],
    });
    expect(result[0]!.landscapeCorpusOnly).toBe(true);
  });

  it('carries evidence_sources through to evidenceSources (BUG183)', () => {
    const opp: SeedingOpportunity = {
      ...makeOpportunity('1', 'roadmap'),
      evidence_sources: [
        { source_type: 'llm_deep_research', available: true, status: 'active', reasoning: ['Novel.'], confidence: 0.6 },
      ],
    };
    const result = toCategorizedOpportunities([opp]);
    expect(result[0]!.evidenceSources).toEqual(opp.evidence_sources);
  });
});
