import type { SavedRepositorySelection } from './savedRepositories';

export type SourceType = 'file' | 'repo';

export type AcceptedFileFormat = 'pdf' | 'docx';

export type GitHost = 'github' | 'azure_devops';

export type AnalysisEngine = 'harvesting' | 'seeding';

export type SeedCorpusDomain = 'ml_ai' | 'biotech' | 'semiconductors' | 'telecoms';

export interface SelectedFile {
  id: string;
  file: File;
  valid: boolean;
  rejectionReason?: string;
}

export interface GitParams {
  repoUrl: string;
  provider: GitHost;
  pat?: string;
  branch?: string;
}

export interface CreateBatchRequest {
  files: File[];
  batchName: string;
  gitParams?: GitParams;
  savedRepositories: SavedRepositorySelection[];
  engine: AnalysisEngine;
  seedCorpusDomain: SeedCorpusDomain;
}

export interface CreateBatchData {
  batch_id: string;
  document_count: number;
  status: string;
}

export type CreateBatchResponse = CreateBatchData;

export type UploadErrorKind = 'validation' | 'network' | 'server';

export interface UploadError {
  kind: UploadErrorKind;
  message: string;
  correlationId?: string;
  errorCode?: string;
  /** Set when the batch was already created server-side but processing failed to start. Retry should resume from start, not re-upload. */
  pendingBatchId?: string;
}

export interface BatchFieldErrors {
  batchName?: string;
  repoUrl?: string;
  gitProvider?: string;
}
