import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useUploadStore } from '../useUploadStore';

vi.mock('../uploadRepository', () => ({
  createBatch: vi.fn(),
}));

import { createBatch } from '../uploadRepository';

const mockCreateBatch = createBatch as ReturnType<typeof vi.fn>;

function makeFile(name: string, size: number, type: string): File {
  const blob = new Blob([new Uint8Array(Math.min(size, 1024))], { type });
  const file = new File([blob], name, { type });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('useUploadStore', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('initialises with idle status and no files', () => {
    const { result } = renderHook(() => useUploadStore());
    expect(result.current.status).toBe('idle');
    expect(result.current.files).toEqual([]);
    expect(result.current.canSubmit).toBe(false);
  });

  it('adds a valid file and marks it valid', () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
    });

    expect(result.current.files).toHaveLength(1);
    expect(result.current.files[0]?.valid).toBe(true);
    expect(result.current.zoneError).toBeUndefined();
  });

  it('adds an invalid file and sets a zone error', () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.addFiles([makeFile('dataset.csv', 1024, 'text/csv')]);
    });

    expect(result.current.files[0]?.valid).toBe(false);
    expect(result.current.zoneError).toMatch(/1 file/i);
  });

  it('removes a file by id', () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
    });
    const id = result.current.files[0]?.id as string;

    act(() => {
      result.current.removeFile(id);
    });

    expect(result.current.files).toHaveLength(0);
  });

  it('blocks submit and sets field errors when batch name is non-empty but too short', async () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setBatchName('ab');
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.fieldErrors.batchName).toBeTruthy();
    expect(result.current.status).toBe('idle');
    expect(mockCreateBatch).not.toHaveBeenCalled();
  });

  it('blocks submit when no valid files are selected', async () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setBatchName('Valid batch name');
      result.current.addFiles([makeFile('dataset.csv', 1024, 'text/csv')]);
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(mockCreateBatch).not.toHaveBeenCalled();
    expect(result.current.status).toBe('idle');
  });

  it('dispatches success on a valid submit', async () => {
    mockCreateBatch.mockResolvedValueOnce({
      ok: true,
      data: { batch_id: 'b1', document_count: 1, status: 'started' },
    });

    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setBatchName('Valid batch name');
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.status).toBe('success');
    expect(result.current.successData?.batch_id).toBe('b1');
  });

  it('dispatches error on a failed submit', async () => {
    mockCreateBatch.mockResolvedValueOnce({
      ok: false,
      error: { kind: 'server', message: 'failed', correlationId: 'corr-1', errorCode: 'BATCH_CREATE_FAILED' },
    });

    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setBatchName('Valid batch name');
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.status).toBe('error');
    expect(result.current.submitError?.correlationId).toBe('corr-1');
  });

  it('clears submit error on clearSubmitError', async () => {
    mockCreateBatch.mockResolvedValueOnce({
      ok: false,
      error: { kind: 'server', message: 'failed' },
    });

    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setBatchName('Valid batch name');
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.status).toBe('error');

    act(() => {
      result.current.clearSubmitError();
    });

    expect(result.current.submitError).toBeNull();
  });

  it('clears batch name field error when batch name is updated', async () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setBatchName('ab');
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.fieldErrors.batchName).toBeTruthy();

    act(() => {
      result.current.setBatchName('Valid batch name');
    });

    expect(result.current.fieldErrors.batchName).toBeUndefined();
  });

  it('enables submit with a valid file and a valid batch name', () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
      result.current.setBatchName('Valid batch name');
    });

    expect(result.current.canSubmit).toBe(true);
  });

  it('completely removes batchName key from fieldErrors when a valid name is typed', () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setBatchName('Valid batch name');
    });

    expect('batchName' in result.current.fieldErrors).toBe(false);
  });

  it('enables submit with a connected repo and zero files', () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setGitParams({
        repoUrl: 'https://github.com/org/repo',
        provider: 'github',
      });
    });

    expect(result.current.files).toHaveLength(0);
    expect(result.current.gitParams).toBeTruthy();
    expect(result.current.canSubmit).toBe(true);
  });

  it('enables submit with both files and a connected repo', () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.addFiles([makeFile('paper.pdf', 1024, 'application/pdf')]);
      result.current.setGitParams({
        repoUrl: 'https://github.com/org/repo',
        provider: 'github',
      });
    });

    expect(result.current.files).toHaveLength(1);
    expect(result.current.gitParams).toBeTruthy();
    expect(result.current.canSubmit).toBe(true);
  });

  it('blocks submit when repo is connected but a file is rejected', () => {
    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.addFiles([makeFile('bad.csv', 1024, 'text/csv')]);
      result.current.setGitParams({
        repoUrl: 'https://github.com/org/repo',
        provider: 'github',
      });
    });

    expect(result.current.gitParams).toBeTruthy();
    expect(result.current.files[0]?.valid).toBe(false);
    expect(result.current.canSubmit).toBe(false);
  });

  it('submits successfully with repo only and no files', async () => {
    mockCreateBatch.mockResolvedValueOnce({
      ok: true,
      data: { batch_id: 'b2', document_count: 0, status: 'started' },
    });

    const { result } = renderHook(() => useUploadStore());

    act(() => {
      result.current.setGitParams({
        repoUrl: 'https://github.com/org/repo',
        provider: 'github',
        pat: 'token123',
        branch: 'main',
      });
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.status).toBe('success');
    expect(result.current.successData?.batch_id).toBe('b2');
    expect(mockCreateBatch).toHaveBeenCalledWith(
      expect.objectContaining({
        files: [],
        gitParams: expect.objectContaining({
          repoUrl: 'https://github.com/org/repo',
          provider: 'github',
        }),
      })
    );
  });
});
