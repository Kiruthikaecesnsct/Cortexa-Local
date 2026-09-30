import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { DirectoryListingDto, LocalSystemCredentials, ScanError, ScanResult } from './scanTypes';

const LIST_ENDPOINT = '/scan/local-system/list';
const NETWORK_ERROR_MESSAGE = 'Unable to reach the scan service.';
const DEFAULT_ERROR_MESSAGE = 'Local file system scan failed.';

const client = new ApiClient(apiBaseUrl, getAccessToken);

function errorEnvelope(err: unknown): ApiResponse<unknown> | undefined {
  if (err === null || typeof err !== 'object' || !('response' in err)) return undefined;
  const response = (err as { response?: { data?: unknown } }).response;
  const data = response?.data;
  return data !== null && typeof data === 'object' ? (data as ApiResponse<unknown>) : undefined;
}

function toScanError(raw: ApiResponse<unknown> | undefined, fallback: string): ScanError {
  return { message: raw?.message ?? fallback, correlationId: raw?.correlation_id };
}

export async function listDirectory(
  credentials: LocalSystemCredentials,
  path: string
): Promise<ScanResult<DirectoryListingDto>> {
  const body = {
    host: credentials.host.trim(),
    port: credentials.port,
    username: credentials.username.trim(),
    private_key: credentials.privateKey.trim(),
    passphrase: credentials.passphrase?.trim() || undefined,
    path: path.trim(),
  };
  try {
    const response = await client.post<DirectoryListingDto>(LIST_ENDPOINT, body);
    if (!response.success || !response.data) {
      return { ok: false, error: toScanError(response, DEFAULT_ERROR_MESSAGE) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    const envelope = errorEnvelope(err);
    return { ok: false, error: toScanError(envelope, envelope ? DEFAULT_ERROR_MESSAGE : NETWORK_ERROR_MESSAGE) };
  }
}
