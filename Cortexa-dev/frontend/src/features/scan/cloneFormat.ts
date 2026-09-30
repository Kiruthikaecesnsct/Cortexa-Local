import type { CloneStatus, RepositoryCloneDto } from './scanTypes';

const SHORT_SHA_LENGTH = 7;

export const IN_PROGRESS_STATUSES: ReadonlySet<CloneStatus> = new Set(['queued', 'cloning', 'uploading']);

export function isInProgress(clone: RepositoryCloneDto): boolean {
  return IN_PROGRESS_STATUSES.has(clone.status);
}

export const STATUS_LABELS: Record<CloneStatus, string> = {
  queued: 'Waiting to start',
  cloning: 'Downloading from GitHub',
  uploading: 'Saving',
  stored: 'Saved',
  failed: 'Failed',
};

export function shortSha(sha: string | null): string {
  return sha ? sha.slice(0, SHORT_SHA_LENGTH) : '';
}

/** Saved zips are named after the repository, matching the stored file. */
export function cloneFileName(clone: RepositoryCloneDto): string {
  return `${clone.repository}.zip`;
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
