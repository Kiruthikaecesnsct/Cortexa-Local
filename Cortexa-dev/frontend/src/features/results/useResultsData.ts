import { useCallback, useEffect, useReducer, useRef } from 'react';
import { jobPollIntervalMs } from '../../core/config/env';
import type { HarvestingResultDto, SeedingResultDto } from '../../core/api/types';
import type { JobStatusDto } from '../jobs/jobTypes';
import { fetchJobStatus } from '../jobs/jobsRepository';
import { isTerminalBatchStatus } from '../jobs/jobProgress';
import { fetchHarvestingResults, fetchSeedingResults } from './resultsRepository';
import type { ResultsError } from './resultsTypes';

interface CachedResults {
  harvesting: HarvestingResultDto | null;
  seeding: SeedingResultDto | null;
  batchStatus: JobStatusDto;
}

const resultsCache = new Map<string, CachedResults>();

export function clearResultsCache(): void {
  resultsCache.clear();
}

export interface ResultsState {
  harvesting: HarvestingResultDto | null;
  seeding: SeedingResultDto | null;
  batchStatus: JobStatusDto | null;
  isLoading: boolean;
  error: ResultsError | null;
}

type ResultsAction =
  | { type: 'POLL_START' }
  | { type: 'STATUS_UPDATE'; data: JobStatusDto }
  | { type: 'RESULTS_LOADED'; harvesting: HarvestingResultDto | null; seeding: SeedingResultDto | null }
  | { type: 'CACHE_RESTORE'; harvesting: HarvestingResultDto | null; seeding: SeedingResultDto | null; batchStatus: JobStatusDto }
  | { type: 'ERROR'; error: ResultsError };

const initialState: ResultsState = {
  harvesting: null,
  seeding: null,
  batchStatus: null,
  isLoading: true,
  error: null,
};

function reducer(state: ResultsState, action: ResultsAction): ResultsState {
  switch (action.type) {
    case 'POLL_START':
      return { ...state, isLoading: state.batchStatus === null };
    case 'STATUS_UPDATE':
      return { ...state, batchStatus: action.data, isLoading: true, error: null };
    case 'RESULTS_LOADED':
      return { ...state, harvesting: action.harvesting, seeding: action.seeding, isLoading: false, error: null };
    case 'CACHE_RESTORE':
      return {
        harvesting: action.harvesting,
        seeding: action.seeding,
        batchStatus: action.batchStatus,
        isLoading: false,
        error: null,
      };
    case 'ERROR':
      return { ...state, isLoading: false, error: action.error };
    default:
      return state;
  }
}

interface EngineRequirement {
  wantsHarvesting: boolean;
  wantsSeeding: boolean;
  legacyMode: boolean;
}

function resolveEngineRequirement(batchStatus: JobStatusDto): EngineRequirement {
  const { wants_harvesting, wants_seeding } = batchStatus;
  if (wants_harvesting === undefined && wants_seeding === undefined) {
    return { wantsHarvesting: true, wantsSeeding: true, legacyMode: true };
  }
  return { wantsHarvesting: wants_harvesting ?? false, wantsSeeding: wants_seeding ?? false, legacyMode: false };
}

const FALLBACK_RESULTS_ERROR: ResultsError = {
  kind: 'network',
  message: "Can't load results right now. The service didn't respond.",
};

interface LoadResultsContext {
  harvesting: HarvestingResultDto | null;
  seeding: SeedingResultDto | null;
  harvestingError: ResultsError | null;
  seedingError: ResultsError | null;
}

function handleLegacyResults(ctx: LoadResultsContext): ResultsAction | null {
  if (ctx.harvestingError && ctx.harvestingError.kind !== 'not_found') {
    return { type: 'ERROR', error: ctx.harvestingError };
  }
  if (ctx.seedingError && ctx.seedingError.kind !== 'not_found') {
    return { type: 'ERROR', error: ctx.seedingError };
  }
  if (!ctx.harvesting && !ctx.seeding) {
    return { type: 'ERROR', error: ctx.harvestingError ?? ctx.seedingError ?? FALLBACK_RESULTS_ERROR };
  }
  return { type: 'RESULTS_LOADED', harvesting: ctx.harvesting, seeding: ctx.seeding };
}

function handleModernResults(ctx: LoadResultsContext, wantsHarvesting: boolean, wantsSeeding: boolean): ResultsAction | null {
  if (wantsHarvesting && ctx.harvestingError) {
    return { type: 'ERROR', error: ctx.harvestingError };
  }
  if (wantsSeeding && ctx.seedingError) {
    return { type: 'ERROR', error: ctx.seedingError };
  }
  return { type: 'RESULTS_LOADED', harvesting: ctx.harvesting, seeding: ctx.seeding };
}

function useVisibilityPause(): () => boolean {
  const hiddenRef = useRef(document.hidden);

  useEffect(() => {
    function handle() {
      hiddenRef.current = document.hidden;
    }
    document.addEventListener('visibilitychange', handle);
    return () => document.removeEventListener('visibilitychange', handle);
  }, []);

  return useCallback(() => hiddenRef.current, []);
}

export function useResultsData(batchId: string): ResultsState {
  const [state, dispatch] = useReducer(
    reducer,
    batchId,
    (id) => {
      const c = resultsCache.get(id);
      return c
        ? {
            harvesting: c.harvesting,
            seeding: c.seeding,
            batchStatus: c.batchStatus,
            isLoading: false,
            error: null,
          }
        : initialState;
    }
  );
  const inFlightRef = useRef(false);
  const doneRef = useRef(resultsCache.has(batchId));
  const isHidden = useVisibilityPause();

  const loadResults = useCallback(
    async (batchStatus: JobStatusDto) => {
      const { wantsHarvesting, wantsSeeding, legacyMode } = resolveEngineRequirement(batchStatus);

      const [h, s] = await Promise.all([
        wantsHarvesting ? fetchHarvestingResults(batchId) : Promise.resolve(null),
        wantsSeeding ? fetchSeedingResults(batchId) : Promise.resolve(null),
      ]);

      const ctx: LoadResultsContext = {
        harvesting: h && h.ok ? h.data : null,
        seeding: s && s.ok ? s.data : null,
        harvestingError: h && !h.ok ? h.error : null,
        seedingError: s && !s.ok ? s.error : null,
      };

      const action = legacyMode
        ? handleLegacyResults(ctx)
        : handleModernResults(ctx, wantsHarvesting, wantsSeeding);

      if (!action) return;

      dispatch(action);

      if (action.type === 'RESULTS_LOADED') {
        resultsCache.set(batchId, { harvesting: ctx.harvesting, seeding: ctx.seeding, batchStatus });
      }
    },
    [batchId]
  );

  const poll = useCallback(async () => {
    if (inFlightRef.current || doneRef.current || isHidden()) return;

    inFlightRef.current = true;
    dispatch({ type: 'POLL_START' });

    const result = await fetchJobStatus(batchId);

    if (!result.ok) {
      dispatch({ type: 'ERROR', error: { kind: result.error.kind, message: result.error.message, correlationId: result.error.correlationId, errorCode: result.error.errorCode } });
      inFlightRef.current = false;
      return;
    }

    dispatch({ type: 'STATUS_UPDATE', data: result.data });

    if (isTerminalBatchStatus(result.data.status)) {
      doneRef.current = true;
      await loadResults(result.data);
    }

    inFlightRef.current = false;
  }, [batchId, isHidden, loadResults]);

  useEffect(() => {
    const cached = resultsCache.get(batchId);
    if (cached) {
      doneRef.current = true;
      dispatch({
        type: 'CACHE_RESTORE',
        harvesting: cached.harvesting,
        seeding: cached.seeding,
        batchStatus: cached.batchStatus,
      });
      return;
    }

    doneRef.current = false;
    void poll();

    const id = window.setInterval(() => {
      if (!doneRef.current) void poll();
    }, jobPollIntervalMs);

    return () => window.clearInterval(id);
  }, [poll, batchId]);

  return state;
}
