import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type {
  PatentApiError,
  PatentConfigDto,
  PatentConfigRequest,
  PatentProbeResponseDto,
  PatentSecretRequest,
  PatentSourceId,
} from './patentApiTypes';

const PATENT_CONFIG_ENDPOINT = '/patent-config';

export type FetchPatentConfigResult = { ok: true; data: PatentConfigDto } | { ok: false; error: PatentApiError };
export type SavePatentConfigResult = { ok: true; data: PatentConfigDto } | { ok: false; error: PatentApiError };
export type WriteSecretResult = { ok: true; data: PatentConfigDto } | { ok: false; error: PatentApiError };
export type TestConnectionResult = { ok: true; data: PatentProbeResponseDto } | { ok: false; error: PatentApiError };

const client = new ApiClient(apiBaseUrl, getAccessToken);

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

function toApiError(raw: ApiResponse<unknown>): PatentApiError {
  return {
    message: raw.message ?? 'Unable to load patent source configuration.',
    correlationId: raw.correlation_id,
  };
}

function networkError(correlationId?: string): PatentApiError {
  return {
    message: 'Unable to connect to configuration service.',
    correlationId,
  };
}

export async function fetchPatentConfig(): Promise<FetchPatentConfigResult> {
  try {
    const response = await client.get<PatentConfigDto>(PATENT_CONFIG_ENDPOINT);
    if (!response.success || !response.data) {
      return { ok: false, error: toApiError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function savePatentConfig(request: PatentConfigRequest): Promise<SavePatentConfigResult> {
  try {
    const response = await client.put<PatentConfigDto>(PATENT_CONFIG_ENDPOINT, request);
    if (!response.success || !response.data) {
      return { ok: false, error: toApiError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function writePatentSecret(source: PatentSourceId, request: PatentSecretRequest): Promise<WriteSecretResult> {
  try {
    const response = await client.post<PatentConfigDto>(`${PATENT_CONFIG_ENDPOINT}/secrets/${source}`, request);
    if (!response.success || !response.data) {
      return { ok: false, error: toApiError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function testPatentConnection(source: PatentSourceId, request?: PatentSecretRequest): Promise<TestConnectionResult> {
  try {
    const response = await client.post<PatentProbeResponseDto>(`${PATENT_CONFIG_ENDPOINT}/secrets/${source}/test`, request ?? {});
    if (!response.success || !response.data) {
      return { ok: false, error: toApiError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}
