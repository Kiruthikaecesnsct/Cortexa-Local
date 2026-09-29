import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { ModelConfigDto, ModelConfigRequest } from './settingsTypes';

const CONFIG_ENDPOINT = '/config';

export type FetchConfigResult = { ok: true; data: ModelConfigDto } | { ok: false; error: ConfigError };
export type SaveConfigResult = { ok: true } | { ok: false; error: ConfigError };

export interface ConfigError {
  message: string;
  correlationId?: string;
}

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

function toConfigError(raw: ApiResponse<unknown>): ConfigError {
  return {
    message: raw.message ?? 'Unable to load configuration.',
    correlationId: raw.correlation_id,
  };
}

function networkError(correlationId?: string): ConfigError {
  return {
    message: 'Unable to connect to configuration service.',
    correlationId,
  };
}

export async function fetchConfig(): Promise<FetchConfigResult> {
  try {
    const response = await client.get<ModelConfigDto>(CONFIG_ENDPOINT);
    if (!response.success || !response.data) {
      return { ok: false, error: toConfigError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function saveConfig(config: ModelConfigRequest): Promise<SaveConfigResult> {
  try {
    const response = await client.put<void>(CONFIG_ENDPOINT, config);
    if (!response.success) {
      return { ok: false, error: toConfigError(response) };
    }
    return { ok: true };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}
