import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { AppShell } from '../../shared/layout/AppShell';
import { Button, Modal, ProgressBar, Spinner, ErrorState, Skeleton, Input, InlineMessage } from '../../shared/ds';
import { BatchStatusBadge, DocumentStatusBadge } from '../../shared/ds/badges';
import { useToast } from '../../shared/ds/Toast';
import { usePollingJob } from './usePollingJob';
import { retryDocument, stopBatch } from './jobsRepository';
import {
  resolveViewState,
  getDocumentStages,
  getDocumentPercent,
  formatEta,
  estimateEtaFromRate,
  stageRollupCounts,
  formatElapsed,
  formatStarted,
  STAGE_DESCRIPTIONS,
  type StageProgress,
} from './jobProgress';
import { useElapsed } from './useElapsed';
import type { JobDocumentDto, JobStatusDto, UserStage } from './jobTypes';

const STAGES = ['Ingest', 'Extract', 'Evidence', 'Score', 'Report'];

function stagePillStyle(state: StageProgress['state']): React.CSSProperties {
  switch (state) {
    case 'done':
      return { background: 'var(--status-success-bg)', color: 'var(--status-success-fg)' };
    case 'active':
      return { background: 'var(--accent-primary-subtle)', color: 'var(--accent-primary)' };
    case 'failed':
      return { background: 'var(--status-danger-bg)', color: 'var(--status-danger-fg)' };
    case 'cancelled':
      return { background: 'var(--surface-sunken)', color: 'var(--text-muted)' };
    default:
      return { background: 'var(--gray-100)', color: 'var(--text-body)' };
  }
}

export function JobProgressPage() {
  const { batchId } = useParams<{ batchId: string }>();
  const navigate = useNavigate();
  const { show } = useToast();
  const { data, isLoading, error } = usePollingJob(batchId ?? '');
  const [stopOpen, setStopOpen] = useState(false);
  const [stopReason, setStopReason] = useState('');

  const view = resolveViewState(batchId, data, isLoading, error);

  async function onRetry(doc: JobDocumentDto) {
    if (!batchId) return;
    const result = await retryDocument(batchId, doc.document_id);
    show(
      result.ok
        ? { variant: 'info', title: `Retrying ${doc.filename}…` }
        : { variant: 'error', title: 'Retry failed', message: result.error.message }
    );
  }

  async function onStop() {
    if (!batchId) return;
    setStopOpen(false);
    const result = await stopBatch(batchId);
    show(
      result.ok
        ? { variant: 'info', title: 'Batch stopped.' }
        : { variant: 'error', title: 'Could not stop batch', message: result.error.message }
    );
  }

  return (
    <AppShell>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
        <h1 style={{ fontFamily: 'var(--font-display)', fontSize: 'var(--text-3xl)', color: 'var(--text-primary)', fontWeight: 600 }}>
          Batch Progress
        </h1>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
          <a href="/batches" onClick={(e) => { e.preventDefault(); navigate('/batches'); }} style={{ fontSize: 13, fontWeight: 600 }}>
            ← Back to Batch History
          </a>
          {view.kind === 'data' && !view.isTerminal && (
            <Button variant="danger" size="sm" onClick={() => setStopOpen(true)}>
              ■ Stop batch
            </Button>
          )}
        </div>
      </div>

      {view.kind === 'loading' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <Skeleton height={200} radius="var(--radius-lg)" />
          <Skeleton height={260} radius="var(--radius-lg)" />
        </div>
      )}

      {(view.kind === 'error-empty' || view.kind === 'missing-id') && (
        <ErrorState
          title="Couldn't load batch status"
          message={view.kind === 'error-empty' ? view.error.message : 'Missing batch id.'}
          correlationId={view.kind === 'error-empty' ? view.error.correlationId : undefined}
          onRetry={() => navigate(0)}
        />
      )}

      {view.kind === 'data' && (
        <ProgressBody
          job={view.data}
          percent={view.aggregate.percent}
          percentExact={view.aggregate.percentExact}
          total={view.aggregate.total}
          finished={view.aggregate.finished}
          failed={view.aggregate.failed}
          isTerminal={view.isTerminal}
          onRetry={onRetry}
          onViewResults={() => navigate(`/batches/${batchId}/results`)}
        />
      )}

      <Modal
        open={stopOpen}
        onClose={() => setStopOpen(false)}
        title="Stop this batch?"
        subtitle="Processing will halt immediately for all remaining documents."
        footer={
          <>
            <Button variant="secondary" fullWidth onClick={() => setStopOpen(false)}>
              Cancel
            </Button>
            <Button variant="danger" fullWidth onClick={() => void onStop()}>
              Stop batch
            </Button>
          </>
        }
      >
        <Input label="Reason (optional)" placeholder="e.g. Wrong corpus selected" value={stopReason} onChange={(e) => setStopReason(e.target.value)} />
      </Modal>
    </AppShell>
  );
}

function ProgressBody({
  job,
  percent,
  percentExact,
  total,
  finished,
  failed,
  isTerminal,
  onRetry,
  onViewResults,
}: {
  job: JobStatusDto;
  percent: number;
  percentExact: number;
  total: number;
  finished: number;
  failed: number;
  isTerminal: boolean;
  onRetry: (doc: JobDocumentDto) => void;
  onViewResults: () => void;
}) {
  const noCandidates = job.status === 'NoCandidates' || job.documents.every((d) => d.status === 'NoCandidates');
  const isBatchFailed = job.status === 'Failed';
  const overallStageIdx = isBatchFailed ? 0 : job.status === 'Completed' ? STAGES.length : Math.min(STAGES.length - 1, Math.floor((percent / 100) * STAGES.length));
  const elapsedMs = useElapsed(job.created_at, isTerminal);
  const isSingleFile = job.documents.length === 1;
  const eta = isSingleFile ? estimateEtaFromRate(elapsedMs, percentExact) : formatEta(elapsedMs, finished, total);

  return (
    <>
      <div
        style={{
          background: 'var(--surface-card)',
          border: '1px solid var(--border-subtle)',
          borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--shadow-xs)',
          padding: 28,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 22 }}>
          <div>
            <div style={{ fontWeight: 700, fontSize: 'var(--text-lg)', color: 'var(--text-primary)', marginBottom: 4 }}>
              {job.batch_name ?? job.batch_id}
            </div>
            <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>
              batch_id {job.batch_id}
            </div>
          </div>
          <BatchStatusBadge status={job.status} />
        </div>

        {job.created_at && (
          <div
            style={{
              display: 'flex',
              gap: 32,
              marginBottom: 22,
              padding: '14px 16px',
              borderRadius: 'var(--radius-md)',
              background: 'var(--surface-sunken)',
              fontSize: 'var(--text-xs)',
            }}
            aria-live="polite"
          >
            <MetaItem label="Started" value={formatStarted(job.created_at)} />
            <MetaItem label="Elapsed" value={formatElapsed(elapsedMs)} ariaLive="off" />
            <MetaItem label="Est. remaining" value={eta ?? 'Calculating…'} muted={!eta} />
            <MetaItem label="Complete" value={`${percentExact.toFixed(1)}%`} />
          </div>
        )}

        {/* Pipeline stepper */}
        <div style={{ display: 'flex', alignItems: 'center', marginBottom: 22 }}>
          {STAGES.map((label, i) => {
            const done = !isBatchFailed && i < overallStageIdx;
            return (
              <div key={label} style={{ display: 'flex', alignItems: 'center', flex: 1 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexShrink: 0 }}>
                  <span
                    style={{
                      width: 20,
                      height: 20,
                      borderRadius: 6,
                      background: done ? 'var(--accent-primary)' : 'var(--gray-100)',
                      color: done ? '#fff' : 'var(--text-muted)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      fontSize: 11,
                      flexShrink: 0,
                    }}
                  >
                    {done ? '✓' : ''}
                  </span>
                  <span style={{ fontSize: 12.5, fontWeight: 600, color: done ? 'var(--text-primary)' : 'var(--text-muted)' }}>{label}</span>
                </div>
                {i < STAGES.length - 1 && (
                  <div style={{ flex: 1, height: 2, background: done ? 'var(--accent-primary)' : 'var(--border-subtle)', margin: '0 10px' }} />
                )}
              </div>
            );
          })}
        </div>

        <div style={{ display: 'flex', gap: 48, marginBottom: 22 }}>
          <Metric label="Documents" value={total} />
          <Metric label="Completed" value={finished} />
          <Metric label="Failed" value={failed} />
        </div>

        <ProgressBar pct={isBatchFailed ? 0 : percent} />

        {isTerminal ? (
          <>
            {noCandidates && (
              <div style={{ marginTop: 16, padding: '14px 16px', borderRadius: 'var(--radius-md)', background: 'var(--status-warning-bg)', color: 'var(--status-warning-fg)', fontSize: 13 }}>
                No patentable candidates found in this batch.
              </div>
            )}
            <div style={{ marginTop: 16 }}>
              <Button variant="primary" onClick={onViewResults}>
                View Results
              </Button>
            </div>
          </>
        ) : (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 12, color: 'var(--text-muted)', fontSize: 'var(--text-xs)' }}>
            <Spinner size={14} />
            Updating every 2 seconds
          </div>
        )}
      </div>

      {isSingleFile ? <SingleFileView job={job} isTerminal={isTerminal} /> : <MultiFileView job={job} onRetry={onRetry} />}
    </>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', marginBottom: 4 }}>{label}</div>
      <div style={{ fontWeight: 700, fontSize: 'var(--text-md)', color: 'var(--text-primary)' }}>{value}</div>
    </div>
  );
}

function MetaItem({
  label,
  value,
  muted,
  ariaLive,
}: {
  label: string;
  value: string;
  muted?: boolean;
  ariaLive?: 'off' | 'polite';
}) {
  return (
    <div style={{ display: 'flex', gap: 6, alignItems: 'baseline' }}>
      <span style={{ color: 'var(--text-muted)', fontWeight: 600 }}>{label}:</span>
      <span
        style={{ color: muted ? 'var(--text-muted)' : 'var(--text-primary)', fontWeight: 600 }}
        aria-live={ariaLive}
      >
        {value}
      </span>
    </div>
  );
}


function SingleFileView({ job, isTerminal }: { job: JobStatusDto; isTerminal: boolean }) {
  const doc = job.documents[0];
  if (!doc) return null;

  const stages = getDocumentStages(doc);
  const activeStage = stages.find((s) => s.state === 'active');
  const isFailed = doc.status === 'Failed';

  if (isFailed) {
    return (
      <div
        style={{
          background: 'var(--surface-card)',
          border: '1px solid var(--border-subtle)',
          borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--shadow-xs)',
          padding: 28,
        }}
        role="status"
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16, alignItems: 'flex-start' }}>
          <div style={{ fontFamily: 'var(--font-display)', fontSize: 'var(--text-2xl)', fontWeight: 600, color: 'var(--text-primary)' }}>
            Processing failed
          </div>
          <div style={{ fontSize: 'var(--text-sm)', color: 'var(--text-body)' }}>
            We couldn't finish this document. The reason reported by the pipeline is below.
          </div>
          <InlineMessage variant="danger">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <span style={{ fontWeight: 600 }}>Reason:</span>
              <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-sm)' }}>
                {doc.error || 'No further detail was reported.'}
              </span>
            </div>
          </InlineMessage>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginTop: 8 }}>
            {stages.map((s, si) => (
              <span
                key={STAGES[si]}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  padding: '4px 9px',
                  borderRadius: 'var(--radius-full)',
                  fontSize: 10.5,
                  fontWeight: 600,
                  ...stagePillStyle(s.state),
                }}
              >
                {STAGES[si]}
              </span>
            ))}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div
      style={{
        background: 'var(--surface-card)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-xs)',
        padding: 28,
      }}
      role="status"
      aria-live="polite"
    >
      {activeStage && (
        <div style={{ textAlign: 'center', padding: '32px 0' }}>
          <div
            style={{
              fontFamily: 'var(--font-display)',
              fontSize: 'var(--text-3xl)',
              fontWeight: 700,
              color: 'var(--text-primary)',
              marginBottom: 12,
            }}
          >
            {activeStage.stage}
          </div>
          <div style={{ fontSize: 'var(--text-md)', color: 'var(--text-muted)', marginBottom: 20 }}>
            {STAGE_DESCRIPTIONS[activeStage.stage as UserStage]}
          </div>
          {!isTerminal && <Spinner size={32} />}
        </div>
      )}
    </div>
  );
}

function DocumentRow({
  doc,
  isLast,
  onRetry,
}: {
  doc: JobDocumentDto;
  isLast: boolean;
  onRetry: (doc: JobDocumentDto) => void;
}) {
  const stages = getDocumentStages(doc);

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
        padding: '16px 0',
        borderBottom: isLast ? 'none' : '1px solid var(--border-subtle)',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
        <div style={{ fontWeight: 600, color: 'var(--text-primary)', fontSize: 'var(--text-sm)' }}>
          {doc.filename}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          {doc.status === 'Failed' && (
            <button
              onClick={() => onRetry(doc)}
              style={{
                fontSize: 12,
                fontWeight: 600,
                color: 'var(--accent-primary)',
                background: 'none',
                border: 'none',
                cursor: 'pointer',
              }}
            >
              Retry
            </button>
          )}
          <DocumentStatusBadge status={doc.status} />
        </div>
      </div>
      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
        {stages.map((s, si) => (
          <span
            key={STAGES[si]}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              padding: '4px 9px',
              borderRadius: 'var(--radius-full)',
              fontSize: 10.5,
              fontWeight: 600,
              ...stagePillStyle(s.state),
            }}
          >
            {STAGES[si]}
          </span>
        ))}
      </div>
      <ProgressBar pct={getDocumentPercent(doc)} height={5} />
      {doc.error && (
        <InlineMessage variant="danger">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontWeight: 600 }}>Reason:</span>
            <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-sm)' }}>
              {doc.error}
            </span>
          </div>
        </InlineMessage>
      )}
    </div>
  );
}

function MultiFileView({ job, onRetry }: { job: JobStatusDto; onRetry: (doc: JobDocumentDto) => void }) {
  const [expanded, setExpanded] = useState(job.documents.length <= 5);
  const counts = stageRollupCounts(job);

  return (
    <div
      style={{
        background: 'var(--surface-card)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-xs)',
        padding: 28,
      }}
    >
      <div
        style={{
          fontFamily: 'var(--font-display)',
          fontSize: 'var(--text-lg)',
          fontWeight: 700,
          color: 'var(--text-primary)',
          marginBottom: 16,
        }}
      >
        Processing stages
      </div>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 24 }}>
        {Object.entries(counts).map(([stage, count]) => (
          <span
            key={stage}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              padding: '6px 12px',
              borderRadius: 'var(--radius-full)',
              fontSize: 'var(--text-xs)',
              fontWeight: 600,
              background: count > 0 ? 'var(--accent-primary-subtle)' : 'var(--surface-sunken)',
              color: count > 0 ? 'var(--accent-primary)' : 'var(--text-muted)',
            }}
          >
            {stage}: {count}
          </span>
        ))}
      </div>

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12 }}>
        <div
          style={{
            fontFamily: 'var(--font-display)',
            fontSize: 'var(--text-md)',
            fontWeight: 700,
            color: 'var(--text-primary)',
          }}
        >
          Documents
        </div>
        {job.documents.length > 5 && (
          <button
            onClick={() => setExpanded(!expanded)}
            aria-expanded={expanded}
            aria-controls="document-list"
            style={{
              fontSize: 'var(--text-xs)',
              fontWeight: 600,
              color: 'var(--accent-primary)',
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              gap: 6,
              minHeight: 44,
              minWidth: 44,
            }}
          >
            {expanded ? 'Collapse' : 'Expand'}
            <span
              style={{
                display: 'inline-block',
                transform: expanded ? 'rotate(180deg)' : 'rotate(0deg)',
                transition: 'transform var(--duration-base) var(--ease-standard)',
              }}
            >
              ▼
            </span>
          </button>
        )}
      </div>

      {expanded && (
        <div id="document-list">
          {job.documents.map((doc, i) => (
            <DocumentRow key={doc.document_id} doc={doc} isLast={i === job.documents.length - 1} onRetry={onRetry} />
          ))}
        </div>
      )}
    </div>
  );
}
