import { useMemo, useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader, SectionCard } from '../../shared/layout/PageHeader';
import { Button, Tabs, Modal, ErrorState, Skeleton, Checkbox } from '../../shared/ds';
import { Spinner } from '../../shared/ds/feedback';
import { BatchStatusBadge } from '../../shared/ds/badges';
import { useToast } from '../../shared/ds/Toast';
import { useSession } from '../../core/auth/useSession';
import { relativeTime, shortDate } from '../../shared/utils';
import { usePollingBatchList } from './usePollingBatchList';
import { useBatchDeletion } from './useBatchDeletion';
import type { BatchSummaryDto } from './jobTypes';
import { routeForBatch } from '../dashboard/utils';

const TABS = ['All', 'Running', 'Completed', 'Failed', 'Cancelled'];

function matchesTab(b: BatchSummaryDto, tab: string): boolean {
  if (tab === 'All') return true;
  if (tab === 'Failed') return b.status === 'Failed' || b.status === 'PartiallyFailed';
  return b.status === tab;
}

export function JobHistoryPage() {
  const navigate = useNavigate();
  const { show } = useToast();
  const { hasPermission } = useSession();
  const canUpload = hasPermission('jobs:submit');
  const { data, isLoading, error, refetch } = usePollingBatchList();
  const deletion = useBatchDeletion({ refetch, onToast: show });

  const [tab, setTab] = useState('All');
  const [selected, setSelected] = useState<string[]>([]);
  const [confirm, setConfirm] = useState<{ ids: string[]; names: string } | null>(null);
  const selectAllRef = useRef<HTMLDivElement>(null);
  const lastFocusRef = useRef<HTMLElement | null>(null);

  const filtered = useMemo(() => (data ?? []).filter((b) => matchesTab(b, tab)), [data, tab]);
  const allChecked = filtered.length > 0 && filtered.every((b) => selected.includes(b.batch_id));

  function toggle(id: string) {
    setSelected((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function toggleAll() {
    setSelected(allChecked ? [] : filtered.map((b) => b.batch_id));
  }

  function askDelete(ids: string[]) {
    const names = ids
      .map((id) => data?.find((b) => b.batch_id === id)?.name ?? id)
      .join(', ');
    setConfirm({ ids, names });
  }

  async function runDelete() {
    if (!confirm) return;
    const ids = confirm.ids;
    lastFocusRef.current = document.activeElement as HTMLElement;
    await deletion.run(ids);
    setSelected((prev) => prev.filter((id) => !ids.includes(id)));
    setConfirm(null);
  }

  useEffect(() => {
    if (!deletion.isRunning && !confirm && lastFocusRef.current) {
      if (filtered.length > 0 && selectAllRef.current) {
        const firstCheckbox = selectAllRef.current.querySelector('label') as HTMLElement;
        firstCheckbox?.focus();
      } else {
        const header = document.querySelector('h1') as HTMLElement;
        header?.focus();
      }
      lastFocusRef.current = null;
    }
  }, [deletion.isRunning, confirm, filtered.length]);

  return (
    <AppShell>
      <PageHeader
        title="Batch History"
        actions={
          canUpload ? (
            <Button variant="primary" onClick={() => navigate('/batches/new')}>
              + New Analysis
            </Button>
          ) : undefined
        }
      />

      <SectionCard>
        <Tabs tabs={TABS} active={tab} onChange={setTab} />

        {isLoading && !data ? (
          <div style={{ marginTop: 16, display: 'flex', flexDirection: 'column', gap: 10 }}>
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} />
            ))}
          </div>
        ) : error ? (
          <div style={{ marginTop: 16 }}>
            <ErrorState title="Couldn't load batches" message={error.message} correlationId={error.correlationId} onRetry={() => void refetch()} />
          </div>
        ) : filtered.length === 0 ? (
          <div style={{ padding: '40px 0', textAlign: 'center', color: 'var(--text-muted)', fontSize: 14 }}>
            No batches match this filter.
          </div>
        ) : (
          <>
            <div
              ref={selectAllRef}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 14,
                marginTop: 14,
                padding: '10px 12px',
                borderRadius: 'var(--radius-sm)',
                background: 'var(--surface-sunken)',
              }}
            >
              <Checkbox label="Select all" checked={allChecked} onChange={toggleAll} disabled={deletion.isRunning} />
              <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>{selected.length} selected</span>
              {selected.length > 0 && (
                <div style={{ marginLeft: 'auto' }}>
                  <Button variant="danger" size="sm" onClick={() => askDelete(selected)} disabled={deletion.isRunning}>
                    Delete ({selected.length})
                  </Button>
                </div>
              )}
            </div>

            <div style={{ marginTop: 4 }}>
              {filtered.map((b, i) => {
                const isPending = deletion.statuses[b.batch_id] === 'pending';
                const isOtherRunning = deletion.isRunning && !isPending;
                return (
                  <div
                    key={b.batch_id}
                    onClick={() => {
                      if (!isPending) navigate(routeForBatch(b));
                    }}
                    aria-busy={isPending}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '16px 0',
                      borderBottom: i === filtered.length - 1 ? 'none' : '1px solid var(--border-subtle)',
                      cursor: isPending ? 'default' : 'pointer',
                      opacity: isPending ? 0.5 : 1,
                      pointerEvents: isPending ? 'none' : undefined,
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                      <span onClick={(e) => e.stopPropagation()}>
                        <Checkbox
                          checked={selected.includes(b.batch_id)}
                          onChange={() => toggle(b.batch_id)}
                          disabled={deletion.isRunning}
                        />
                      </span>
                      <div>
                        <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginBottom: 4 }}>{b.name ?? b.batch_id}</div>
                        <div style={{ fontSize: 12, color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>
                          {b.completed_count}/{b.document_count} docs · {shortDate(b.created_at)} · {relativeTime(b.created_at)}
                        </div>
                      </div>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                      <BatchStatusBadge status={b.status} />
                      {isPending ? (
                        <div
                          role="status"
                          aria-label="Deleting batch"
                          style={{
                            width: 28,
                            height: 28,
                            borderRadius: 'var(--radius-sm)',
                            border: '1px solid var(--border-subtle)',
                            background: 'var(--surface-card)',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                          }}
                        >
                          <Spinner size={14} />
                        </div>
                      ) : (
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            askDelete([b.batch_id]);
                          }}
                          title="Delete batch"
                          disabled={isOtherRunning}
                          aria-disabled={isOtherRunning}
                          tabIndex={isOtherRunning ? -1 : undefined}
                          style={{
                            width: 28,
                            height: 28,
                            borderRadius: 'var(--radius-sm)',
                            border: '1px solid var(--border-subtle)',
                            background: 'var(--surface-card)',
                            color: 'var(--status-danger-fg)',
                            cursor: isOtherRunning ? 'default' : 'pointer',
                            opacity: isOtherRunning ? 0.5 : 1,
                          }}
                        >
                          ✕
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </>
        )}
      </SectionCard>

      <Modal
        open={confirm !== null}
        onClose={() => {
          if (!deletion.isRunning) setConfirm(null);
        }}
        title="Delete batches permanently?"
        subtitle={
          confirm
            ? `You are about to delete ${confirm.ids.length} batch${confirm.ids.length > 1 ? 'es' : ''}. This cannot be undone.`
            : ''
        }
        footer={
          <>
            <Button variant="secondary" fullWidth onClick={() => setConfirm(null)} disabled={deletion.isRunning}>
              Cancel
            </Button>
            <Button
              variant="danger"
              fullWidth
              onClick={() => void runDelete()}
              disabled={deletion.isRunning}
              icon={deletion.isRunning ? <Spinner size={14} /> : null}
            >
              {deletion.isRunning ? 'Deleting…' : 'Delete permanently'}
            </Button>
          </>
        }
      >
        <div
          role="dialog"
          aria-busy={deletion.isRunning}
          style={{ display: 'flex', flexDirection: 'column', gap: 12 }}
        >
          {confirm && (
            <div
              style={{
                fontFamily: 'var(--font-mono)',
                fontSize: 12,
                color: 'var(--text-body)',
                background: 'var(--surface-sunken)',
                borderRadius: 'var(--radius-sm)',
                padding: '10px 12px',
                wordBreak: 'break-word',
              }}
            >
              {confirm.names}
            </div>
          )}
          {deletion.isRunning && confirm && (
            <div
              role="status"
              aria-live="polite"
              aria-atomic="true"
              style={{
                fontSize: 12,
                color: 'var(--text-muted)',
              }}
            >
              {(() => {
                const total = confirm.ids.length;
                const done = confirm.ids.filter(
                  (id) => deletion.statuses[id] === 'success' || deletion.statuses[id] === 'error'
                ).length;
                if (total === 1) return 'Deleting…';
                return `Deleting ${done} of ${total}…`;
              })()}
            </div>
          )}
        </div>
      </Modal>
    </AppShell>
  );
}
