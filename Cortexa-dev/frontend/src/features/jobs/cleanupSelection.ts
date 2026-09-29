import type { BatchStatus, BatchSummaryDto } from './jobTypes';

const DELETABLE_STATUSES: ReadonlySet<BatchStatus> = new Set<BatchStatus>([
  'Completed',
  'Failed',
  'PartiallyFailed',
  'Cancelled',
]);

export function isDeletable(status: BatchStatus): boolean {
  return DELETABLE_STATUSES.has(status);
}

export function selectFailed(batches: BatchSummaryDto[]): string[] {
  return batches
    .filter((b) => b.status === 'Failed')
    .map((b) => b.batch_id);
}

export function selectIncompleteOlderThan(
  batches: BatchSummaryDto[],
  days: number,
  now: Date = new Date()
): string[] {
  const cutoffMs = now.getTime() - days * 24 * 60 * 60 * 1000;
  return batches
    .filter((b) => {
      const isIncomplete =
        b.status === 'Failed' || b.status === 'PartiallyFailed' || b.status === 'Cancelled';
      if (!isIncomplete) return false;
      return new Date(b.created_at).getTime() < cutoffMs;
    })
    .map((b) => b.batch_id);
}
