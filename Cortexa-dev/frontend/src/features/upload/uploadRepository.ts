import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { CreateBatchData, CreateBatchRequest, UploadError } from './uploadTypes';

type CreateBatchResult = { ok: true; data: CreateBatchData } | { ok: false; error: UploadError };

const client = new ApiClient(apiBaseUrl, getAccessToken);

function buildFormData(request: CreateBatchRequest): FormData {
  const form = new FormData();
  request.files.forEach((file) => form.append('files[]', file));
  form.append('batch_name', request.batchName);
  form.append('engine', request.engine);
  form.append('seed_corpus_domain', request.seedCorpusDomain);
  if (request.gitParams) {
    form.append('repo_url', request.gitParams.repoUrl);
    form.append('git_provider', request.gitParams.provider);
    if (request.gitParams.pat) form.append('git_pat', request.gitParams.pat);
    if (request.gitParams.branch) form.append('git_branch', request.gitParams.branch);
  }
  request.savedRepositories.forEach((repo) => form.append('saved_repositories', JSON.stringify(repo)));
  return form;
}

function toUploadError(raw: ApiResponse<unknown>, fallbackCorrelationId?: string): UploadError {
  const correlationId = raw.correlation_id ?? fallbackCorrelationId;
  return {
    kind: 'server',
    message: raw.message ?? "Couldn't create the batch. Try again.",
    correlationId,
    errorCode: raw.error_code,
  };
}

function networkError(correlationId?: string): UploadError {
  return {
    kind: 'network',
    message: "Can't reach Cortexa right now. The pipeline service didn't respond.",
    correlationId,
  };
}

export function extractCorrelationId(err: unknown): string | undefined {
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

export async function startBatchProcessing(batchId: string): Promise<CreateBatchResult> {
  try {
    const response = await client.post<{ batch_id: string }>('/batches/start', { batch_id: batchId });
    if (response.success) {
      return { ok: true, data: { batch_id: batchId, document_count: 0, status: 'started' } };
    }
    return { ok: false, error: { ...toUploadError(response), pendingBatchId: batchId } };
  } catch (err) {
    return { ok: false, error: { ...networkError(extractCorrelationId(err)), pendingBatchId: batchId } };
  }
}

export async function resumeBatch(pendingBatchId: string): Promise<CreateBatchResult> {
  return startBatchProcessing(pendingBatchId);
}

export async function createBatch(request: CreateBatchRequest): Promise<CreateBatchResult> {
  try {
    const form = buildFormData(request);
    const response = await client.postForm<CreateBatchData>('/batches', form);
    if (!response.success || !response.data) {
      return { ok: false, error: toUploadError(response) };
    }

    return startBatchProcessing(response.data.batch_id);
  } catch (err) {
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}
