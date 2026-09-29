import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { HarvestingResultDto, SeedingResultDto } from '../../core/api/types';
import type { ResultsError } from './resultsTypes';

export const HARVESTING_RESULTS_ENDPOINT = (batchId: string): string =>
  `/harvesting/batches/${batchId}/results`;
export const SEEDING_RESULTS_ENDPOINT = (batchId: string): string =>
  `/seeding/batches/${batchId}/results`;

type FetchHarvestingResult = { ok: true; data: HarvestingResultDto } | { ok: false; error: ResultsError };
type FetchSeedingResult = { ok: true; data: SeedingResultDto } | { ok: false; error: ResultsError };

const client = new ApiClient(apiBaseUrl, getAccessToken);

const NOT_FOUND_ERROR_CODES = new Set([
  'SEEDING_RESULT_NOT_FOUND',
  'HARVESTING_RESULT_NOT_FOUND',
  'REPORT_NOT_FOUND',
]);

function isNotFoundErrorCode(errorCode: string | undefined): boolean {
  return errorCode !== undefined && NOT_FOUND_ERROR_CODES.has(errorCode);
}

function extractStatusCode(err: unknown): number | undefined {
  if (
    err !== null &&
    typeof err === 'object' &&
    'response' in err &&
    err.response !== null &&
    typeof err.response === 'object' &&
    'status' in err.response &&
    typeof (err.response as { status: unknown }).status === 'number'
  ) {
    return (err.response as { status: number }).status;
  }
  return undefined;
}

function extractResponseData(err: unknown): ApiResponse<unknown> | undefined {
  if (err !== null && typeof err === 'object' && 'response' in err) {
    const resp = (err as { response?: { data?: ApiResponse<unknown> } }).response;
    return resp?.data;
  }
  return undefined;
}

function notFoundError(message: string, correlationId?: string, errorCode?: string): ResultsError {
  return { kind: 'not_found', message, correlationId, errorCode };
}

function toResultsError(raw: ApiResponse<unknown>): ResultsError {
  if (isNotFoundErrorCode(raw.error_code)) {
    return notFoundError(raw.message ?? 'No results found for this engine.', raw.correlation_id, raw.error_code);
  }
  return {
    kind: 'server',
    message: raw.message ?? "Can't load results right now.",
    correlationId: raw.correlation_id,
    errorCode: raw.error_code,
  };
}

function networkError(correlationId?: string): ResultsError {
  return {
    kind: 'network',
    message: "Can't load results right now. The service didn't respond.",
    correlationId,
  };
}

function toCaughtError(err: unknown): ResultsError {
  const statusCode = extractStatusCode(err);
  const body = extractResponseData(err);
  const correlationId = body?.correlation_id;

  if (statusCode === 404 || isNotFoundErrorCode(body?.error_code)) {
    return notFoundError(body?.message ?? 'No results found for this engine.', correlationId, body?.error_code);
  }
  return networkError(correlationId);
}

export async function fetchHarvestingResults(batchId: string): Promise<FetchHarvestingResult> {
  try {
    const response = await client.get<HarvestingResultDto>(HARVESTING_RESULTS_ENDPOINT(batchId));
    if (!response.success || !response.data) {
      return { ok: false, error: toResultsError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: toCaughtError(err) };
  }
}

export async function fetchSeedingResults(batchId: string): Promise<FetchSeedingResult> {
  try {
    const response = await client.get<SeedingResultDto>(SEEDING_RESULTS_ENDPOINT(batchId));
    if (!response.success || !response.data) {
      return { ok: false, error: toResultsError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: toCaughtError(err) };
  }
}
