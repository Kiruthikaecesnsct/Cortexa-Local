import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { ScanError, ScanResult } from './scanTypes';

const NETWORK_ERROR_MESSAGE = 'Unable to reach the scan service.';

const client = new ApiClient(apiBaseUrl, getAccessToken);

/** Organization URL and personal access token; the same shape for every provider. */
export interface ScanCredentials {
  orgUrl: string;
  pat: string;
}

function parseEnvelope(data: unknown): ApiResponse<unknown> | undefined {
  // Blob downloads come back as an ArrayBuffer even when the server sends a JSON error.
  if (data instanceof ArrayBuffer) {
    try {
      return JSON.parse(new TextDecoder().decode(data)) as ApiResponse<unknown>;
    } catch {
      return undefined;
    }
  }
  return data !== null && typeof data === 'object' ? (data as ApiResponse<unknown>) : undefined;
}

function errorEnvelope(err: unknown): ApiResponse<unknown> | undefined {
  if (err === null || typeof err !== 'object' || !('response' in err)) return undefined;
  const response = (err as { response?: { data?: unknown } }).response;
  return parseEnvelope(response?.data);
}

function toScanError(raw: ApiResponse<unknown> | undefined, fallback: string): ScanError {
  return { message: raw?.message ?? fallback, correlationId: raw?.correlation_id };
}

function failedRequest(err: unknown, fallback: string): { ok: false; error: ScanError } {
  const envelope = errorEnvelope(err);
  return { ok: false, error: toScanError(envelope, envelope ? fallback : NETWORK_ERROR_MESSAGE) };
}

function unwrap<T>(response: ApiResponse<T>, fallback: string): ScanResult<T> {
  if (!response.success || !response.data) {
    return { ok: false, error: toScanError(response, fallback) };
  }
  return { ok: true, data: response.data };
}

export function credentialsBody(credentials: ScanCredentials): Record<string, string> {
  return { org_url: credentials.orgUrl.trim(), pat: credentials.pat.trim() };
}

export async function postScan<T>(path: string, body: Record<string, string>, fallback: string): Promise<ScanResult<T>> {
  try {
    return unwrap(await client.post<T>(path, body), fallback);
  } catch (err) {
    return failedRequest(err, fallback);
  }
}

export async function getScan<T>(path: string, fallback: string): Promise<ScanResult<T>> {
  try {
    return unwrap(await client.get<T>(path), fallback);
  } catch (err) {
    return failedRequest(err, fallback);
  }
}

export async function getScanBlob(path: string, fallback: string): Promise<ScanResult<Blob>> {
  try {
    return { ok: true, data: await client.getBlob(path) };
  } catch (err) {
    return failedRequest(err, fallback);
  }
}
