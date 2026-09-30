import { credentialsBody, postScan } from './scanHttp';
import type { GithubCredentials, ListBranchesDto, ListRepositoriesDto, RepositoryTreeDto, ScanResult } from './scanTypes';

const REPOSITORIES_ENDPOINT = '/scan/github/repositories';
const BRANCHES_ENDPOINT = '/scan/github/branches';
const TREE_ENDPOINT = '/scan/github/tree';
const DEFAULT_ERROR_MESSAGE = 'GitHub scan failed.';

export function listRepositories(credentials: GithubCredentials): Promise<ScanResult<ListRepositoriesDto>> {
  return postScan<ListRepositoriesDto>(REPOSITORIES_ENDPOINT, credentialsBody(credentials), DEFAULT_ERROR_MESSAGE);
}

export function listBranches(credentials: GithubCredentials, repository: string): Promise<ScanResult<ListBranchesDto>> {
  return postScan<ListBranchesDto>(BRANCHES_ENDPOINT, { ...credentialsBody(credentials), repository }, DEFAULT_ERROR_MESSAGE);
}

export function fetchRepositoryTree(
  credentials: GithubCredentials,
  repository: string,
  branch: string
): Promise<ScanResult<RepositoryTreeDto>> {
  return postScan<RepositoryTreeDto>(TREE_ENDPOINT, { ...credentialsBody(credentials), repository, branch }, DEFAULT_ERROR_MESSAGE);
}
