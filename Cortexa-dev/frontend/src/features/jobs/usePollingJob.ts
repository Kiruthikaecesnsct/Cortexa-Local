import { useCallback, useEffect, useReducer, useRef } from 'react';
import { jobPollIntervalMs } from '../../core/config/env';
import { fetchJobStatus } from './jobsRepository';
import { isTerminalBatchStatus } from './jobProgress';
import type { JobError, JobStatusDto } from './jobTypes';

export interface PollingJobState {
  data: JobStatusDto | null;
  isLoading: boolean;
  error: JobError | null;
  isPolling: boolean;
}

type PollingAction =
  | { type: 'FETCH_START' }
  | { type: 'FETCH_SUCCESS'; data: JobStatusDto }
  | { type: 'FETCH_ERROR'; error: JobError }
  | { type: 'STOP_POLLING' };

const initialState: PollingJobState = {
  data: null,
  isLoading: true,
  error: null,
  isPolling: true,
};

function pollingReducer(state: PollingJobState, action: PollingAction): PollingJobState {
  switch (action.type) {
    case 'FETCH_START':
      return { ...state, isLoading: state.data === null };
    case 'FETCH_SUCCESS':
      return { ...state, data: action.data, isLoading: false, error: null };
    case 'FETCH_ERROR':
      return { ...state, isLoading: false, error: action.error };
    case 'STOP_POLLING':
      return { ...state, isPolling: false };
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

export function usePollingJob(batchId: string): PollingJobState {
  const [state, dispatch] = useReducer(pollingReducer, initialState);
  const inFlightRef = useRef(false);
  const stoppedRef = useRef(false);
  const isHidden = useVisibilityPause();

  const poll = useCallback(async () => {
    if (inFlightRef.current || stoppedRef.current || isHidden()) {
      return;
    }
    inFlightRef.current = true;
    dispatch({ type: 'FETCH_START' });

    const result = await fetchJobStatus(batchId);

    if (result.ok) {
      dispatch({ type: 'FETCH_SUCCESS', data: result.data });
      if (isTerminalBatchStatus(result.data.status)) {
        stoppedRef.current = true;
        dispatch({ type: 'STOP_POLLING' });
      }
    } else {
      dispatch({ type: 'FETCH_ERROR', error: result.error });
    }

    inFlightRef.current = false;
  }, [batchId, isHidden]);

  useEffect(() => {
    stoppedRef.current = false;
    void poll();

    const intervalId = window.setInterval(() => {
      if (!stoppedRef.current) {
        void poll();
      }
    }, jobPollIntervalMs);

    return () => window.clearInterval(intervalId);
  }, [poll]);

  return state;
}
