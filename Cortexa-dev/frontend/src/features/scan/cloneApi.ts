import { credentialsBody, getScan, getScanBlob, postScan, type ScanCredentials } from './scanHttp';
import type { CloneFilesDto, CloneListDto, RepositoryCloneDto, ScanResult } from './scanTypes';

const SAVE_ERROR_MESSAGE = 'Saving the repository failed.';
const FILES_ERROR_MESSAGE = 'Could not load the files for this repository.';

/** Save-to-storage calls for one provider; every provider exposes the same endpoints. */
export interface CloneApi {
  start(credentials: ScanCredentials, repository: string, branch: string): Promise<ScanResult<RepositoryCloneDto>>;
  list(): Promise<ScanResult<CloneListDto>>;
  download(clone: RepositoryCloneDto): Promise<ScanResult<Blob>>;
  files(owner: string, repository: string, branch: string): Promise<ScanResult<CloneFilesDto>>;
}

function repoQuery(owner: string, repository: string, branch: string): URLSearchParams {
  return new URLSearchParams({ owner, repository, branch });
}

function downloadPath(basePath: string, clone: RepositoryCloneDto): string {
  return `${basePath}/download?${repoQuery(clone.owner, clone.repository, clone.branch)}`;
}

export function createCloneApi(basePath: string): CloneApi {
  return {
    start: (credentials, repository, branch) =>
      postScan<RepositoryCloneDto>(basePath, { ...credentialsBody(credentials), repository, branch }, SAVE_ERROR_MESSAGE),
    list: () => getScan<CloneListDto>(basePath, SAVE_ERROR_MESSAGE),
    download: (clone) => getScanBlob(downloadPath(basePath, clone), SAVE_ERROR_MESSAGE),
    files: (owner, repository, branch) =>
      getScan<CloneFilesDto>(`${basePath}/files?${repoQuery(owner, repository, branch)}`, FILES_ERROR_MESSAGE),
  };
}

export const githubCloneApi = createCloneApi('/scan/github/clones');
export const azureDevOpsCloneApi = createCloneApi('/scan/azure-devops/clones');
