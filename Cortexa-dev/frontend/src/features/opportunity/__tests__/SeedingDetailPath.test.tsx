import React from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { OpportunityDetailPage } from '../OpportunityDetailPage';
import { toCategorizedOpportunities, toRankedCandidates } from '../../results/resultsMappers';
import { setResults, clearBatch } from '../resultsStore';
import type { CandidateDto, SeedingResultDto } from '../../../core/api/types';

vi.mock('../../../core/auth/useSession', () => ({
  useSession: () => ({ hasPermission: () => false }),
}));
vi.mock('../../export', () => ({
  useExport: () => ({ exportOpportunity: vi.fn().mockResolvedValue(undefined) }),
}));
vi.mock('../../../shared/ds/Toast', () => ({ useToast: () => ({ show: vi.fn() }) }));
vi.mock('../../../shared/layout/AppShell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

let params: { batchId: string; candidateId: string } = { batchId: 'batch-1', candidateId: 'opp-live' };
vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
  useParams: () => params,
}));

function v2SeedingResult(): SeedingResultDto {
  return {
    id: 'seed-1',
    batch_id: 'batch-1',
    created_at: '2026-07-12T00:00:00Z',
    engine: 'seeding',
    document_id: 'doc-1',
    explanation: '',
    is_empty: false,
    source_flags: { evidence_reachable: true, corpus_only: false, live_sources_ok: ['USPTO'], degraded_sources: [] },
    opportunities: [
      {
        id: 'opp-live',
        title: 'Adaptive Cache Eviction',
        description: 'A learned policy for evicting cache entries.',
        confidence_score: 82,
        roadmap_alignment: 'Aligns with the 2027 latency roadmap.',
        category: 'whitespace',
        target_concept: 'cache eviction',
        novelty_delta: 'Introduces a reinforcement signal absent from the source disclosure.',
        mechanism: 'RL agent scores entries.',
        claim_statement: 'A method comprising: observing access patterns; evicting by learned score.',
        grounded_in: {
          chunk_ids: ['chunk-aaa', 'chunk-bbb'],
          excerpts: [
            { chunk_id: 'chunk-aaa', section_label: 'Section 3.2 Eviction', text: 'The cache currently uses LRU eviction.' },
          ],
        },
        prior_art_proximity: [
          { reference: 'US1234567', title: 'Cache Management System', url: 'https://patents.example/US1234567', source: 'USPTO', relevance_score: 0.71, note: '' },
          { reference: 'CORPUS-9', title: 'Corpus Paper On Caching', url: '', source: 'corpus', relevance_score: 0.55, note: 'corpus match — no external link' },
        ],
        axes: {
          Novelty: { score: 88, refs: ['E1'] },
          Inventiveness: { score: 74, refs: [] },
        },
        citations: [
          { ref: 'E1', source_type: 'patent_api', title: 'Cache Management System', patent_id: 'US1234567', url: 'https://patents.example/US1234567', similarity: 0.71 },
        ],
        source_availability: { patent_api: true, vector_corpus: true, llm_deep_research: true },
        source_status: { patent_api: 'active', vector_corpus: 'active', llm_deep_research: 'active' },
        round_index: 1,
        candidate_id: 'cand-x',
        evidence_bundle_id: 'bundle-x',
        weighted_score: 82,
        innovation_rationale: 'Strong open space.',
      },
    ],
  };
}

function loadIntoStore(result: SeedingResultDto): void {
  const categorized = toCategorizedOpportunities(result.opportunities, result.source_flags);
  setResults('batch-1', [], categorized);
}

describe('Seeding opportunity detail — real store path (AC2)', () => {
  beforeEach(() => {
    params = { batchId: 'batch-1', candidateId: 'opp-live' };
  });
  afterEach(() => clearBatch('batch-1'));

  it('renders v2 fields end-to-end: novelty delta, grounded-in, prior art, category, axes', () => {
    loadIntoStore(v2SeedingResult());

    render(<OpportunityDetailPage />);

    // Novelty delta callout.
    expect(screen.getByText(/Introduces a reinforcement signal/)).toBeTruthy();
    expect(screen.getByLabelText('Novelty delta')).toBeTruthy();

    // Built from your document — section label shown, never the raw chunk id as primary text.
    expect(screen.getByText('Section 3.2 Eviction')).toBeTruthy();
    expect(screen.getByText(/currently uses LRU eviction/)).toBeTruthy();

    // Prior-art proximity — live link with external target, corpus honest text.
    const liveLink = screen.getByRole('link', { name: /Open US1234567 \(live patent match, relevance 71%, opens in new tab\)/ });
    expect(liveLink.getAttribute('href')).toBe('https://patents.example/US1234567');
    expect(liveLink.getAttribute('target')).toBe('_blank');
    expect(screen.getByText('corpus match — no external link')).toBeTruthy();

    // Category badge in header.
    expect(screen.getByText('Whitespace')).toBeTruthy();

    // Axis scores mapped from opportunity.axes.
    expect(screen.getByText('Novelty')).toBeTruthy();

    // Claim statement flows to the claim draft panel.
    expect(screen.getByText(/observing access patterns/)).toBeTruthy();

    // Roadmap alignment.
    expect(screen.getByText(/2027 latency roadmap/)).toBeTruthy();
  });

  it('does not render harvesting-only provenance for a seeding opportunity', () => {
    loadIntoStore(v2SeedingResult());
    render(<OpportunityDetailPage />);
    expect(screen.queryByText('Provenance')).toBeNull();
  });
});

describe('Seeding opportunity detail — legacy report (no v2 fields)', () => {
  beforeEach(() => {
    params = { batchId: 'batch-1', candidateId: 'opp-legacy' };
  });
  afterEach(() => clearBatch('batch-1'));

  it('renders the old branch with honest empties and no axis card', () => {
    const legacy: SeedingResultDto = {
      id: 'seed-legacy',
      batch_id: 'batch-1',
      created_at: '2026-07-12T00:00:00Z',
      opportunities: [
        { id: 'opp-legacy', title: 'Legacy Idea', description: 'An older seeding idea.', confidence_score: 50, roadmap_alignment: '' },
      ],
    };
    setResults('batch-1', [], toCategorizedOpportunities(legacy.opportunities));

    render(<OpportunityDetailPage />);

    expect(screen.getByText('No novelty delta was recorded for this opportunity.')).toBeTruthy();
    expect(screen.getByText('No source passages were recorded for this opportunity.')).toBeTruthy();
    expect(screen.getByText('No close prior art was found for this concept.')).toBeTruthy();
    expect(screen.getByText(/No roadmap was provided for this batch/)).toBeTruthy();
    // No axis rows for a legacy opportunity.
    expect(screen.queryByText('Axis Scores')).toBeNull();
  });
});

describe('Seeding opportunity detail — corpus-only prior art', () => {
  beforeEach(() => {
    params = { batchId: 'batch-1', candidateId: 'opp-degraded' };
  });
  afterEach(() => clearBatch('batch-1'));

  it('gives the corpus-only honest message when the landscape was corpus-only', () => {
    const result: SeedingResultDto = {
      id: 'seed-degraded',
      batch_id: 'batch-1',
      created_at: '2026-07-12T00:00:00Z',
      source_flags: { evidence_reachable: true, corpus_only: true, live_sources_ok: [], degraded_sources: ['patent_api'] },
      opportunities: [
        {
          id: 'opp-degraded',
          title: 'Degraded Idea',
          description: 'An idea validated corpus-only.',
          confidence_score: 60,
          roadmap_alignment: 'Roadmap note.',
          novelty_delta: 'Something new.',
          prior_art_proximity: [],
        },
      ],
    };
    setResults('batch-1', [], toCategorizedOpportunities(result.opportunities, result.source_flags));

    render(<OpportunityDetailPage />);

    expect(screen.getByText(/Prior-art landscape was corpus-only for this batch/)).toBeTruthy();
  });
});

describe('Harvesting opportunity detail — regression (unchanged)', () => {
  beforeEach(() => {
    params = { batchId: 'batch-1', candidateId: 'cand-1' };
  });
  afterEach(() => clearBatch('batch-1'));

  it('still renders the provenance section and no seeding sections', () => {
    const candidate: CandidateDto = {
      id: 'cand-1',
      title: 'Harvested Candidate',
      abstract: 'A harvested invention.',
      claim_draft: 'A method comprising: step A.',
      novelty_hypothesis: 'Novel.',
      source_asset_id: 'asset-1',
      batch_id: 'batch-1',
      created_at: '2026-07-12T00:00:00Z',
      rank: 1,
      weighted_score: 90,
      maturity: 'Mature',
      provenance_links: [
        { document_id: 'doc-1', locator: 'chars:10-40', source_kind: 'Pdf', excerpt: 'Source excerpt text.', page_number: 3 },
      ],
    };
    const ranked = toRankedCandidates([candidate], []);
    setResults('batch-1', ranked, []);

    render(<OpportunityDetailPage />);

    expect(screen.getByText('Provenance')).toBeTruthy();
    expect(within(document.body).queryByLabelText('Novelty delta')).toBeNull();
    expect(screen.queryByText('Closest prior art')).toBeNull();
    expect(screen.queryByText('Built from your document')).toBeNull();
  });
});
