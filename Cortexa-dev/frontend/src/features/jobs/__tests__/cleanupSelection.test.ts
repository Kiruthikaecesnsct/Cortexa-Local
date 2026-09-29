import { describe, it, expect } from 'vitest';
import type { BatchSummaryDto, BatchStatus } from '../jobTypes';
import { isDeletable, selectFailed, selectIncompleteOlderThan } from '../cleanupSelection';

function makeBatch(overrides: Partial<BatchSummaryDto> & { status: BatchStatus }): BatchSummaryDto {
  return {
    batch_id: 'batch-1',
    document_count: 3,
    completed_count: 3,
    created_at: '2024-01-01T00:00:00Z',
    ...overrides,
  };
}

const NOW = new Date('2024-03-01T00:00:00Z');
const DAYS = 7;

describe('isDeletable', () => {
  const deletableStatuses: BatchStatus[] = ['Completed', 'Failed', 'PartiallyFailed', 'Cancelled'];
  const nonDeletableStatuses: BatchStatus[] = ['Running'];

  deletableStatuses.forEach((status) => {
    it(`returns true for ${status}`, () => {
      expect(isDeletable(status)).toBe(true);
    });
  });

  nonDeletableStatuses.forEach((status) => {
    it(`returns false for ${status}`, () => {
      expect(isDeletable(status)).toBe(false);
    });
  });
});

describe('selectFailed', () => {
  it('returns ids of Failed batches only', () => {
    const batches: BatchSummaryDto[] = [
      makeBatch({ batch_id: 'b1', status: 'Failed' }),
      makeBatch({ batch_id: 'b2', status: 'Completed' }),
      makeBatch({ batch_id: 'b3', status: 'Failed' }),
      makeBatch({ batch_id: 'b4', status: 'PartiallyFailed' }),
    ];
    expect(selectFailed(batches)).toEqual(['b1', 'b3']);
  });

  it('returns empty array when no Failed batches', () => {
    const batches: BatchSummaryDto[] = [
      makeBatch({ batch_id: 'b1', status: 'Running' }),
      makeBatch({ batch_id: 'b2', status: 'Completed' }),
    ];
    expect(selectFailed(batches)).toEqual([]);
  });
});

describe('selectIncompleteOlderThan', () => {
  it('includes Failed batches older than cutoff', () => {
    const batches: BatchSummaryDto[] = [
      makeBatch({ batch_id: 'old-failed', status: 'Failed', created_at: '2024-02-01T00:00:00Z' }),
    ];
    const result = selectIncompleteOlderThan(batches, DAYS, NOW);
    expect(result).toContain('old-failed');
  });

  it('includes PartiallyFailed batches older than cutoff', () => {
    const batches: BatchSummaryDto[] = [
      makeBatch({
        batch_id: 'old-partial',
        status: 'PartiallyFailed',
        created_at: '2024-02-01T00:00:00Z',
      }),
    ];
    const result = selectIncompleteOlderThan(batches, DAYS, NOW);
    expect(result).toContain('old-partial');
  });

  it('includes Cancelled batches older than cutoff', () => {
    const batches: BatchSummaryDto[] = [
      makeBatch({
        batch_id: 'old-cancelled',
        status: 'Cancelled',
        created_at: '2024-02-01T00:00:00Z',
      }),
    ];
    const result = selectIncompleteOlderThan(batches, DAYS, NOW);
    expect(result).toContain('old-cancelled');
  });

  it('excludes incomplete batches within retention window', () => {
    const batches: BatchSummaryDto[] = [
      makeBatch({ batch_id: 'recent', status: 'Failed', created_at: '2024-02-28T00:00:00Z' }),
    ];
    const result = selectIncompleteOlderThan(batches, DAYS, NOW);
    expect(result).not.toContain('recent');
  });

  it('excludes Running batches even if old', () => {
    const batches: BatchSummaryDto[] = [
      makeBatch({ batch_id: 'old-running', status: 'Running', created_at: '2024-01-01T00:00:00Z' }),
    ];
    const result = selectIncompleteOlderThan(batches, DAYS, NOW);
    expect(result).not.toContain('old-running');
  });

  it('excludes Completed batches even if old', () => {
    const batches: BatchSummaryDto[] = [
      makeBatch({ batch_id: 'old-done', status: 'Completed', created_at: '2024-01-01T00:00:00Z' }),
    ];
    const result = selectIncompleteOlderThan(batches, DAYS, NOW);
    expect(result).not.toContain('old-done');
  });

  it('returns empty array when no batches match', () => {
    expect(selectIncompleteOlderThan([], DAYS, NOW)).toEqual([]);
  });
});
