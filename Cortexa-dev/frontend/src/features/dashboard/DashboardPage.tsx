import { useNavigate } from 'react-router-dom';
import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader, SectionCard } from '../../shared/layout/PageHeader';
import { Button, StatCard, EmptyState, ErrorState, Skeleton, IconUpload, IconHistory, IconChevronRight } from '../../shared/ds';
import { BatchStatusBadge } from '../../shared/ds/badges';
import { useSession } from '../../core/auth/useSession';
import { relativeTime } from '../../shared/utils';
import { usePollingBatchList } from '../jobs/usePollingBatchList';
import { computeDashboardStats, routeForBatch } from './utils';

export function DashboardPage() {
  const navigate = useNavigate();
  const { hasPermission } = useSession();
  const canUpload = hasPermission('jobs:submit');
  const { data, isLoading, error, refetch } = usePollingBatchList();

  const stats = computeDashboardStats(data ?? []);
  const recent = [...(data ?? [])]
    .sort((a, b) => new Date(b.created_at).getTime() - new Date(a.created_at).getTime())
    .slice(0, 6);

  return (
    <AppShell>
      <PageHeader
        title="Dashboard"
        actions={
          canUpload ? (
            <Button variant="primary" onClick={() => navigate('/batches/new')}>
              + New Analysis
            </Button>
          ) : undefined
        }
      />

      {isLoading && !data ? (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 16 }}>
            {Array.from({ length: 4 }).map((_, i) => (
              <StatCard key={i} label="" value="" loading />
            ))}
          </div>
          <Skeleton height={220} radius="var(--radius-lg)" />
        </>
      ) : error ? (
        <ErrorState message={error.message} correlationId={error.correlationId} onRetry={() => void refetch()} />
      ) : (data?.length ?? 0) === 0 ? (
        <EmptyState
          title="No batches yet"
          message="Upload a document or connect a repo to run your first analysis."
          action={canUpload ? { label: 'New Analysis', onClick: () => navigate('/batches/new') } : undefined}
        />
      ) : (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 16 }}>
            <StatCard label="Total Batches" value={String(stats.total)} sublabel="all time" />
            <StatCard label="Running" value={String(stats.running)} sublabel="in the pipeline" />
            <StatCard label="Completed" value={String(stats.completed)} sublabel="ready to review" />
            <StatCard label="Failed" value={String(stats.failed)} sublabel="need attention" />
          </div>

          <div>
            <div
              style={{
                fontSize: 11,
                fontWeight: 600,
                letterSpacing: '0.04em',
                textTransform: 'uppercase',
                color: 'var(--text-muted)',
                marginBottom: 10,
              }}
            >
              Quick Actions
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
              {canUpload && (
                <QuickAction
                  icon={<IconUpload />}
                  title="Upload Documents"
                  desc="Start a new patent analysis from papers, theses, or code."
                  onClick={() => navigate('/batches/new')}
                />
              )}
              <QuickAction
                icon={<IconHistory />}
                title="Batch History"
                desc="Browse and filter every analysis batch you have run."
                onClick={() => navigate('/batches')}
              />
            </div>
          </div>

          <SectionCard title="Recent Batches">
            {recent.map((b, i) => (
              <div
                key={b.batch_id}
                onClick={() => navigate(routeForBatch(b))}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '16px 0',
                  borderBottom: i === recent.length - 1 ? 'none' : '1px solid var(--border-subtle)',
                  cursor: 'pointer',
                }}
              >
                <div>
                  <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginBottom: 6 }}>{b.name ?? b.batch_id}</div>
                  <div style={{ fontSize: 13, color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>
                    {b.completed_count}/{b.document_count} docs · {relativeTime(b.created_at)}
                  </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <BatchStatusBadge status={b.status} />
                  <span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}>
                    <IconChevronRight size={16} />
                  </span>
                </div>
              </div>
            ))}
          </SectionCard>
        </>
      )}
    </AppShell>
  );
}

function QuickAction({
  icon,
  title,
  desc,
  onClick,
}: {
  icon: React.ReactNode;
  title: string;
  desc: string;
  onClick: () => void;
}) {
  return (
    <div
      onClick={onClick}
      style={{
        cursor: 'pointer',
        background: 'var(--surface-card)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-xs)',
        padding: 18,
        display: 'flex',
        alignItems: 'center',
        gap: 14,
      }}
    >
      <div
        style={{
          width: 38,
          height: 38,
          borderRadius: 9,
          background: 'var(--accent-primary-subtle)',
          color: 'var(--accent-primary)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          flexShrink: 0,
        }}
      >
        {icon}
      </div>
      <div>
        <div style={{ fontWeight: 600, color: 'var(--text-primary)', fontSize: 14 }}>{title}</div>
        <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>{desc}</div>
      </div>
    </div>
  );
}
