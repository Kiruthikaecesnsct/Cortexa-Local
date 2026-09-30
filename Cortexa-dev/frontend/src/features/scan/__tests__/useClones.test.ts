import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CloneApi } from '../cloneApi';
import type { RepositoryCloneDto } from '../scanTypes';

const downloadBlob = vi.fn();
vi.mock('../../export/downloadBlob', () => ({ downloadBlob: (...args: unknown[]) => downloadBlob(...args) }));

const { useClones } = await import('../useClones');

const POLL_MS = 3000;

function clone(status: RepositoryCloneDto['status'], repository = 'api'): RepositoryCloneDto {
  return {
    clone_id: 'c'.repeat(32),
    provider: 'github',
    owner: 'acme',
    repository,
    branch: 'main',
    status,
    created_at: '2026-09-30T10:00:00Z',
    updated_at: '2026-09-30T10:00:00Z',
    size_bytes: status === 'stored' ? 5 : null,
    commit_sha: 'abcdef1',
    error: null,
  };
}

const listed = (...clones: RepositoryCloneDto[]) => ({ ok: true as const, data: { clones } });

function fakeApi() {
  return { start: vi.fn(), list: vi.fn(), download: vi.fn() } satisfies Record<keyof CloneApi, unknown>;
}

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

  it('polls while a save is in progress and stops once it is stored', async () => {
    const api = fakeApi();
    api.list.mockResolvedValueOnce(listed(clone('cloning'))).mockResolvedValueOnce(listed(clone('stored')));
    const { result } = renderHook(() => useClones(api));
    await flush();
    expect(result.current.clones).toMatchObject({ status: 'loaded', data: [{ status: 'cloning' }] });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(POLL_MS);
    });
    expect(result.current.clones).toMatchObject({ status: 'loaded', data: [{ status: 'stored' }] });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(POLL_MS * 3);
    });
    expect(api.list).toHaveBeenCalledTimes(2);
  });

  it('does not poll when nothing is in progress', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue(listed(clone('stored')));
    renderHook(() => useClones(api));
    await flush();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(POLL_MS * 3);
    });
    expect(api.list).toHaveBeenCalledTimes(1);
  });

  it('refreshes after starting a save and reports start errors', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue(listed());
    api.start.mockResolvedValueOnce({ ok: true, data: clone('queued') });
    api.start.mockResolvedValueOnce({ ok: false, error: { message: 'Repository too large' } });
    const { result } = renderHook(() => useClones(api));
    await flush();
    const credentials = { orgUrl: 'https://github.com/acme', pat: 'tok' };

    let outcome;
    await act(async () => {
      outcome = await result.current.start(credentials, 'api', 'main');
    });
    expect(outcome).toEqual({ ok: true });
    expect(api.start).toHaveBeenCalledWith(credentials, 'api', 'main');
    expect(api.list).toHaveBeenCalledTimes(2);

    await act(async () => {
      outcome = await result.current.start(credentials, 'api', 'main');
    });
    expect(outcome).toEqual({ ok: false, error: { message: 'Repository too large' } });
  });

  it('downloads the blob named after the repository', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue(listed());
    const blob = new Blob(['zip']);
    api.download.mockResolvedValue({ ok: true, data: blob });
    const { result } = renderHook(() => useClones(api));
    await flush();
    const azureClone = { ...clone('stored', 'Platform/api'), provider: 'azure-devops' as const };

    await act(async () => {
      await result.current.download(azureClone);
    });

    expect(api.download).toHaveBeenCalledWith(azureClone);
    expect(downloadBlob).toHaveBeenCalledWith(blob, 'api.zip');
    expect(result.current.downloading).toBeNull();
  });
});
