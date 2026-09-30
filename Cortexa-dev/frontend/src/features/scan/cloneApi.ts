import { credentialsBody, getScan, getScanBlob, postScan, type ScanCredentials } from './scanHttp';
import type { CloneListDto, RepositoryCloneDto, ScanResult } from './scanTypes';

const SAVE_ERROR_MESSAGE = 'Saving the repository failed.';

/** Save-to-storage calls for one provider; every provider exposes the same three endpoints. */
export interface CloneApi {
  start(credentials: ScanCredentials, repository: string, branch: string): Promise<ScanResult<RepositoryCloneDto>>;
  list(): Promise<ScanResult<CloneListDto>>;
  download(clone: RepositoryCloneDto): Promise<ScanResult<Blob>>;
}

function downloadPath(basePath: string, clone: RepositoryCloneDto): string {
  const query = new URLSearchParams({ owner: clone.owner, repository: clone.repository, branch: clone.branch });
  return `${basePath}/download?${query}`;
}

export function createCloneApi(basePath: string): CloneApi {
  return {
    start: (credentials, repository, branch) =>
      postScan<RepositoryCloneDto>(basePath, { ...credentialsBody(credentials), repository, branch }, SAVE_ERROR_MESSAGE),
    list: () => getScan<CloneListDto>(basePath, SAVE_ERROR_MESSAGE),
    download: (clone) => getScanBlob(downloadPath(basePath, clone), SAVE_ERROR_MESSAGE),
  };
}

export const githubCloneApi = createCloneApi('/scan/github/clones');
export const azureDevOpsCloneApi = createCloneApi('/scan/azure-devops/clones');
