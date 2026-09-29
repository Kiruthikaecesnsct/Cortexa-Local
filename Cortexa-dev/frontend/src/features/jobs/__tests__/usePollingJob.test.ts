import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { usePollingJob } from '../usePollingJob';
import type { JobStatusDto } from '../jobTypes';

const TestPollIntervalMs = vi.hoisted(() => 2000);

vi.mock('../../../core/config/env', () => ({
  jobPollIntervalMs: TestPollIntervalMs,
}));

vi.mock('../jobsRepository', () => ({
  fetchJobStatus: vi.fn(),
}));

import { fetchJobStatus } from '../jobsRepository';

const mockFetchJobStatus = fetchJobStatus as ReturnType<typeof vi.fn>;

const TestBatchId = 'batch-1';

function makeJob(status: JobStatusDto['status']): JobStatusDto {
  return {
    batch_id: TestBatchId,
    status,
    documents: [{ document_id: 'doc-1', filename: 'paper.pdf', status: 'Ingesting' }],
  };
}

describe('usePollingJob', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    Object.defineProperty(document, 'hidden', { value: false, configurable: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('polls immediately on mount and again after the configured interval', async () => {
    const ExpectedCallsAfterMount = 1;
    const ExpectedCallsAfterInterval = 2;
    mockFetchJobStatus.mockResolvedValue({ ok: true, data: makeJob('Running') });

    renderHook(() => usePollingJob(TestBatchId));
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAfterMount);

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs);
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAfterInterval);
  });

  it('stops polling once a terminal batch status is returned', async () => {
    const ExpectedCallsAtTerminal = 1;
    mockFetchJobStatus.mockResolvedValue({ ok: true, data: makeJob('Completed') });

    renderHook(() => usePollingJob(TestBatchId));
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAtTerminal);

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs * 5);
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAtTerminal);
  });

  it('cleans up the interval on unmount', async () => {
    const ExpectedCallsAfterMount = 1;
    mockFetchJobStatus.mockResolvedValue({ ok: true, data: makeJob('Running') });

    const { unmount } = renderHook(() => usePollingJob(TestBatchId));
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAfterMount);

    unmount();

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs * 3);
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAfterMount);
  });

  it('skips a poll tick while the previous request is still in-flight', async () => {
    const ExpectedCallsWhileInFlight = 1;
    const ExpectedCallsAfterResolution = 2;
    let resolveFirst!: (value: { ok: true; data: JobStatusDto }) => void;
    const firstCall = new Promise<{ ok: true; data: JobStatusDto }>((resolve) => {
      resolveFirst = resolve;
    });
    mockFetchJobStatus.mockReturnValueOnce(firstCall);
    mockFetchJobStatus.mockResolvedValue({ ok: true, data: makeJob('Running') });

    renderHook(() => usePollingJob(TestBatchId));
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsWhileInFlight);

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs);
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsWhileInFlight);

    await act(async () => {
      resolveFirst({ ok: true, data: makeJob('Running') });
      await Promise.resolve();
      await Promise.resolve();
    });

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs);
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAfterResolution);
  });

  it('pauses polling when the document becomes hidden and resumes when visible again', async () => {
    const ExpectedCallsAfterMount = 1;
    mockFetchJobStatus.mockResolvedValue({ ok: true, data: makeJob('Running') });

    renderHook(() => usePollingJob(TestBatchId));
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAfterMount);

    Object.defineProperty(document, 'hidden', { value: true, configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs * 3);
      await Promise.resolve();
    });

    expect(mockFetchJobStatus).toHaveBeenCalledTimes(ExpectedCallsAfterMount);

    Object.defineProperty(document, 'hidden', { value: false, configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs);
      await Promise.resolve();
    });

    expect(mockFetchJobStatus.mock.calls.length).toBeGreaterThan(ExpectedCallsAfterMount);
  });

  it('keeps last-known-good data and surfaces the error when a poll fails', async () => {
    const goodJob = makeJob('Running');
    mockFetchJobStatus.mockResolvedValueOnce({ ok: true, data: goodJob });
    mockFetchJobStatus.mockResolvedValueOnce({
      ok: false,
      error: { kind: 'server', message: 'boom' },
    });

    const { result } = renderHook(() => usePollingJob(TestBatchId));
    await act(async () => {
      await Promise.resolve();
    });

    expect(result.current.data).toEqual(goodJob);

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(result.current.error).toEqual({ kind: 'server', message: 'boom' });
    expect(result.current.data).toEqual(goodJob);
  });
});
