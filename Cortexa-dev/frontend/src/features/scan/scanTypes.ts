export interface GithubCredentials {
  orgUrl: string;
  pat: string;
}

export interface AzureDevOpsCredentials {
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

export type CloneStatus = 'queued' | 'cloning' | 'uploading' | 'stored' | 'failed';

export type ScanProvider = 'github' | 'azure-devops';

export interface RepositoryCloneDto {
  clone_id: string;
  provider: ScanProvider;
  owner: string;
  repository: string;
  branch: string;
  status: CloneStatus;
  created_at: string;
  updated_at: string;
  size_bytes: number | null;
  commit_sha: string | null;
  error: string | null;
}

export interface CloneListDto {
  clones: RepositoryCloneDto[];
}

/** Flat list of repo-relative file paths saved for one clone; the UI groups them into folders. */
export interface CloneFilesDto {
  files: string[];
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

export interface LocalSystemCredentials {
  host: string;
  port: number;
  username: string;
  privateKey: string;
  passphrase?: string;
}

export type DirectoryEntryType = 'file' | 'directory' | 'symlink';

export interface DirectoryEntryDto {
  name: string;
  path: string;
  type: DirectoryEntryType;
  size: number | null;
  modified_at: string | null;
}

export interface DirectoryListingDto {
  path: string;
  entries: DirectoryEntryDto[];
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
