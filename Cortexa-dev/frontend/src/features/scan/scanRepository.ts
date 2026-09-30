import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type {
  CloneListDto,
  GithubCredentials,
  ListBranchesDto,
  ListRepositoriesDto,
  RepositoryCloneDto,
  RepositoryTreeDto,
  ScanError,
  ScanResult,
} from './scanTypes';

const REPOSITORIES_ENDPOINT = '/scan/github/repositories';
const BRANCHES_ENDPOINT = '/scan/github/branches';
const TREE_ENDPOINT = '/scan/github/tree';
const CLONES_ENDPOINT = '/scan/github/clones';
const CLONE_DOWNLOAD_ENDPOINT = (clone: RepositoryCloneDto) =>
  `${CLONES_ENDPOINT}/download?${new URLSearchParams({ owner: clone.owner, repository: clone.repository, branch: clone.branch })}`;
const NETWORK_ERROR_MESSAGE = 'Unable to reach the scan service.';
const DEFAULT_ERROR_MESSAGE = 'GitHub scan failed.';

const client = new ApiClient(apiBaseUrl, getAccessToken);

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

function failedRequest(err: unknown): { ok: false; error: ScanError } {
  const envelope = errorEnvelope(err);
  return { ok: false, error: toScanError(envelope, envelope ? DEFAULT_ERROR_MESSAGE : NETWORK_ERROR_MESSAGE) };
}

function unwrap<T>(response: ApiResponse<T>): ScanResult<T> {
  if (!response.success || !response.data) {
    return { ok: false, error: toScanError(response, DEFAULT_ERROR_MESSAGE) };
  }
  return { ok: true, data: response.data };
}

async function postScan<T>(path: string, body: Record<string, string>): Promise<ScanResult<T>> {
  try {
    return unwrap(await client.post<T>(path, body));
  } catch (err) {
    return failedRequest(err);
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

export function startClone(
  credentials: GithubCredentials,
  repository: string,
  branch: string
): Promise<ScanResult<RepositoryCloneDto>> {
  return postScan<RepositoryCloneDto>(CLONES_ENDPOINT, { ...credentialsBody(credentials), repository, branch });
}

export async function listClones(): Promise<ScanResult<CloneListDto>> {
  try {
    return unwrap(await client.get<CloneListDto>(CLONES_ENDPOINT));
  } catch (err) {
    return failedRequest(err);
  }
}

export async function downloadClone(clone: RepositoryCloneDto): Promise<ScanResult<Blob>> {
  try {
    return { ok: true, data: await client.getBlob(CLONE_DOWNLOAD_ENDPOINT(clone)) };
  } catch (err) {
    return failedRequest(err);
  }
}
