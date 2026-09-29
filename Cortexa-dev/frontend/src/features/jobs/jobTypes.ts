export type BatchStatus =
  | 'Created'
  | 'Running'
  | 'Completed'
  | 'PartiallyFailed'
  | 'Failed'
  | 'Cancelled'
  | 'NoCandidates';

export type DocumentStatus =
  | 'Uploaded'
  | 'Ingesting'
  | 'Ingested'
  | 'Extracting'
  | 'Extracted'
  | 'Evidencing'
  | 'Scored'
  | 'EngineDone'
  | 'NoCandidates'
  | 'Failed'
  | 'Cancelled';

export type UserStage = 'Ingestion' | 'Extraction' | 'Evidence' | 'Score' | 'Report';

export interface JobDocumentDto {
  document_id: string;
  filename: string;
  status: DocumentStatus;
  error?: string;
}

export interface JobStatusDto {
  batch_id: string;
  batch_name?: string;
  status: BatchStatus;
  documents: JobDocumentDto[];
  wants_harvesting?: boolean;
  wants_seeding?: boolean;
  created_at?: string;
}

export interface BatchSummaryDto {
  batch_id: string;
  name?: string;
  status: BatchStatus;
  document_count: number;
  completed_count: number;
  created_at: string;
}

export type JobErrorKind = 'network' | 'server' | 'not_found' | 'forbidden';

export interface StoreDeletionDto {
  store_name: string;
  deleted_count: number;
  success: boolean;
  error?: string;
}

export interface DeleteBatchResultDto {
  batch_id: string;
  status: string;
  fully_deleted: boolean;
  stores: StoreDeletionDto[];
}

export interface JobError {
  kind: JobErrorKind;
  message: string;
  correlationId?: string;
  errorCode?: string;
}
