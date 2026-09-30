import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type {
  GithubCredentials,
  ListBranchesDto,
  ListRepositoriesDto,
  RepositoryTreeDto,
  ScanError,
  ScanResult,
} from './scanTypes';

const REPOSITORIES_ENDPOINT = '/scan/github/repositories';
const BRANCHES_ENDPOINT = '/scan/github/branches';
const TREE_ENDPOINT = '/scan/github/tree';
const NETWORK_ERROR_MESSAGE = 'Unable to reach the scan service.';
const DEFAULT_ERROR_MESSAGE = 'GitHub scan failed.';

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

async function postScan<T>(path: string, body: Record<string, string>): Promise<ScanResult<T>> {
  try {
    const response = await client.post<T>(path, body);
    if (!response.success || !response.data) {
      return { ok: false, error: toScanError(response, DEFAULT_ERROR_MESSAGE) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    const envelope = errorEnvelope(err);
    return { ok: false, error: toScanError(envelope, envelope ? DEFAULT_ERROR_MESSAGE : NETWORK_ERROR_MESSAGE) };
  }
}

function credentialsBody(credentials: GithubCredentials): Record<string, string> {
  return { org_url: credentials.orgUrl.trim(), pat: credentials.pat.trim() };
}

export function listRepositories(credentials: GithubCredentials): Promise<ScanResult<ListRepositoriesDto>> {
  return postScan<ListRepositoriesDto>(REPOSITORIES_ENDPOINT, credentialsBody(credentials));
}

export function listBranches(credentials: GithubCredentials, repository: string): Promise<ScanResult<ListBranchesDto>> {
  return postScan<ListBranchesDto>(BRANCHES_ENDPOINT, { ...credentialsBody(credentials), repository });
}

export function fetchRepositoryTree(
  credentials: GithubCredentials,
  repository: string,
  branch: string
): Promise<ScanResult<RepositoryTreeDto>> {
  return postScan<RepositoryTreeDto>(TREE_ENDPOINT, { ...credentialsBody(credentials), repository, branch });
}
