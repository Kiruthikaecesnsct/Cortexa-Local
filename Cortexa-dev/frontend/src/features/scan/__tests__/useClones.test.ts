import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { RepositoryCloneDto } from '../scanTypes';

const listClones = vi.fn();
const startClone = vi.fn();
const downloadClone = vi.fn();
const downloadBlob = vi.fn();

vi.mock('../scanRepository', () => ({
  listClones: () => listClones(),
  startClone: (...args: unknown[]) => startClone(...args),
  downloadClone: (...args: unknown[]) => downloadClone(...args),
}));
vi.mock('../../export/downloadBlob', () => ({ downloadBlob: (...args: unknown[]) => downloadBlob(...args) }));

const { useClones } = await import('../useClones');

const POLL_MS = 3000;

function clone(status: RepositoryCloneDto['status']): RepositoryCloneDto {
  return {
    clone_id: 'c'.repeat(32),
    owner: 'acme',
    repository: 'api',
    branch: 'main',
    status,
    created_at: '2026-09-30T10:00:00Z',
    updated_at: '2026-09-30T10:00:00Z',
    size_bytes: status === 'stored' ? 5 : null,
    commit_sha: 'abcdef1',
    error: null,
  };
}

const listed = (...clones: RepositoryCloneDto[]) => ({ ok: true, data: { clones } });

async function flush() {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('useClones', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
  });
  afterEach(() => vi.useRealTimers());

  it('polls while a clone is in progress and stops once it is stored', async () => {
    listClones.mockResolvedValueOnce(listed(clone('cloning'))).mockResolvedValueOnce(listed(clone('stored')));
    const { result } = renderHook(() => useClones());
    await flush();
    expect(result.current.clones).toMatchObject({ status: 'loaded', data: [{ status: 'cloning' }] });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(POLL_MS);
    });
    expect(result.current.clones).toMatchObject({ status: 'loaded', data: [{ status: 'stored' }] });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(POLL_MS * 3);
    });
    expect(listClones).toHaveBeenCalledTimes(2);
  });

  it('does not poll when nothing is in progress', async () => {
    listClones.mockResolvedValue(listed(clone('stored')));
    renderHook(() => useClones());
    await flush();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(POLL_MS * 3);
    });
    expect(listClones).toHaveBeenCalledTimes(1);
  });

  it('refreshes after starting a clone and reports start errors', async () => {
    listClones.mockResolvedValue(listed());
    startClone.mockResolvedValueOnce({ ok: true, data: clone('queued') });
    startClone.mockResolvedValueOnce({ ok: false, error: { message: 'Repository too large' } });
    const { result } = renderHook(() => useClones());
    await flush();
    const credentials = { orgUrl: 'https://github.com/acme', pat: 'tok' };

    let outcome;
    await act(async () => {
      outcome = await result.current.start(credentials, 'api', 'main');
    });
    expect(outcome).toEqual({ ok: true });
    expect(startClone).toHaveBeenCalledWith(credentials, 'api', 'main');
    expect(listClones).toHaveBeenCalledTimes(2);

    await act(async () => {
      outcome = await result.current.start(credentials, 'api', 'main');
    });
    expect(outcome).toEqual({ ok: false, error: { message: 'Repository too large' } });
  });

  it('downloads the blob with a readable file name', async () => {
    listClones.mockResolvedValue(listed(clone('stored')));
    const blob = new Blob(['zip']);
    downloadClone.mockResolvedValue({ ok: true, data: blob });
    const { result } = renderHook(() => useClones());
    await flush();

    await act(async () => {
      await result.current.download(clone('stored'));
    });

    expect(downloadClone).toHaveBeenCalledWith(clone('stored'));
    expect(downloadBlob).toHaveBeenCalledWith(blob, 'api.zip');
    expect(result.current.downloading).toBeNull();
  });
});
