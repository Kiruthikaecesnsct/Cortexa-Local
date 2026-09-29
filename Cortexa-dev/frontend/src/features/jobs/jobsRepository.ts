import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { BatchStatus, BatchSummaryDto, DeleteBatchResultDto, JobError, JobStatusDto } from './jobTypes';

export const BATCH_LIST_ENDPOINT = '/batches';
export const JOB_STATUS_ENDPOINT = (batchId: string): string => `/batches/${batchId}/status`;
export const JOB_DOCUMENT_RETRY_ENDPOINT = (batchId: string, documentId: string): string =>
  `/batches/${batchId}/documents/${documentId}/retry`;
export const JOB_STOP_ENDPOINT = (batchId: string): string => `/batches/${batchId}/stop`;
export const JOB_DELETE_ENDPOINT = (batchId: string): string => `/batches/${batchId}`;

type FetchJobStatusResult = { ok: true; data: JobStatusDto } | { ok: false; error: JobError };
type FetchBatchListResult = { ok: true; data: BatchSummaryDto[] } | { ok: false; error: JobError };
type RetryDocumentResult = { ok: true } | { ok: false; error: JobError };
type StopBatchResult = { ok: true; data: { batch_id: string; status: BatchStatus } } | { ok: false; error: JobError };
export type DeleteBatchResult = { ok: true; data: DeleteBatchResultDto } | { ok: false; error: JobError };

const client = new ApiClient(apiBaseUrl, getAccessToken);

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

function extractCorrelationId(err: unknown): string | undefined {
  if (
    err !== null &&
    typeof err === 'object' &&
    'response' in err &&
    err.response !== null &&
    typeof err.response === 'object' &&
    'data' in err.response
  ) {
    const data = (err.response as { data?: ApiResponse<unknown> }).data;
    return data?.correlation_id;
  }
  return undefined;
}

function toJobError(raw: ApiResponse<unknown>): JobError {
  return {
    kind: 'server',
    message: raw.message ?? "Can't load batch progress right now.",
    correlationId: raw.correlation_id,
    errorCode: raw.error_code,
  };
}

function networkError(correlationId?: string): JobError {
  return {
    kind: 'network',
    message: "Can't load batch progress right now. The pipeline status service didn't respond.",
    correlationId,
  };
}

export async function fetchJobStatus(batchId: string): Promise<FetchJobStatusResult> {
  try {
    const response = await client.get<JobStatusDto>(JOB_STATUS_ENDPOINT(batchId));
    if (!response.success || !response.data) {
      const kind = response.error_code === 'BATCH_NOT_FOUND' ? 'not_found' : 'server';
      return { ok: false, error: { ...toJobError(response), kind } };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function fetchBatchList(): Promise<FetchBatchListResult> {
  try {
    const response = await client.get<BatchSummaryDto[]>(BATCH_LIST_ENDPOINT);
    if (!response.success || !response.data) {
      return { ok: false, error: toJobError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function retryDocument(batchId: string, documentId: string): Promise<RetryDocumentResult> {
  try {
    const response = await client.post<unknown>(JOB_DOCUMENT_RETRY_ENDPOINT(batchId, documentId), {});
    if (!response.success) {
      return { ok: false, error: toJobError(response) };
    }
    return { ok: true };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function deleteBatch(batchId: string): Promise<DeleteBatchResult> {
  try {
    const response = await client.delete<DeleteBatchResultDto>(JOB_DELETE_ENDPOINT(batchId));
    if (!response.success || !response.data) {
      const kind = response.error_code === 'BATCH_NOT_FOUND' ? 'not_found' : 'server';
      return { ok: false, error: { ...toJobError(response), kind } };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    if (extractStatusCode(err) === 403) {
      return {
        ok: false,
        error: {
          kind: 'forbidden',
          message: 'You do not have permission to delete batches.',
          correlationId: extractCorrelationId(err),
        },
      };
    }
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function stopBatch(batchId: string): Promise<StopBatchResult> {
  try {
    const response = await client.post<{ batch_id: string; status: BatchStatus }>(JOB_STOP_ENDPOINT(batchId), {});
    if (!response.success || !response.data) {
      return { ok: false, error: toJobError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}
