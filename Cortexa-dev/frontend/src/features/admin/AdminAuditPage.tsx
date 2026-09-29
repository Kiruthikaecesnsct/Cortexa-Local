import { useEffect, useState } from 'react';
import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader, SectionCard } from '../../shared/layout/PageHeader';
import { Button, Select, Skeleton, ErrorState } from '../../shared/ds';
import { relativeTime } from '../../shared/utils';
import { fetchAudit, type AuditPageDto } from './adminAuditRepository';

const EVENT_TYPES = ['', 'UserLogin', 'UserCreated', 'UserRoleChanged', 'BatchCreated', 'BatchDeleted', 'ConfigUpdated', 'PermissionSetChanged'];
const PAGE_SIZE = 20;

export function AdminAuditPage() {
  const [eventType, setEventType] = useState('');
  const [page, setPage] = useState(1);
  const [data, setData] = useState<AuditPageDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    const result = await fetchAudit({ eventType: eventType || undefined, page, size: PAGE_SIZE });
    if (result.ok) setData(result.data);
    else setError(result.error.message);
    setLoading(false);
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [eventType, page]);

  const totalPages = data ? Math.max(1, Math.ceil(data.total / (data.size || PAGE_SIZE))) : 1;

  return (
    <AppShell>
      <PageHeader title="Audit Log" />

      <SectionCard>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 16, flexWrap: 'wrap' }}>
          <span style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Event type</span>
          <div style={{ minWidth: 220 }}>
            <Select
              options={EVENT_TYPES.map((e) => ({ value: e, label: e || 'All events' }))}
              value={eventType}
              placeholder=""
              onChange={(e) => {
                setPage(1);
                setEventType(e.target.value);
              }}
            />
          </div>
          {data && (
            <span style={{ fontSize: 12, color: 'var(--text-muted)', marginLeft: 'auto' }}>
              {data.total} events total · page {data.page} of {totalPages}
            </span>
          )}
        </div>

        {loading && !data ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {Array.from({ length: 8 }).map((_, i) => (
              <Skeleton key={i} height={40} />
            ))}
          </div>
        ) : error ? (
          <ErrorState title="Couldn't load the audit log" message={error} onRetry={() => void load()} />
        ) : data && data.items.length === 0 ? (
          <div style={{ padding: '40px 0', textAlign: 'center', color: 'var(--text-muted)' }}>No audit events match this filter.</div>
        ) : (
          <>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: '1.4fr 1.2fr 1.6fr 1fr',
                gap: 12,
                paddingBottom: 12,
                borderBottom: '1px solid var(--border-subtle)',
                fontSize: 12,
                fontWeight: 600,
                color: 'var(--text-muted)',
              }}
            >
              <div>User</div>
              <div>Event</div>
              <div>Details</div>
              <div>When</div>
            </div>
            {(data?.items ?? []).map((a) => (
              <div
                key={a.id}
                style={{
                  display: 'grid',
                  gridTemplateColumns: '1.4fr 1.2fr 1.6fr 1fr',
                  gap: 12,
                  alignItems: 'center',
                  padding: '12px 0',
                  borderBottom: '1px solid var(--border-subtle)',
                  fontSize: 13,
                }}
              >
                <div style={{ color: 'var(--text-primary)', fontFamily: 'var(--font-mono)', fontSize: 12 }}>{a.userId}</div>
                <div style={{ fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--text-body)' }}>{a.eventType}</div>
                <div style={{ color: 'var(--text-muted)', fontSize: 12.5 }}>{a.details || '—'}</div>
                <div style={{ color: 'var(--text-muted)', fontSize: 12 }}>{relativeTime(a.createdDate)}</div>
              </div>
            ))}

            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 10, marginTop: 16 }}>
              <Button variant="secondary" size="sm" disabled={page <= 1 || loading} onClick={() => setPage((p) => Math.max(1, p - 1))}>
                Previous
              </Button>
              <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                {page} / {totalPages}
              </span>
              <Button variant="secondary" size="sm" disabled={page >= totalPages || loading} onClick={() => setPage((p) => p + 1)}>
                Next
              </Button>
            </div>
          </>
        )}
      </SectionCard>
    </AppShell>
  );
}
