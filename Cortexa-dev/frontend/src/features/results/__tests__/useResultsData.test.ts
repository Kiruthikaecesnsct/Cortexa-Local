import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useResultsData, clearResultsCache } from '../useResultsData';
import type { JobStatusDto } from '../../jobs/jobTypes';
import type { HarvestingResultDto, SeedingResultDto } from '../../../core/api/types';

const TestPollIntervalMs = vi.hoisted(() => 2000);

vi.mock('../../../core/config/env', () => ({
  jobPollIntervalMs: TestPollIntervalMs,
}));

vi.mock('../../jobs/jobsRepository', () => ({
  fetchJobStatus: vi.fn(),
}));

vi.mock('../resultsRepository', () => ({
  fetchHarvestingResults: vi.fn(),
  fetchSeedingResults: vi.fn(),
}));

import { fetchJobStatus } from '../../jobs/jobsRepository';
import { fetchHarvestingResults, fetchSeedingResults } from '../resultsRepository';

const mockFetchJobStatus = fetchJobStatus as ReturnType<typeof vi.fn>;
const mockFetchHarvestingResults = fetchHarvestingResults as ReturnType<typeof vi.fn>;
const mockFetchSeedingResults = fetchSeedingResults as ReturnType<typeof vi.fn>;

const TestBatchId = 'batch-1';

function makeJob(overrides: Partial<JobStatusDto> = {}): JobStatusDto {
  return {
    batch_id: TestBatchId,
    status: 'Completed',
    documents: [],
    wants_harvesting: true,
    wants_seeding: true,
    ...overrides,
  };
}

function makeHarvesting(): HarvestingResultDto {
  return {
    id: 'h-1',
    batch_id: TestBatchId,
    candidates: [],
    verdicts: [],
    summary: 'summary',
    created_at: new Date().toISOString(),
  };
}

function makeSeeding(): SeedingResultDto {
  return {
    id: 's-1',
    batch_id: TestBatchId,
    opportunities: [],
    created_at: new Date().toISOString(),
  };
}

async function flush() {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

describe('useResultsData', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    clearResultsCache();
    Object.defineProperty(document, 'hidden', { value: false, configurable: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('loads harvesting-only data when seeding 404s and wants_seeding is false', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: true, wants_seeding: false }),
    });
    mockFetchHarvestingResults.mockResolvedValue({ ok: true, data: makeHarvesting() });
    mockFetchSeedingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'not_found', message: 'not found', errorCode: 'SEEDING_RESULT_NOT_FOUND' },
    });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(mockFetchSeedingResults).not.toHaveBeenCalled();
    expect(result.current.harvesting).not.toBeNull();
    expect(result.current.seeding).toBeNull();
    expect(result.current.error).toBeNull();
  });

  it('loads seeding-only data when harvesting 404s and wants_harvesting is false', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: false, wants_seeding: true }),
    });
    mockFetchHarvestingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'not_found', message: 'not found', errorCode: 'HARVESTING_RESULT_NOT_FOUND' },
    });
    mockFetchSeedingResults.mockResolvedValue({ ok: true, data: makeSeeding() });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(mockFetchHarvestingResults).not.toHaveBeenCalled();
    expect(result.current.seeding).not.toBeNull();
    expect(result.current.harvesting).toBeNull();
    expect(result.current.error).toBeNull();
  });

  it('loads both engines when both are wanted and both succeed', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: true, wants_seeding: true }),
    });
    mockFetchHarvestingResults.mockResolvedValue({ ok: true, data: makeHarvesting() });
    mockFetchSeedingResults.mockResolvedValue({ ok: true, data: makeSeeding() });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(result.current.harvesting).not.toBeNull();
    expect(result.current.seeding).not.toBeNull();
    expect(result.current.error).toBeNull();
  });

  it('errors the whole page when a required engine fails with a genuine server error', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: true, wants_seeding: true }),
    });
    mockFetchHarvestingResults.mockResolvedValue({ ok: true, data: makeHarvesting() });
    mockFetchSeedingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'server', message: 'boom' },
    });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(result.current.error).toEqual({ kind: 'server', message: 'boom' });
  });

  it('errors the whole page when a required engine fails with a network failure', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: true, wants_seeding: false }),
    });
    mockFetchHarvestingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'network', message: 'network down' },
    });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(result.current.error).toEqual({ kind: 'network', message: 'network down' });
  });

  it('falls back to at-least-one-engine behavior for legacy payloads missing both flags', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: undefined, wants_seeding: undefined }),
    });
    mockFetchHarvestingResults.mockResolvedValue({ ok: true, data: makeHarvesting() });
    mockFetchSeedingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'not_found', message: 'not found', errorCode: 'SEEDING_RESULT_NOT_FOUND' },
    });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(mockFetchHarvestingResults).toHaveBeenCalled();
    expect(mockFetchSeedingResults).toHaveBeenCalled();
    expect(result.current.harvesting).not.toBeNull();
    expect(result.current.seeding).toBeNull();
    expect(result.current.error).toBeNull();
  });

  it('errors on legacy payloads when one engine has a genuine server failure even though the other succeeds', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: undefined, wants_seeding: undefined }),
    });
    mockFetchHarvestingResults.mockResolvedValue({ ok: true, data: makeHarvesting() });
    mockFetchSeedingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'server', message: 'boom' },
    });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(result.current.error).toEqual({ kind: 'server', message: 'boom' });
    expect(result.current.harvesting).toBeNull();
    expect(result.current.seeding).toBeNull();
  });

  it('errors on legacy payloads when one engine has a network failure even though the other succeeds', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: undefined, wants_seeding: undefined }),
    });
    mockFetchHarvestingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'network', message: 'network down' },
    });
    mockFetchSeedingResults.mockResolvedValue({ ok: true, data: makeSeeding() });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(result.current.error).toEqual({ kind: 'network', message: 'network down' });
  });

  it('errors on legacy payloads when both engines are missing/failed', async () => {
    mockFetchJobStatus.mockResolvedValue({
      ok: true,
      data: makeJob({ wants_harvesting: undefined, wants_seeding: undefined }),
    });
    mockFetchHarvestingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'not_found', message: 'no harvesting', errorCode: 'HARVESTING_RESULT_NOT_FOUND' },
    });
    mockFetchSeedingResults.mockResolvedValue({
      ok: false,
      error: { kind: 'not_found', message: 'no seeding', errorCode: 'SEEDING_RESULT_NOT_FOUND' },
    });

    const { result } = renderHook(() => useResultsData(TestBatchId));
    await flush();

    expect(result.current.error).not.toBeNull();
    expect(result.current.harvesting).toBeNull();
    expect(result.current.seeding).toBeNull();
  });
});
