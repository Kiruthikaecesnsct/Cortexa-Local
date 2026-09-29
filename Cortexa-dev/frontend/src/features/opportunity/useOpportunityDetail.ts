import { useCallback, useEffect, useState } from 'react';
import { getCandidate, getOpportunity } from './resultsStore';
import { candidateToDetail, opportunityToDetail } from './opportunityMappers';
import { fetchCandidateDetail } from './opportunityRepository';
import type { OpportunityDetail, OpportunityDetailError } from './opportunityTypes';

interface UseOpportunityDetailResult {
  detail: OpportunityDetail | null;
  isLoading: boolean;
  error: OpportunityDetailError | null;
  notInResults: boolean;
  refetch: () => void;
}

function fromStore(candidateId: string): OpportunityDetail | null {
  const candidate = getCandidate(candidateId);
  if (candidate) return candidateToDetail(candidate);
  const opportunity = getOpportunity(candidateId);
  if (opportunity) return opportunityToDetail(opportunity);
  return null;
}

export function useOpportunityDetail(batchId: string, candidateId: string): UseOpportunityDetailResult {
  const [detail, setDetail] = useState<OpportunityDetail | null>(() => fromStore(candidateId));
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<OpportunityDetailError | null>(null);

  const load = useCallback(async () => {
    const cached = fromStore(candidateId);
    if (cached) {
      setDetail(cached);
      setError(null);
      return;
    }
    if (!batchId) {
      setError({ kind: 'not_found', message: 'Candidate not found.' });
      return;
    }
    setIsLoading(true);
    setError(null);
    const result = await fetchCandidateDetail(batchId, candidateId);
    if (result.ok) {
      setDetail(candidateToDetail(result.data));
    } else {
      setDetail(null);
      setError(result.error);
    }
    setIsLoading(false);
  }, [batchId, candidateId]);

  useEffect(() => {
    void load();
  }, [load]);

  return {
    detail,
    isLoading,
    error,
    notInResults: !isLoading && detail === null && error?.kind === 'not_found',
    refetch: () => void load(),
  };
}
