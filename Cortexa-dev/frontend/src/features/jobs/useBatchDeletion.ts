import { useState, useCallback, useRef } from 'react';
import type { ToastContextValue } from '../../shared/ds/Toast';
import { deleteBatch } from './jobsRepository';
import type { DeleteBatchResult } from './jobsRepository';

export type PerBatchDeleteState = 'pending' | 'success' | 'error';

export interface UseBatchDeletionResult {
  run: (batchIds: string[]) => Promise<void>;
  statuses: Record<string, PerBatchDeleteState>;
  isRunning: boolean;
  hasPermissionError: boolean;
}

interface UseBatchDeletionOptions {
  refetch: () => Promise<void>;
  onToast: ToastContextValue['show'];
}

interface DeletionCounts {
  successCount: number;
  partialCount: number;
  errorCount: number;
  forbiddenSeen: boolean;
  firstFailure?: { message: string; errorCode?: string };
  firstPartialStore?: string;
  sagaOnlyPartialCount: number;
}

type ToastPayload = Parameters<ToastContextValue['show']>[0];

const POOL_SIZE = 4;
const MAX_ERROR_MESSAGE_LENGTH = 200;
const GENERIC_FALLBACK = 'None of the selected batches could be deleted.';
const GENERIC_LOAD_ERROR = "Can't load batch progress right now.";
const GENERIC_NETWORK_ERROR = "Can't load batch progress right now. The pipeline status service didn't respond.";

function buildPendingStatuses(ids: string[]): Record<string, PerBatchDeleteState> {
  const init: Record<string, PerBatchDeleteState> = {};
  ids.forEach((id) => {
    init[id] = 'pending';
  });
  return init;
}

function isSagaOnlyPartial(stores: Array<{ store_name: string; success: boolean }>): boolean {
  const failedStores = stores.filter((s) => !s.success);
  return failedStores.length === 1 && failedStores[0]?.store_name === 'CosmosBatches';
}

function processBatchResult(
  batchId: string,
  result: DeleteBatchResult,
  counts: DeletionCounts,
  setStatuses: React.Dispatch<React.SetStateAction<Record<string, PerBatchDeleteState>>>
): void {
  if (result.ok) {
    if (result.data.fully_deleted) {
      counts.successCount++;
      setStatuses((prev) => ({ ...prev, [batchId]: 'success' }));
    } else {
      counts.partialCount++;
      if (isSagaOnlyPartial(result.data.stores)) {
        counts.sagaOnlyPartialCount++;
      }
      if (!counts.firstPartialStore) {
        const failedStore = result.data.stores.find((s) => !s.success);
        counts.firstPartialStore = failedStore?.store_name;
      }
      setStatuses((prev) => ({ ...prev, [batchId]: 'error' }));
    }
  } else {
    if (result.error.kind === 'forbidden') {
      counts.forbiddenSeen = true;
    } else if (!counts.firstFailure) {
      counts.firstFailure = {
        message: result.error.message,
        errorCode: result.error.errorCode,
      };
    }
    counts.errorCount++;
    setStatuses((prev) => ({ ...prev, [batchId]: 'error' }));
  }
}

function isGenericFallback(message: string): boolean {
  return message === GENERIC_LOAD_ERROR || message === GENERIC_NETWORK_ERROR;
}

function truncateMessage(raw: string): string {
  const sanitized = raw.trim().replace(/\n+/g, ' ');
  if (sanitized.length <= MAX_ERROR_MESSAGE_LENGTH) return sanitized;
  const truncated = sanitized.slice(0, MAX_ERROR_MESSAGE_LENGTH);
  const lastSpace = truncated.lastIndexOf(' ');
  return lastSpace > MAX_ERROR_MESSAGE_LENGTH * 0.7
    ? truncated.slice(0, lastSpace) + '…'
    : truncated + '…';
}

function formatErrorMessage(failure: { message: string; errorCode?: string }): string {
  const truncated = truncateMessage(failure.message);
  if (!failure.errorCode || failure.errorCode.trim() === '') return truncated;
  return `${truncated} (${failure.errorCode})`;
}

function buildPartialMessage(counts: DeletionCounts): string {
  const n = counts.partialCount;
  const subject = `${n} batch${n !== 1 ? 'es were' : ' was'}`;
  const store = counts.firstPartialStore;
  const where = store ? ` The ${store} store could not be purged.` : '';
  return `${subject} only partially deleted — some data still remains.${where} Retry, or contact your administrator if it persists.`;
}

function resolveDeletionToast(counts: DeletionCounts): ToastPayload {
  if (counts.forbiddenSeen) {
    return {
      variant: 'error',
      title: 'Permission denied',
      message: 'You do not have permission to delete batches. Contact your administrator for access.',
    };
  }
  const totalFailed = counts.partialCount + counts.errorCount;
  if (totalFailed === 0) {
    const n = counts.successCount;
    return {
      variant: 'success',
      title: 'Batches deleted',
      message: `${n} batch${n !== 1 ? 'es' : ''} permanently removed.`,
    };
  }
  if (counts.successCount === 0) {
    if (counts.errorCount === 0 && counts.partialCount > 0) {
      if (counts.sagaOnlyPartialCount === counts.partialCount) {
        return {
          variant: 'warning',
          title: 'Finishing up',
          message: 'The batch data was removed. Retry once to finish clearing it.',
        };
      }
      return {
        variant: 'warning',
        title: 'Batch not fully deleted',
        message: buildPartialMessage(counts),
      };
    }
    let errorMessage = GENERIC_FALLBACK;
    if (counts.firstFailure && !isGenericFallback(counts.firstFailure.message)) {
      errorMessage = formatErrorMessage(counts.firstFailure);
    }
    return {
      variant: 'error',
      title: 'Deletion failed',
      message: errorMessage,
    };
  }
  return {
    variant: 'warning',
    title: 'Cleanup finished with issues',
    message: `${counts.successCount} deleted · ${totalFailed} partially failed or errored.`,
  };
}

async function runWithPool(
  items: string[],
  poolSize: number,
  fn: (item: string) => Promise<void>
): Promise<void> {
  for (let i = 0; i < items.length; i += poolSize) {
    const chunk = items.slice(i, i + poolSize);
    await Promise.allSettled(chunk.map(fn));
  }
}

export function useBatchDeletion({
  refetch,
  onToast,
}: UseBatchDeletionOptions): UseBatchDeletionResult {
  const [statuses, setStatuses] = useState<Record<string, PerBatchDeleteState>>({});
  const [isRunning, setIsRunning] = useState(false);
  const [hasPermissionError, setHasPermissionError] = useState(false);
  const inFlightRef = useRef(false);

  const run = useCallback(
    async (batchIds: string[]) => {
      if (inFlightRef.current) return;
      if (batchIds.length === 0) return;
      inFlightRef.current = true;
      setIsRunning(true);
      setHasPermissionError(false);
      setStatuses(buildPendingStatuses(batchIds));

      const counts: DeletionCounts = {
        successCount: 0,
        partialCount: 0,
        errorCount: 0,
        forbiddenSeen: false,
        firstFailure: undefined,
        sagaOnlyPartialCount: 0,
      };

      await runWithPool(batchIds, POOL_SIZE, async (batchId) => {
        const result = await deleteBatch(batchId);
        processBatchResult(batchId, result, counts, setStatuses);
      });

      if (counts.forbiddenSeen) setHasPermissionError(true);
      onToast(resolveDeletionToast(counts));
      await refetch();
      setStatuses({});
      setIsRunning(false);
      inFlightRef.current = false;
    },
    [onToast, refetch]
  );

  return { run, statuses, isRunning, hasPermissionError };
}
