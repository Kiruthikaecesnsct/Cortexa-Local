import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useAuthBootstrap } from './useAuthBootstrap';
import * as bootstrapSession from './bootstrapSession';

vi.mock('./bootstrapSession');

const mockedRehydrateSession = vi.mocked(bootstrapSession.rehydrateSession);

describe('useAuthBootstrap', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns ready true after successful rehydration', async () => {
    mockedRehydrateSession.mockResolvedValue(true);

    const { result } = renderHook(() => useAuthBootstrap());

    expect(result.current.ready).toBe(false);

    await waitFor(() => {
      expect(result.current.ready).toBe(true);
    });

    expect(mockedRehydrateSession).toHaveBeenCalledTimes(1);
  });

  it('returns ready true when rehydration returns false', async () => {
    mockedRehydrateSession.mockResolvedValue(false);

    const { result } = renderHook(() => useAuthBootstrap());

    expect(result.current.ready).toBe(false);

    await waitFor(() => {
      expect(result.current.ready).toBe(true);
    });

    expect(mockedRehydrateSession).toHaveBeenCalledTimes(1);
  });

  it('returns ready true when rehydration throws an error', async () => {
    mockedRehydrateSession.mockRejectedValue(new Error('Rehydration failed'));

    const { result } = renderHook(() => useAuthBootstrap());

    expect(result.current.ready).toBe(false);

    await waitFor(() => {
      expect(result.current.ready).toBe(true);
    });

    expect(mockedRehydrateSession).toHaveBeenCalledTimes(1);
  });

  it('calls rehydrateSession only once when rendered multiple times', async () => {
    mockedRehydrateSession.mockResolvedValue(true);

    const { result, rerender } = renderHook(() => useAuthBootstrap());

    expect(result.current.ready).toBe(false);

    rerender();

    await waitFor(() => {
      expect(result.current.ready).toBe(true);
    });

    expect(mockedRehydrateSession).toHaveBeenCalledTimes(1);
  });
});
