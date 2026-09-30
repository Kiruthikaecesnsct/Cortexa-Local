export interface GithubCredentials {
  orgUrl: string;
  pat: string;
}

export interface RepositorySummaryDto {
  name: string;
  full_name: string;
  private: boolean;
  default_branch: string | null;
  html_url: string;
  description: string | null;
  size_kb: number;
  updated_at: string | null;
}

export interface ListRepositoriesDto {
  owner: string;
  repositories: RepositorySummaryDto[];
}

export interface BranchSummaryDto {
  name: string;
  protected: boolean;
  commit_sha: string | null;
}

export interface ListBranchesDto {
  repository: string;
  branches: BranchSummaryDto[];
}

export type TreeEntryType = 'blob' | 'tree' | 'commit';

export interface TreeEntryDto {
  path: string;
  type: TreeEntryType;
  size: number | null;
}

export interface RepositoryTreeDto {
  repository: string;
  branch: string;
  entries: TreeEntryDto[];
  truncated: boolean;
}

export interface ScanError {
  message: string;
  correlationId?: string;
}

export type ScanResult<T> = { ok: true; data: T } | { ok: false; error: ScanError };

export type Loadable<T> =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'loaded'; data: T }
  | { status: 'error'; error: ScanError };

export type FileNodeKind = 'folder' | 'file' | 'submodule';

export interface FileNode {
  name: string;
  path: string;
  kind: FileNodeKind;
  size: number | null;
  children: FileNode[];
}

export interface LoadedTree {
  tree: RepositoryTreeDto;
  nodes: FileNode[];
}
