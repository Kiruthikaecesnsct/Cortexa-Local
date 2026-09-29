import type { CategorizedOpportunity, RankedCandidate } from '../results/resultsTypes';

interface ResultsStoreState {
  batchId: string;
  candidates: Map<string, RankedCandidate>;
  opportunities: Map<string, CategorizedOpportunity>;
}

let currentState: ResultsStoreState | null = null;

export function setResults(
  batchId: string,
  candidates: RankedCandidate[],
  opportunities: CategorizedOpportunity[]
): void {
  currentState = {
    batchId,
    candidates: new Map(candidates.map((c) => [c.id, c])),
    opportunities: new Map(opportunities.map((o) => [o.id, o])),
  };
}

export function getCandidate(candidateId: string): RankedCandidate | undefined {
  return currentState?.candidates.get(candidateId);
}

export function getOpportunity(opportunityId: string): CategorizedOpportunity | undefined {
  return currentState?.opportunities.get(opportunityId);
}

export function hasBatch(batchId: string): boolean {
  return currentState?.batchId === batchId;
}

export function clearBatch(batchId: string): void {
  if (currentState && currentState.batchId === batchId) {
    currentState = null;
  }
}
