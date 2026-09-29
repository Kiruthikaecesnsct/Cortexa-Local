import type { BatchSummaryDto } from '../jobs/jobTypes';
import { formatDate, pluralizeDocuments } from '../../shared/utils';

export { formatDate, pluralizeDocuments };

export function routeForBatch(batch: BatchSummaryDto): string {
  return batch.status === 'Completed'
    ? `/batches/${batch.batch_id}/results`
    : `/batches/${batch.batch_id}`;
}

export interface DashboardStats {
  total: number;
  running: number;
  completed: number;
  failed: number;
}

export function computeDashboardStats(batches: BatchSummaryDto[]): DashboardStats {
  const total = batches.length;
  const running = batches.filter((b) => b.status === 'Running').length;
  const completed = batches.filter((b) => b.status === 'Completed').length;
  const failed = batches.filter((b) => b.status === 'Failed' || b.status === 'PartiallyFailed').length;
  return { total, running, completed, failed };
}
