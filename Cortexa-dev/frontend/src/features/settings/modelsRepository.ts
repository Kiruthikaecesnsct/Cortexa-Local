import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { ModelCapability, ModelDto, ModelsResponseDto, PipelineStage } from './settingsTypes';

const MODELS_ENDPOINT = '/model/models';

export type FetchModelsResult = { ok: true; data: ModelsResponseDto } | { ok: false; error: ModelsError };

export interface ModelsError {
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

function networkError(correlationId?: string): ModelsError {
  return {
    message: 'Unable to connect to model service.',
    correlationId,
  };
}

function isModelsResponse(body: unknown): body is ModelsResponseDto {
  return body !== null && typeof body === 'object' && Array.isArray((body as ModelsResponseDto).models);
}

export async function fetchModels(): Promise<FetchModelsResult> {
  try {
    // model-router returns a bare body (no ApiResponse envelope); internal
    // services read it raw too, so the frontend must not expect success/data.
    const body = await client.getRaw<ModelsResponseDto>(MODELS_ENDPOINT);
    if (!isModelsResponse(body)) {
      return { ok: false, error: networkError() };
    }
    return { ok: true, data: body };
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export function hasCapability(model: ModelDto, capability: ModelCapability): boolean {
  return model.capabilities.includes(capability);
}

export function getModelsForStage(models: ModelDto[], stage: PipelineStage): ModelDto[] {
  return models.filter((m) => m.enabled && m.allowedStages.includes(stage));
}
