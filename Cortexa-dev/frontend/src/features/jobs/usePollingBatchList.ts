import { useCallback, useEffect, useReducer, useRef } from 'react';
import { batchListPollIntervalMs } from '../../core/config/env';
import { fetchBatchList } from './jobsRepository';
import type { BatchSummaryDto, JobError } from './jobTypes';

interface PollingBatchListState {
  data: BatchSummaryDto[] | null;
  isLoading: boolean;
  error: JobError | null;
}

type PollingAction =
  | { type: 'FETCH_START' }
  | { type: 'FETCH_SUCCESS'; data: BatchSummaryDto[] }
  | { type: 'FETCH_ERROR'; error: JobError };

const initialState: PollingBatchListState = {
  data: null,
  isLoading: true,
  error: null,
};

function pollingReducer(state: PollingBatchListState, action: PollingAction): PollingBatchListState {
  switch (action.type) {
    case 'FETCH_START':
      return { ...state, isLoading: state.data === null };
    case 'FETCH_SUCCESS':
      return { ...state, data: action.data, isLoading: false, error: null };
    case 'FETCH_ERROR':
      return { ...state, isLoading: false, error: action.error };
    default:
      return state;
  }
}

function useVisibilityPause(): () => boolean {
  const hiddenRef = useRef(document.hidden);

  useEffect(() => {
    function handleVisibilityChange() {
      hiddenRef.current = document.hidden;
    }
    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, []);

  return useCallback(() => hiddenRef.current, []);
}

export function usePollingBatchList(): {
  data: BatchSummaryDto[] | null;
  isLoading: boolean;
  error: JobError | null;
  refetch: () => Promise<void>;
} {
  const [state, dispatch] = useReducer(pollingReducer, initialState);
  const inFlightRef = useRef(false);
  const isHidden = useVisibilityPause();

  const poll = useCallback(async () => {
    if (inFlightRef.current || isHidden()) {
      return;
    }
    inFlightRef.current = true;
    dispatch({ type: 'FETCH_START' });

    const result = await fetchBatchList();

    if (result.ok) {
      dispatch({ type: 'FETCH_SUCCESS', data: result.data });
    } else {
      dispatch({ type: 'FETCH_ERROR', error: result.error });
    }

    inFlightRef.current = false;
  }, [isHidden]);

  const refetch = useCallback(async () => {
    inFlightRef.current = false;
    await poll();
  }, [poll]);

  useEffect(() => {
    void poll();

    const intervalId = window.setInterval(() => {
      void poll();
    }, batchListPollIntervalMs);

    return () => window.clearInterval(intervalId);
  }, [poll]);

  return { ...state, refetch };
}
