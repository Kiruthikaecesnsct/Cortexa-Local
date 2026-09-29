import { describe, it, expect, beforeEach } from 'vitest';
import { setResults, getCandidate, getOpportunity, clearBatch } from '../resultsStore';
import type { RankedCandidate, CategorizedOpportunity } from '../../results/resultsTypes';

const BatchAId = 'batch-a';
const BatchBId = 'batch-b';
const CandidateAId = 'cand-a1';
const OpportunityAId = 'opp-a1';
const CandidateBId = 'cand-b1';

function makeCandidate(overrides: Partial<RankedCandidate> = {}): RankedCandidate {
  return {
    id: CandidateAId,
    title: 'Adaptive Neural Compression',
    abstract: 'A method for compressing neural networks adaptively.',
    rank: 1,
    maturity: 'mature',
    patentability_score: 0.82,
    recommendation: 'pursue',
    rationale: 'Strong novelty.',
    source_asset_id: 'asset-1',
    batch_id: BatchAId,
    claimDraft: 'A method comprising: step A; step B.',
    noveltyHypothesis: 'No known prior art.',
    ...overrides,
  };
}

function makeOpportunity(overrides: Partial<CategorizedOpportunity> = {}): CategorizedOpportunity {
  return {
    id: OpportunityAId,
    title: 'Edge-deployed compression pipeline',
    description: 'A roadmap-derived opportunity.',
    confidence_score: 0.65,
    roadmap_alignment: 'whitespace expansion',
    category: 'whitespace',
    ...overrides,
  };
}

describe('resultsStore', () => {
  beforeEach(() => {
    clearBatch(BatchAId);
    clearBatch(BatchBId);
  });

  it('returns the stored candidate after setResults', () => {
    const candidate = makeCandidate();
    setResults(BatchAId, [candidate], []);

    expect(getCandidate(CandidateAId)).toEqual(candidate);
  });

  it('returns the stored opportunity after setResults', () => {
    const opportunity = makeOpportunity();
    setResults(BatchAId, [], [opportunity]);

    expect(getOpportunity(OpportunityAId)).toEqual(opportunity);
  });

  it('returns undefined for getCandidate when the id is unknown', () => {
    setResults(BatchAId, [makeCandidate()], []);

    expect(getCandidate('unknown-id')).toBeUndefined();
  });

  it('returns undefined for getOpportunity when the id is unknown', () => {
    setResults(BatchAId, [], [makeOpportunity()]);

    expect(getOpportunity('unknown-id')).toBeUndefined();
  });

  it('misses lookups for the old batch candidate id after switching to a new batchId', () => {
    setResults(BatchAId, [makeCandidate({ id: CandidateAId, batch_id: BatchAId })], []);
    setResults(BatchBId, [makeCandidate({ id: CandidateBId, batch_id: BatchBId })], []);

    expect(getCandidate(CandidateAId)).toBeUndefined();
    expect(getCandidate(CandidateBId)).toBeDefined();
  });

  it('clears the state when clearBatch is called with the matching batchId', () => {
    setResults(BatchAId, [makeCandidate()], [makeOpportunity()]);

    clearBatch(BatchAId);

    expect(getCandidate(CandidateAId)).toBeUndefined();
    expect(getOpportunity(OpportunityAId)).toBeUndefined();
  });

  it('leaves the state intact when clearBatch is called with a non-matching batchId', () => {
    setResults(BatchAId, [makeCandidate()], []);

    clearBatch(BatchBId);

    expect(getCandidate(CandidateAId)).toEqual(makeCandidate());
  });
});
