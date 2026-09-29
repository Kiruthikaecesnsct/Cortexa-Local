import { describe, it, expect } from 'vitest';
import {
  getDocumentStages,
  getDocumentPercent,
  aggregateBatch,
  isTerminalBatchStatus,
  isDocumentFailed,
  isDocumentFinished,
  formatEta,
  resolveViewState,
  getBatchSteps,
  estimateEtaFromRate,
  stageRollupCounts,
} from '../jobProgress';
import type { DocumentStatus, JobDocumentDto, JobStatusDto, BatchStatus, JobError, BatchSummaryDto } from '../jobTypes';

function makeDoc(status: DocumentStatus): JobDocumentDto {
  return { document_id: 'doc-1', filename: 'paper.pdf', status };
}

function makeJob(statuses: DocumentStatus[], batchStatus: BatchStatus = 'Running'): JobStatusDto {
  return {
    batch_id: 'batch-1',
    status: batchStatus,
    documents: statuses.map((status, index) => ({
      document_id: `doc-${index}`,
      filename: `file-${index}.pdf`,
      status,
    })),
  };
}

function makeSummary(status: BatchStatus, completedCount: number, documentCount: number): BatchSummaryDto {
  return {
    batch_id: 'batch-1',
    status,
    document_count: documentCount,
    completed_count: completedCount,
    created_at: '2024-01-01T00:00:00Z',
  };
}

describe('getDocumentStages', () => {
  it.each<[DocumentStatus, ['done' | 'active' | 'pending' | 'failed', 'done' | 'active' | 'pending' | 'failed', 'done' | 'active' | 'pending' | 'failed', 'done' | 'active' | 'pending' | 'failed', 'done' | 'active' | 'pending' | 'failed']]>([
    ['Uploaded',   ['active', 'pending', 'pending', 'pending', 'pending']],
    ['Ingesting',  ['active', 'pending', 'pending', 'pending', 'pending']],
    ['Ingested',   ['done',   'active',  'pending', 'pending', 'pending']],
    ['Extracting', ['done',   'active',  'pending', 'pending', 'pending']],
    ['Extracted',  ['done',   'done',    'active',  'pending', 'pending']],
    ['Evidencing', ['done',   'done',    'active',  'pending', 'pending']],
    ['Scored',     ['done',   'done',    'done',    'active',  'pending']],
    ['EngineDone', ['done',   'done',    'done',    'done',    'done'  ]],
  ])('maps %s to %j', (status, expectedStates) => {
    const doc = makeDoc(status);

    const stages = getDocumentStages(doc);

    expect(stages).toEqual([
      { stage: 'Ingestion', state: expectedStates[0] },
      { stage: 'Extraction', state: expectedStates[1] },
      { stage: 'Evidence', state: expectedStates[2] },
      { stage: 'Score', state: expectedStates[3] },
      { stage: 'Report', state: expectedStates[4] },
    ]);
  });

  it('maps Failed to all stages pending (stage-neutral treatment)', () => {
    const doc = makeDoc('Failed');

    const stages = getDocumentStages(doc);

    expect(stages).toEqual([
      { stage: 'Ingestion', state: 'pending' },
      { stage: 'Extraction', state: 'pending' },
      { stage: 'Evidence', state: 'pending' },
      { stage: 'Score', state: 'pending' },
      { stage: 'Report', state: 'pending' },
    ]);
  });

  it('returns Score stage active and Report stage pending for Scored status', () => {
    const doc = makeDoc('Scored');

    const stages = getDocumentStages(doc);

    expect(stages.find((s) => s.stage === 'Score')?.state).toBe('active');
    expect(stages.find((s) => s.stage === 'Report')?.state).toBe('pending');
  });

  it('returns all five stages done for EngineDone status', () => {
    const doc = makeDoc('EngineDone');

    const stages = getDocumentStages(doc);

    expect(stages).toHaveLength(5);
    expect(stages.every((s) => s.state === 'done')).toBe(true);
  });
});

describe('getDocumentPercent', () => {
  const ExpectedFirstStepPercent = 10;
  const ExpectedSecondStepPercent = 30;
  const ExpectedThirdStepPercent = 50;
  const ExpectedScoredPercent = 100;
  const ExpectedFullPercent = 100;
  const ExpectedFailedPercent = 0;

  it.each<[DocumentStatus, number]>([
    ['Uploaded',   ExpectedFirstStepPercent],
    ['Ingesting',  ExpectedFirstStepPercent],
    ['Ingested',   ExpectedSecondStepPercent],
    ['Extracting', ExpectedSecondStepPercent],
    ['Extracted',  ExpectedThirdStepPercent],
    ['Evidencing', ExpectedThirdStepPercent],
    ['Scored',     ExpectedScoredPercent],
    ['EngineDone', ExpectedFullPercent],
    ['Failed',     ExpectedFailedPercent],
  ])('returns %i for status %s', (status, expectedPercent) => {
    const doc = makeDoc(status);

    const percent = getDocumentPercent(doc);

    expect(percent).toBe(expectedPercent);
  });

  it('returns 100 for Scored status (consistent with isDocumentFinished)', () => {
    const doc = makeDoc('Scored');

    const percent = getDocumentPercent(doc);

    expect(percent).toBe(100);
  });

  it('returns 100 for EngineDone status', () => {
    const doc = makeDoc('EngineDone');

    const percent = getDocumentPercent(doc);

    expect(percent).toBe(100);
  });
});

describe('aggregateBatch', () => {
  it('computes total, finished, failed and percent across mixed statuses', () => {
    const ExpectedTotal = 4;
    const ExpectedFinished = 3;
    const ExpectedFailed = 1;
    // Scored=100, Failed=0, Ingesting=10 (in-flight), EngineDone=100 → mean 52.5 → 53.
    const ExpectedPercent = 53;
    const ExpectedPercentExact = 52.5;
    const job = makeJob(['Scored', 'Failed', 'Ingesting', 'EngineDone']);

    const aggregate = aggregateBatch(job);

    expect(aggregate).toEqual({
      total: ExpectedTotal,
      finished: ExpectedFinished,
      failed: ExpectedFailed,
      percent: ExpectedPercent,
      percentExact: ExpectedPercentExact,
    });
  });

  it('reflects in-flight stage progress for a single-document batch (BUG150)', () => {
    const ExpectedIngestingPercent = 10;
    const ExpectedExtractedPercent = 50;

    expect(aggregateBatch(makeJob(['Ingesting'])).percent).toBe(ExpectedIngestingPercent);
    expect(aggregateBatch(makeJob(['Extracted'])).percent).toBe(ExpectedExtractedPercent);
  });

  it('returns zero percent with no NaN when there are no documents', () => {
    const ExpectedTotal = 0;
    const ExpectedFinished = 0;
    const ExpectedFailed = 0;
    const ExpectedPercent = 0;
    const ExpectedPercentExact = 0;
    const job = makeJob([]);

    const aggregate = aggregateBatch(job);

    expect(aggregate).toEqual({
      total: ExpectedTotal,
      finished: ExpectedFinished,
      failed: ExpectedFailed,
      percent: ExpectedPercent,
      percentExact: ExpectedPercentExact,
    });
    expect(Number.isNaN(aggregate.percent)).toBe(false);
    expect(Number.isNaN(aggregate.percentExact)).toBe(false);
  });

  it('counts Failed documents as finished and failed but contributes 0% to progress', () => {
    const ExpectedTotal = 1;
    const ExpectedFinished = 1;
    const ExpectedFailed = 1;
    const ExpectedPercent = 0;
    const ExpectedPercentExact = 0;
    const job = makeJob(['Failed']);

    const aggregate = aggregateBatch(job);

    expect(aggregate).toEqual({
      total: ExpectedTotal,
      finished: ExpectedFinished,
      failed: ExpectedFailed,
      percent: ExpectedPercent,
      percentExact: ExpectedPercentExact,
    });
  });

  it('populates percentExact with the exact unrounded ratio', () => {
    const job = makeJob(['Ingesting', 'Ingested', 'Extracting']);
    const ExpectedExact = (10 + 30 + 30) / 3;
    const ExpectedRounded = 23;

    const aggregate = aggregateBatch(job);

    expect(aggregate.percentExact).toBeCloseTo(ExpectedExact, 5);
    expect(aggregate.percent).toBe(ExpectedRounded);
  });
});

describe('isTerminalBatchStatus', () => {
  it.each<[BatchStatus, boolean]>([
    ['Completed', true],
    ['PartiallyFailed', true],
    ['Failed', true],
    ['Running', false],
  ])('returns %s for status %s', (status, expected) => {
    expect(isTerminalBatchStatus(status)).toBe(expected);
  });
});

describe('isDocumentFailed', () => {
  it('returns true for Failed status', () => {
    expect(isDocumentFailed('Failed')).toBe(true);
  });

  it('returns false for non-Failed status', () => {
    expect(isDocumentFailed('Scored')).toBe(false);
  });
});

describe('isDocumentFinished', () => {
  it.each<[DocumentStatus, boolean]>([
    ['Scored', true],
    ['EngineDone', true],
    ['Failed', true],
    ['Uploaded', false],
    ['Ingesting', false],
    ['Ingested', false],
    ['Extracting', false],
    ['Extracted', false],
    ['Evidencing', false],
  ])('returns %s for status %s', (status, expected) => {
    expect(isDocumentFinished(status)).toBe(expected);
  });
});

describe('formatEta', () => {
  const OneMinuteMs = 60_000;

  it('returns null when no documents have finished yet', () => {
    const ExpectedFinishedCount = 0;
    const ExpectedTotalCount = 5;
    const ExpectedElapsedMs = 60_000;

    const eta = formatEta(ExpectedElapsedMs, ExpectedFinishedCount, ExpectedTotalCount);

    expect(eta).toBeNull();
  });

  it('returns null when all documents have finished', () => {
    const ExpectedFinishedCount = 5;
    const ExpectedTotalCount = 5;
    const ExpectedElapsedMs = 60_000;

    const eta = formatEta(ExpectedElapsedMs, ExpectedFinishedCount, ExpectedTotalCount);

    expect(eta).toBeNull();
  });

  it('returns null when total count is zero', () => {
    const ExpectedFinishedCount = 0;
    const ExpectedTotalCount = 0;
    const ExpectedElapsedMs = 0;

    const eta = formatEta(ExpectedElapsedMs, ExpectedFinishedCount, ExpectedTotalCount);

    expect(eta).toBeNull();
  });

  it('returns "less than a minute" when remaining time is under a minute', () => {
    const ExpectedFinishedCount = 9;
    const ExpectedTotalCount = 10;
    const ExpectedElapsedMs = 9_000;

    const eta = formatEta(ExpectedElapsedMs, ExpectedFinishedCount, ExpectedTotalCount);

    expect(eta).toBe('less than a minute');
  });

  it('returns "about 1 min" when remaining time rounds to exactly one minute', () => {
    const ExpectedFinishedCount = 1;
    const ExpectedTotalCount = 2;
    const ExpectedElapsedMs = OneMinuteMs;

    const eta = formatEta(ExpectedElapsedMs, ExpectedFinishedCount, ExpectedTotalCount);

    expect(eta).toBe('about 1 min');
  });

  it('returns "about N min" phrasing for multi-minute remainders', () => {
    const ExpectedFinishedCount = 1;
    const ExpectedTotalCount = 5;
    const ExpectedElapsedMs = OneMinuteMs;

    const eta = formatEta(ExpectedElapsedMs, ExpectedFinishedCount, ExpectedTotalCount);

    expect(eta).toBe('about 4 min');
  });
});

describe('getBatchSteps', () => {
  it('returns all five steps done for a Completed batch', () => {
    const summary = makeSummary('Completed', 5, 5);

    const steps = getBatchSteps(summary);

    expect(steps).toEqual([
      { label: 'Inject', state: 'done' },
      { label: 'Extract', state: 'done' },
      { label: 'Evidence', state: 'done' },
      { label: 'Score', state: 'done' },
      { label: 'Report', state: 'done' },
    ]);
  });

  it('returns Inject active and remaining steps pending for Running batch with no completed documents', () => {
    const summary = makeSummary('Running', 0, 5);

    const steps = getBatchSteps(summary);

    expect(steps).toEqual([
      { label: 'Inject', state: 'active' },
      { label: 'Extract', state: 'pending' },
      { label: 'Evidence', state: 'pending' },
      { label: 'Score', state: 'pending' },
      { label: 'Report', state: 'pending' },
    ]);
  });

  it('returns Inject and Extract done with Evidence active for Running batch at ratio 0.4', () => {
    const summary = makeSummary('Running', 2, 5);

    const steps = getBatchSteps(summary);

    expect(steps).toEqual([
      { label: 'Inject', state: 'done' },
      { label: 'Extract', state: 'done' },
      { label: 'Evidence', state: 'active' },
      { label: 'Score', state: 'pending' },
      { label: 'Report', state: 'pending' },
    ]);
  });

  it('returns first four steps done and Report active for Running batch at ratio 0.8', () => {
    const summary = makeSummary('Running', 4, 5);

    const steps = getBatchSteps(summary);

    expect(steps).toEqual([
      { label: 'Inject', state: 'done' },
      { label: 'Extract', state: 'done' },
      { label: 'Evidence', state: 'done' },
      { label: 'Score', state: 'done' },
      { label: 'Report', state: 'active' },
    ]);
  });

  it('returns Inject failed and remaining steps pending for Failed batch with no completed documents', () => {
    const summary = makeSummary('Failed', 0, 5);

    const steps = getBatchSteps(summary);

    expect(steps).toEqual([
      { label: 'Inject', state: 'failed' },
      { label: 'Extract', state: 'pending' },
      { label: 'Evidence', state: 'pending' },
      { label: 'Score', state: 'pending' },
      { label: 'Report', state: 'pending' },
    ]);
  });

  it('returns Inject, Extract, Evidence done with Score failed for Failed batch at ratio 0.6', () => {
    const summary = makeSummary('Failed', 3, 5);

    const steps = getBatchSteps(summary);

    expect(steps).toEqual([
      { label: 'Inject', state: 'done' },
      { label: 'Extract', state: 'done' },
      { label: 'Evidence', state: 'done' },
      { label: 'Score', state: 'failed' },
      { label: 'Report', state: 'pending' },
    ]);
  });

  it('returns Inject done with Extract failed for PartiallyFailed batch at ratio 0.2', () => {
    const summary = makeSummary('PartiallyFailed', 1, 5);

    const steps = getBatchSteps(summary);

    expect(steps).toEqual([
      { label: 'Inject', state: 'done' },
      { label: 'Extract', state: 'failed' },
      { label: 'Evidence', state: 'pending' },
      { label: 'Score', state: 'pending' },
      { label: 'Report', state: 'pending' },
    ]);
  });
});

describe('resolveViewState', () => {
  const sampleError: JobError = { kind: 'network', message: 'boom' };

  it('returns missing-id when batchId is undefined', () => {
    const viewState = resolveViewState(undefined, null, false, null);

    expect(viewState).toEqual({ kind: 'missing-id' });
  });

  it('returns missing-id when batchId is empty string', () => {
    const viewState = resolveViewState('', null, false, null);

    expect(viewState).toEqual({ kind: 'missing-id' });
  });

  it('returns loading when there is no data yet and a fetch is in flight', () => {
    const viewState = resolveViewState('batch-1', null, true, null);

    expect(viewState).toEqual({ kind: 'loading' });
  });

  it('returns error-empty when there is no data and the fetch failed', () => {
    const viewState = resolveViewState('batch-1', null, false, sampleError);

    expect(viewState).toEqual({ kind: 'error-empty', error: sampleError });
  });

  it('returns empty when there is no data, no error, and not loading', () => {
    const viewState = resolveViewState('batch-1', null, false, null);

    expect(viewState).toEqual({ kind: 'empty' });
  });

  it('returns data with staleError null when the fetch succeeded with no prior error', () => {
    const job = makeJob(['Uploaded'], 'Running');

    const viewState = resolveViewState('batch-1', job, false, null);

    expect(viewState).toEqual({
      kind: 'data',
      data: job,
      aggregate: aggregateBatch(job),
      isTerminal: false,
      staleError: null,
    });
  });

  it('returns data with staleError populated when data exists alongside a poll error', () => {
    const job = makeJob(['Scored'], 'Completed');

    const viewState = resolveViewState('batch-1', job, false, sampleError);

    expect(viewState).toEqual({
      kind: 'data',
      data: job,
      aggregate: aggregateBatch(job),
      isTerminal: true,
      staleError: sampleError,
    });
  });

  it('marks isTerminal false while batch status is Running even if data exists', () => {
    const job = makeJob(['Ingesting'], 'Running');

    const viewState = resolveViewState('batch-1', job, false, null);

    expect(viewState.kind).toBe('data');
    expect(viewState.kind === 'data' && viewState.isTerminal).toBe(false);
  });

  it('marks isTerminal true for PartiallyFailed and Failed batch statuses', () => {
    const partial = resolveViewState('batch-1', makeJob(['Failed', 'Scored'], 'PartiallyFailed'), false, null);
    const failed = resolveViewState('batch-1', makeJob(['Failed'], 'Failed'), false, null);

    expect(partial.kind === 'data' && partial.isTerminal).toBe(true);
    expect(failed.kind === 'data' && failed.isTerminal).toBe(true);
  });

  it('prefers data over loading when both data and isLoading are present', () => {
    const job = makeJob(['Uploaded'], 'Running');

    const viewState = resolveViewState('batch-1', job, true, null);

    expect(viewState.kind).toBe('data');
  });
});

describe('estimateEtaFromRate', () => {
  it('returns null when percentExact is zero or negative', () => {
    const ExpectedElapsedMs = 60_000;
    expect(estimateEtaFromRate(ExpectedElapsedMs, 0)).toBeNull();
    expect(estimateEtaFromRate(ExpectedElapsedMs, -5)).toBeNull();
  });

  it('returns null when percentExact is 100 or above', () => {
    const ExpectedElapsedMs = 60_000;
    expect(estimateEtaFromRate(ExpectedElapsedMs, 100)).toBeNull();
    expect(estimateEtaFromRate(ExpectedElapsedMs, 105)).toBeNull();
  });

  it('returns null when elapsedMs is zero or negative', () => {
    const ExpectedPercentExact = 50;
    expect(estimateEtaFromRate(0, ExpectedPercentExact)).toBeNull();
    expect(estimateEtaFromRate(-1000, ExpectedPercentExact)).toBeNull();
  });

  it('returns "less than a minute" when remaining time rounds to under a minute', () => {
    const ExpectedElapsedMs = 10_000;
    const ExpectedPercentExact = 90;
    const result = estimateEtaFromRate(ExpectedElapsedMs, ExpectedPercentExact);
    expect(result).toBe('less than a minute');
  });

  it('returns "about 1 min" when remaining time rounds to exactly one minute', () => {
    const ExpectedElapsedMs = 60_000;
    const ExpectedPercentExact = 50;
    const result = estimateEtaFromRate(ExpectedElapsedMs, ExpectedPercentExact);
    expect(result).toBe('about 1 min');
  });

  it('returns "about N min" for multi-minute remainders', () => {
    const ExpectedElapsedMs = 120_000;
    const ExpectedPercentExact = 40;
    const result = estimateEtaFromRate(ExpectedElapsedMs, ExpectedPercentExact);
    expect(result).toBe('about 3 min');
  });
});

describe('stageRollupCounts', () => {
  it('buckets documents by their active stage', () => {
    const job = makeJob(['Ingesting', 'Ingested', 'Extracting', 'Evidencing']);
    const counts = stageRollupCounts(job);
    expect(counts.Ingestion).toBe(1);
    expect(counts.Extraction).toBe(2);
    expect(counts.Evidence).toBe(1);
    expect(counts.Report).toBe(0);
  });

  it('buckets finished documents to Report stage', () => {
    const job = makeJob(['Scored', 'EngineDone', 'NoCandidates']);
    const counts = stageRollupCounts(job);
    expect(counts.Report).toBe(3);
  });

  it('does not bucket failed documents to any stage (no failed stage reported)', () => {
    const job = makeJob(['Failed', 'Extracting']);
    const counts = stageRollupCounts(job);
    expect(counts.Ingestion).toBe(0);
    expect(counts.Extraction).toBe(1);
    expect(counts.Evidence).toBe(0);
    expect(counts.Score).toBe(0);
    expect(counts.Report).toBe(0);
  });

  it('handles an empty document list', () => {
    const job = makeJob([]);
    const counts = stageRollupCounts(job);
    expect(counts.Ingestion).toBe(0);
    expect(counts.Extraction).toBe(0);
    expect(counts.Evidence).toBe(0);
    expect(counts.Score).toBe(0);
    expect(counts.Report).toBe(0);
  });
});
