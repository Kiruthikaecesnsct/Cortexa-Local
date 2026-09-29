import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { usePollingBatchList } from '../usePollingBatchList';
import type { BatchSummaryDto } from '../jobTypes';

const TestPollIntervalMs = vi.hoisted(() => 3000);

vi.mock('../../../core/config/env', () => ({
  batchListPollIntervalMs: TestPollIntervalMs,
}));

vi.mock('../jobsRepository', () => ({
  fetchBatchList: vi.fn(),
}));

import { fetchBatchList } from '../jobsRepository';

const mockFetchBatchList = fetchBatchList as ReturnType<typeof vi.fn>;

const TestBatchList: BatchSummaryDto[] = [
  {
    batch_id: 'batch-1',
    status: 'Running',
    document_count: 5,
    completed_count: 2,
    created_at: '2024-01-01T00:00:00Z',
  },
];

describe('usePollingBatchList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    Object.defineProperty(document, 'hidden', { value: false, configurable: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('calls fetchBatchList immediately on mount', async () => {
    const ExpectedCallsOnMount = 1;
    mockFetchBatchList.mockResolvedValue({ ok: true, data: TestBatchList });

    renderHook(() => usePollingBatchList());
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockFetchBatchList).toHaveBeenCalledTimes(ExpectedCallsOnMount);
  });

  it('sets data and clears isLoading and error after a successful fetch', async () => {
    mockFetchBatchList.mockResolvedValue({ ok: true, data: TestBatchList });

    const { result } = renderHook(() => usePollingBatchList());
    await act(async () => {
      await Promise.resolve();
    });

    expect(result.current.data).toEqual(TestBatchList);
    expect(result.current.isLoading).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it('sets error and clears isLoading and data remains null after a failed fetch', async () => {
    const TestError = { kind: 'network' as const, message: 'no connection' };
    mockFetchBatchList.mockResolvedValue({ ok: false, error: TestError });

    const { result } = renderHook(() => usePollingBatchList());
    await act(async () => {
      await Promise.resolve();
    });

    expect(result.current.error).toEqual(TestError);
    expect(result.current.isLoading).toBe(false);
    expect(result.current.data).toBeNull();
  });

  it('calls fetchBatchList a second time after batchListPollIntervalMs elapses', async () => {
    const ExpectedCallsAfterMount = 1;
    const ExpectedCallsAfterInterval = 2;
    mockFetchBatchList.mockResolvedValue({ ok: true, data: TestBatchList });

    renderHook(() => usePollingBatchList());
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockFetchBatchList).toHaveBeenCalledTimes(ExpectedCallsAfterMount);

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs);
      await Promise.resolve();
    });

    expect(mockFetchBatchList).toHaveBeenCalledTimes(ExpectedCallsAfterInterval);
  });

  it('does not call fetchBatchList again after unmount when timers advance', async () => {
    const ExpectedCallsAfterMount = 1;
    mockFetchBatchList.mockResolvedValue({ ok: true, data: TestBatchList });

    const { unmount } = renderHook(() => usePollingBatchList());
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockFetchBatchList).toHaveBeenCalledTimes(ExpectedCallsAfterMount);

    unmount();

    await act(async () => {
      vi.advanceTimersByTime(TestPollIntervalMs * 3);
      await Promise.resolve();
    });

    expect(mockFetchBatchList).toHaveBeenCalledTimes(ExpectedCallsAfterMount);
  });
});
