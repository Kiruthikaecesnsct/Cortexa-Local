import type { CloneStatus, RepositoryCloneDto, ScanProvider } from './scanTypes';

const SHORT_SHA_LENGTH = 7;

export const IN_PROGRESS_STATUSES: ReadonlySet<CloneStatus> = new Set(['queued', 'cloning', 'uploading']);

export function isInProgress(clone: RepositoryCloneDto): boolean {
  return IN_PROGRESS_STATUSES.has(clone.status);
}

export const PROVIDER_NAMES: Record<ScanProvider, string> = {
  github: 'GitHub',
  'azure-devops': 'Azure DevOps',
};

const STATUS_LABELS: Record<CloneStatus, (provider: ScanProvider) => string> = {
  queued: () => 'Waiting to start',
  cloning: (provider) => `Downloading from ${PROVIDER_NAMES[provider]}`,
  uploading: () => 'Saving',
  stored: () => 'Saved',
  failed: () => 'Failed',
};

/** Status text for a save, naming the site it is being downloaded from. */
export function statusLabel(clone: RepositoryCloneDto): string {
  return STATUS_LABELS[clone.status](clone.provider);
}

export function shortSha(sha: string | null): string {
  return sha ? sha.slice(0, SHORT_SHA_LENGTH) : '';
}

/** Saved zips are named after the repository (the last part of an Azure "project/repo"). */
export function cloneFileName(clone: RepositoryCloneDto): string {
  return `${clone.repository.split('/').pop() ?? clone.repository}.zip`;
}

export interface BranchRef {
  owner: string;
  repository: string;
  branch: string;
}

/** The newest save of this owner/repository/branch, if there is one. */
export function latestCloneFor(clones: RepositoryCloneDto[], ref: BranchRef): RepositoryCloneDto | undefined {
  return clones
    .filter((c) => c.owner === ref.owner && c.repository === ref.repository && c.branch === ref.branch)
    .sort((a, b) => b.created_at.localeCompare(a.created_at))[0];
}
