import { credentialsBody, postScan } from './scanHttp';
import type { AzureDevOpsCredentials, ListBranchesDto, ListRepositoriesDto, RepositoryTreeDto, ScanResult } from './scanTypes';

const REPOSITORIES_ENDPOINT = '/scan/azure-devops/repositories';
const BRANCHES_ENDPOINT = '/scan/azure-devops/branches';
const TREE_ENDPOINT = '/scan/azure-devops/tree';
const DEFAULT_ERROR_MESSAGE = 'Azure DevOps scan failed.';

export function listRepositories(credentials: AzureDevOpsCredentials): Promise<ScanResult<ListRepositoriesDto>> {
  return postScan<ListRepositoriesDto>(REPOSITORIES_ENDPOINT, credentialsBody(credentials), DEFAULT_ERROR_MESSAGE);
}

export function listBranches(credentials: AzureDevOpsCredentials, repository: string): Promise<ScanResult<ListBranchesDto>> {
  return postScan<ListBranchesDto>(BRANCHES_ENDPOINT, { ...credentialsBody(credentials), repository }, DEFAULT_ERROR_MESSAGE);
}

export function fetchRepositoryTree(
  credentials: AzureDevOpsCredentials,
  repository: string,
  branch: string
): Promise<ScanResult<RepositoryTreeDto>> {
  return postScan<RepositoryTreeDto>(TREE_ENDPOINT, { ...credentialsBody(credentials), repository, branch }, DEFAULT_ERROR_MESSAGE);
}
