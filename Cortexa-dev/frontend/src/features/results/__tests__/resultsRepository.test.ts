import { describe, it, expect, vi, beforeEach } from 'vitest';

const mockGet = vi.hoisted(() => vi.fn());

vi.mock('../../../core/api/client', () => ({
  ApiClient: vi.fn().mockImplementation(() => ({
    get: mockGet,
  })),
}));

import { fetchHarvestingResults, fetchSeedingResults } from '../resultsRepository';

function axiosError(status: number, body: Record<string, unknown> = {}) {
  return { response: { status, data: body } };
}

describe('resultsRepository', () => {
  beforeEach(() => {
    mockGet.mockReset();
  });

  it('returns kind not_found on a 404 status with a not-found error_code', async () => {
    mockGet.mockRejectedValueOnce(
      axiosError(404, { error_code: 'HARVESTING_RESULT_NOT_FOUND', correlation_id: 'corr-1' })
    );

    const result = await fetchHarvestingResults('batch-1');

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('not_found');
      expect(result.error.errorCode).toBe('HARVESTING_RESULT_NOT_FOUND');
      expect(result.error.correlationId).toBe('corr-1');
    }
  });

  it('returns kind not_found on a bare 404 status with no error_code', async () => {
    mockGet.mockRejectedValueOnce(axiosError(404));

    const result = await fetchSeedingResults('batch-1');

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('not_found');
    }
  });

  it('returns kind network on a genuine 500 failure', async () => {
    mockGet.mockRejectedValueOnce(axiosError(500, { error_code: 'INTERNAL_ERROR', correlation_id: 'corr-2' }));

    const result = await fetchHarvestingResults('batch-1');

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('network');
      expect(result.error.correlationId).toBe('corr-2');
    }
  });

  it('returns kind network when the request throws without a response (true network failure)', async () => {
    mockGet.mockRejectedValueOnce(new Error('ECONNRESET'));

    const result = await fetchSeedingResults('batch-1');

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('network');
    }
  });

  it('returns ok data on success', async () => {
    const data = { id: 's-1', batch_id: 'batch-1', opportunities: [], created_at: '2024-01-01' };
    mockGet.mockResolvedValueOnce({ success: true, data, correlation_id: 'corr-3' });

    const result = await fetchSeedingResults('batch-1');

    expect(result).toEqual({ ok: true, data });
  });
});
