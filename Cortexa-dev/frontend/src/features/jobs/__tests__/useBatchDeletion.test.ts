import { renderHook, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useBatchDeletion } from '../useBatchDeletion';
import * as jobsRepository from '../jobsRepository';
import type { DeleteBatchResult } from '../jobsRepository';

vi.mock('../jobsRepository', () => ({
  deleteBatch: vi.fn(),
}));

describe('useBatchDeletion', () => {
  const mockRefetch = vi.fn(async () => {});
  const mockOnToast = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows accurate retryable message when only CosmosBatches store fails', async () => {
    const sagaOnlyPartialResult: DeleteBatchResult = {
      ok: true,
      data: {
        batch_id: 'batch-123',
        status: 'partial',
        fully_deleted: false,
        stores: [
          { store_name: 'CosmosData', deleted_count: 5, success: true },
          { store_name: 'BlobStorage', deleted_count: 2, success: true },
          { store_name: 'KeyVault', deleted_count: 1, success: true },
          { store_name: 'ServiceBus', deleted_count: 10, success: true },
          {
            store_name: 'CosmosBatches',
            deleted_count: 0,
            success: false,
            error: 'RequestTimeout',
          },
        ],
      },
    };

    vi.mocked(jobsRepository.deleteBatch).mockResolvedValueOnce(sagaOnlyPartialResult);

    const { result } = renderHook(() =>
      useBatchDeletion({ refetch: mockRefetch, onToast: mockOnToast })
    );

    result.current.run(['batch-123']);

    await waitFor(() => {
      expect(mockOnToast).toHaveBeenCalledWith({
        variant: 'warning',
        title: 'Finishing up',
        message: 'The batch data was removed. Retry once to finish clearing it.',
      });
    });
  });

  it('shows partial warning when real data stores fail', async () => {
    const realDataStoreFailureResult: DeleteBatchResult = {
      ok: true,
      data: {
        batch_id: 'batch-456',
        status: 'partial',
        fully_deleted: false,
        stores: [
          { store_name: 'CosmosData', deleted_count: 5, success: true },
          { store_name: 'BlobStorage', deleted_count: 2, success: false, error: 'AccessDenied' },
          { store_name: 'KeyVault', deleted_count: 0, success: false, error: 'Forbidden' },
          { store_name: 'ServiceBus', deleted_count: 10, success: true },
          { store_name: 'CosmosBatches', deleted_count: 1, success: true },
        ],
      },
    };

    vi.mocked(jobsRepository.deleteBatch).mockResolvedValueOnce(realDataStoreFailureResult);

    const { result } = renderHook(() =>
      useBatchDeletion({ refetch: mockRefetch, onToast: mockOnToast })
    );

    result.current.run(['batch-456']);

    await waitFor(() => {
      expect(mockOnToast).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: 'warning',
          title: 'Batch not fully deleted',
          message: expect.stringContaining('some data still remains'),
        })
      );
    });
  });

  it('shows success when fully deleted', async () => {
    const successResult: DeleteBatchResult = {
      ok: true,
      data: {
        batch_id: 'batch-789',
        status: 'deleted',
        fully_deleted: true,
        stores: [
          { store_name: 'CosmosData', deleted_count: 5, success: true },
          { store_name: 'BlobStorage', deleted_count: 2, success: true },
          { store_name: 'KeyVault', deleted_count: 1, success: true },
          { store_name: 'ServiceBus', deleted_count: 10, success: true },
          { store_name: 'CosmosBatches', deleted_count: 1, success: true },
        ],
      },
    };

    vi.mocked(jobsRepository.deleteBatch).mockResolvedValueOnce(successResult);

    const { result } = renderHook(() =>
      useBatchDeletion({ refetch: mockRefetch, onToast: mockOnToast })
    );

    result.current.run(['batch-789']);

    await waitFor(() => {
      expect(mockOnToast).toHaveBeenCalledWith({
        variant: 'success',
        title: 'Batches deleted',
        message: '1 batch permanently removed.',
      });
    });
  });

  it('shows permission denied for 403', async () => {
    const forbiddenResult: DeleteBatchResult = {
      ok: false,
      error: {
        kind: 'forbidden',
        message: 'You do not have permission to delete batches.',
      },
    };

    vi.mocked(jobsRepository.deleteBatch).mockResolvedValueOnce(forbiddenResult);

    const { result } = renderHook(() =>
      useBatchDeletion({ refetch: mockRefetch, onToast: mockOnToast })
    );

    result.current.run(['batch-999']);

    await waitFor(() => {
      expect(mockOnToast).toHaveBeenCalledWith({
        variant: 'error',
        title: 'Permission denied',
        message: 'You do not have permission to delete batches. Contact your administrator for access.',
      });
    });
  });

  it('sets isRunning true during deletion and false after completion', async () => {
    const successResult: DeleteBatchResult = {
      ok: true,
      data: {
        batch_id: 'batch-abc',
        status: 'deleted',
        fully_deleted: true,
        stores: [
          { store_name: 'CosmosData', deleted_count: 5, success: true },
          { store_name: 'BlobStorage', deleted_count: 2, success: true },
          { store_name: 'KeyVault', deleted_count: 1, success: true },
          { store_name: 'ServiceBus', deleted_count: 10, success: true },
          { store_name: 'CosmosBatches', deleted_count: 1, success: true },
        ],
      },
    };

    vi.mocked(jobsRepository.deleteBatch).mockImplementation(
      () => new Promise((resolve) => setTimeout(() => resolve(successResult), 100))
    );

    const { result } = renderHook(() =>
      useBatchDeletion({ refetch: mockRefetch, onToast: mockOnToast })
    );

    expect(result.current.isRunning).toBe(false);

    result.current.run(['batch-abc']);

    await waitFor(() => {
      expect(result.current.isRunning).toBe(true);
    });

    await waitFor(() => {
      expect(result.current.isRunning).toBe(false);
    });

    await waitFor(() => {
      expect(mockOnToast).toHaveBeenCalledWith({
        variant: 'success',
        title: 'Batches deleted',
        message: '1 batch permanently removed.',
      });
    });
  });
});
