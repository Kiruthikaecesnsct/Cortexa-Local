import type { BatchStatus, BatchSummaryDto, DocumentStatus, JobDocumentDto, JobError, JobStatusDto, UserStage } from './jobTypes';

export type StageState = 'done' | 'active' | 'pending' | 'failed' | 'cancelled';

export interface StageProgress {
  stage: UserStage;
  state: StageState;
}

const STAGE_ORDER: UserStage[] = ['Ingestion', 'Extraction', 'Evidence', 'Score', 'Report'];

const STATUS_TO_STAGE: Record<DocumentStatus, UserStage | 'failed'> = {
  Uploaded: 'Ingestion',
  Ingesting: 'Ingestion',
  Ingested: 'Extraction',
  Extracting: 'Extraction',
  Extracted: 'Evidence',
  Evidencing: 'Evidence',
  Scored: 'Score',
  EngineDone: 'Report',
  NoCandidates: 'Report',
  Failed: 'failed',
  Cancelled: 'failed',
};

const STATUS_TO_ACTIVE_STAGE_INDEX: Record<DocumentStatus, number> = {
  Uploaded: 0,
  Ingesting: 0,
  Ingested: 1,
  Extracting: 1,
  Extracted: 2,
  Evidencing: 2,
  Scored: 3,
  EngineDone: 5,
  NoCandidates: 5,
  Failed: -1,
  Cancelled: -1,
};

const TERMINAL_BATCH_STATUSES: BatchStatus[] = ['Completed', 'PartiallyFailed', 'Failed', 'Cancelled', 'NoCandidates'];


export function isTerminalBatchStatus(status: BatchStatus): boolean {
  return TERMINAL_BATCH_STATUSES.includes(status);
}

export function isDocumentFailed(status: DocumentStatus): boolean {
  return status === 'Failed';
}

export function isDocumentFinished(status: DocumentStatus): boolean {
  return status === 'Scored' || status === 'EngineDone' || status === 'Failed' || status === 'NoCandidates';
}

function stageIndexForStatus(status: DocumentStatus): number {
  return STATUS_TO_ACTIVE_STAGE_INDEX[status];
}

export function getDocumentStages(doc: JobDocumentDto): StageProgress[] {
  if (isDocumentFailed(doc.status)) {
    return buildFailedStages();
  }

  if (doc.status === 'Cancelled') {
    return STAGE_ORDER.map((stage) => ({ stage, state: 'cancelled' as StageState }));
  }

  const activeIndex = stageIndexForStatus(doc.status);
  return STAGE_ORDER.map((stage, index) => ({
    stage,
    state: stageStateForIndex(index, activeIndex),
  }));
}

function buildFailedStages(): StageProgress[] {
  return STAGE_ORDER.map((stage) => ({
    stage,
    state: 'pending' as StageState,
  }));
}

function stageStateForIndex(index: number, activeIndex: number): StageState {
  if (index < activeIndex) {
    return 'done';
  }
  if (index === activeIndex) {
    return 'active';
  }
  return 'pending';
}

const COARSE_STEP_PERCENT = 100 / STAGE_ORDER.length;

export function getDocumentPercent(doc: JobDocumentDto): number {
  if (doc.status === 'Scored' || doc.status === 'EngineDone' || doc.status === 'NoCandidates') {
    return 100;
  }
  if (doc.status === 'Cancelled') {
    return 0;
  }
  if (isDocumentFailed(doc.status)) {
    return 0;
  }
  const activeIndex = stageIndexForStatus(doc.status);
  return Math.round(activeIndex * COARSE_STEP_PERCENT + COARSE_STEP_PERCENT / 2);
}

export interface BatchAggregate {
  total: number;
  finished: number;
  failed: number;
  percent: number;
  percentExact: number;
}

function documentProgressContribution(doc: JobDocumentDto): number {
  if (isDocumentFailed(doc.status)) {
    return 0;
  }
  return isDocumentFinished(doc.status) ? 100 : getDocumentPercent(doc);
}

export function aggregateBatch(job: JobStatusDto): BatchAggregate {
  const total = job.documents.length;
  const finished = job.documents.filter((doc) => isDocumentFinished(doc.status)).length;
  const failed = job.documents.filter((doc) => isDocumentFailed(doc.status)).length;
  const progressSum = job.documents.reduce((sum, doc) => sum + documentProgressContribution(doc), 0);
  const percent = total === 0 ? 0 : Math.round(progressSum / total);
  const percentExact = total === 0 ? 0 : progressSum / total;
  return { total, finished, failed, percent, percentExact };
}

export function getStatusToStageLookup(): Record<DocumentStatus, UserStage | 'done' | 'failed'> {
  return STATUS_TO_STAGE;
}

const ETA_MINUTE_MS = 60_000;

export const STAGE_DESCRIPTIONS: Record<UserStage, string> = {
  Ingestion: 'Reading and splitting your document',
  Extraction: 'Extracting candidate inventions',
  Evidence: 'Gathering patent evidence from three sources',
  Score: 'Scoring patentability against the evidence',
  Report: 'Compiling your patent opportunity report',
};

function formatMinutesRemaining(minutes: number): string {
  if (minutes < 1) {
    return 'less than a minute';
  }
  if (minutes === 1) {
    return 'about 1 min';
  }
  return `about ${minutes} min`;
}

export function formatEta(elapsedMs: number, finishedCount: number, totalCount: number): string | null {
  if (finishedCount < 1 || finishedCount >= totalCount || totalCount === 0) {
    return null;
  }
  const remaining = totalCount - finishedCount;
  const avgPerDoc = elapsedMs / finishedCount;
  const remainingMs = avgPerDoc * remaining;
  const minutes = Math.round(remainingMs / ETA_MINUTE_MS);
  return formatMinutesRemaining(minutes);
}

export function estimateEtaFromRate(elapsedMs: number, percentExact: number): string | null {
  if (percentExact <= 0 || percentExact >= 100 || elapsedMs <= 0) {
    return null;
  }
  const remainingMs = (elapsedMs * (100 - percentExact)) / percentExact;
  const minutes = Math.round(remainingMs / ETA_MINUTE_MS);
  return formatMinutesRemaining(minutes);
}

export function stageRollupCounts(job: JobStatusDto): Record<UserStage, number> {
  const counts: Record<UserStage, number> = {
    Ingestion: 0,
    Extraction: 0,
    Evidence: 0,
    Score: 0,
    Report: 0,
  };

  for (const doc of job.documents) {
    // Scored/EngineDone/NoCandidates are finished statuses (isDocumentFinished === true,
    // getDocumentPercent === 100); by design they roll up to the terminal Report bucket.
    if (doc.status === 'EngineDone' || doc.status === 'NoCandidates' || doc.status === 'Scored') {
      counts.Report += 1;
      continue;
    }

    const stages = getDocumentStages(doc);
    const activeStage = stages.find((s) => s.state === 'active');
    const failedStage = stages.find((s) => s.state === 'failed');

    if (failedStage) {
      counts[failedStage.stage] += 1;
    } else if (activeStage) {
      counts[activeStage.stage] += 1;
    }
  }

  return counts;
}

export function formatElapsed(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  if (hours > 0) {
    return `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
  }
  if (minutes >= 10) {
    return `${minutes}:${String(seconds).padStart(2, '0')}`;
  }
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

export function formatStarted(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

export type StepState = 'done' | 'active' | 'pending' | 'failed' | 'cancelled';

export interface PipelineStep {
  label: string;
  state: StepState;
}

const PIPELINE_STEPS = ['Inject', 'Extract', 'Evidence', 'Score', 'Report'] as const;
export type PipelineStepName = (typeof PIPELINE_STEPS)[number];

const STEP_THRESHOLDS = [0.2, 0.4, 0.6, 0.8] as const;

function deriveStepIndex(completedCount: number, documentCount: number): number {
  if (documentCount === 0) return 0;
  const ratio = completedCount / documentCount;
  const idx = STEP_THRESHOLDS.findIndex((t) => ratio < t);
  return idx === -1 ? STEP_THRESHOLDS.length : idx;
}

export function getBatchSteps(summary: BatchSummaryDto): PipelineStep[] {
  if (summary.status === 'Completed') {
    return PIPELINE_STEPS.map((label) => ({ label, state: 'done' as StepState }));
  }

  const stepIndex = deriveStepIndex(summary.completed_count, summary.document_count);

  if (summary.status === 'Failed' || summary.status === 'PartiallyFailed') {
    return PIPELINE_STEPS.map((label, index) => ({
      label,
      state: (index < stepIndex ? 'done' : index === stepIndex ? 'failed' : 'pending') as StepState,
    }));
  }

  if (summary.status === 'Cancelled') {
    return PIPELINE_STEPS.map((label, index) => ({
      label,
      state: (index < stepIndex ? 'done' : 'cancelled') as StepState,
    }));
  }

  return PIPELINE_STEPS.map((label, index) => ({
    label,
    state: (index < stepIndex ? 'done' : index === stepIndex ? 'active' : 'pending') as StepState,
  }));
}

export type JobViewState =
  | { kind: 'missing-id' }
  | { kind: 'loading' }
  | { kind: 'error-empty'; error: JobError }
  | { kind: 'empty' }
  | {
      kind: 'data';
      data: JobStatusDto;
      aggregate: BatchAggregate;
      isTerminal: boolean;
      staleError: JobError | null;
    };

export function resolveViewState(
  batchId: string | undefined,
  data: JobStatusDto | null,
  isLoading: boolean,
  error: JobError | null
): JobViewState {
  if (!batchId) {
    return { kind: 'missing-id' };
  }
  if (data) {
    return {
      kind: 'data',
      data,
      aggregate: aggregateBatch(data),
      isTerminal: isTerminalBatchStatus(data.status) && data.status !== 'Running',
      staleError: error,
    };
  }
  if (isLoading) {
    return { kind: 'loading' };
  }
  if (error) {
    return { kind: 'error-empty', error };
  }
  return { kind: 'empty' };
}
