import { useState } from 'react';
import { Badge, Button, IconBranch, InlineMessage, Spinner } from '../../shared/ds';
import { SectionCard } from '../../shared/layout/PageHeader';
import { relativeTime } from '../../shared/utils';
import { formatBytes } from '../upload/uploadValidation';
import { isInProgress, shortSha, statusLabel } from './cloneFormat';
import type { Loadable, RepositoryCloneDto, ScanError } from './scanTypes';
import type { CloneActionResult } from './useClones';

function CloneStatusView({ clone }: { clone: RepositoryCloneDto }) {
  if (isInProgress(clone)) {
    return (
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8, fontSize: 'var(--text-sm)', color: 'var(--accent-primary)' }}>
        <Spinner size={14} /> {statusLabel(clone)}…
      </span>
    );
  }
  return <Badge tone={clone.status === 'stored' ? 'success' : 'danger'}>{statusLabel(clone)}</Badge>;
}

function CloneMeta({ clone }: { clone: RepositoryCloneDto }) {
  const parts = [
    clone.size_bytes !== null ? formatBytes(clone.size_bytes) : null,
    clone.commit_sha ? `commit ${shortSha(clone.commit_sha)}` : null,
    `started ${relativeTime(clone.created_at)}`,
  ].filter(Boolean);
  return <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>{parts.join(' · ')}</span>;
}

interface CloneRowProps {
  clone: RepositoryCloneDto;
  downloading: boolean;
  onDownload: (clone: RepositoryCloneDto) => void;
}

function CloneRow({ clone, downloading, onDownload }: CloneRowProps) {
  return (
    <li style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 4px', borderTop: '1px solid var(--border-subtle)' }}>
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={{ display: 'flex', alignItems: 'center', gap: 8, fontFamily: 'var(--font-mono)', fontSize: 'var(--text-sm)' }}>
          <span style={{ color: 'var(--text-muted)' }}>{clone.owner} /</span>
          <strong>{clone.repository}</strong>
          <Badge tone="info" icon={<IconBranch size={11} />}>
            {clone.branch}
          </Badge>
        </span>
        <CloneMeta clone={clone} />
        {clone.status === 'failed' && clone.error && <span style={{ fontSize: 'var(--text-xs)', color: 'var(--status-danger-fg)' }}>{clone.error}</span>}
      </div>
      <CloneStatusView clone={clone} />
      {clone.status === 'stored' && (
        <Button size="sm" variant="secondary" disabled={downloading} onClick={() => onDownload(clone)}>
          {downloading ? 'Downloading…' : 'Download .zip'}
        </Button>
      )}
    </li>
  );
}

interface PanelBodyProps {
  clones: Loadable<RepositoryCloneDto[]>;
  downloading: string | null;
  onDownload: (clone: RepositoryCloneDto) => void;
  onRetry: () => void;
}

function PanelBody({ clones, downloading, onDownload, onRetry }: PanelBodyProps) {
  if (clones.status === 'loading' || clones.status === 'idle') {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, color: 'var(--text-muted)', fontSize: 'var(--text-sm)' }}>
        <Spinner size={16} /> Loading your saved repositories…
      </div>
    );
  }
  if (clones.status === 'error') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10, alignItems: 'flex-start' }}>
        <InlineMessage variant="danger" correlationId={clones.error.correlationId}>
          {clones.error.message}
        </InlineMessage>
        <Button size="sm" variant="secondary" onClick={onRetry}>
          Try again
        </Button>
      </div>
    );
  }
  if (clones.data.length === 0) {
    return (
      <div style={{ color: 'var(--text-muted)', fontSize: 'var(--text-sm)' }}>
        Nothing saved yet. Choose a repository and branch above, then select <strong>Save repository</strong>.
      </div>
    );
  }
  return (
    <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {clones.data.map((clone) => (
        <CloneRow key={clone.clone_id} clone={clone} downloading={downloading === clone.clone_id} onDownload={onDownload} />
      ))}
    </ul>
  );
}

interface ClonesPanelProps {
  clones: Loadable<RepositoryCloneDto[]>;
  downloading: string | null;
  onDownload: (clone: RepositoryCloneDto) => Promise<CloneActionResult>;
  onRetry: () => void;
}

export function ClonesPanel({ clones, downloading, onDownload, onRetry }: ClonesPanelProps) {
  const [error, setError] = useState<ScanError | undefined>();

  async function handleDownload(clone: RepositoryCloneDto) {
    setError(undefined);
    const result = await onDownload(clone);
    if (!result.ok) setError(result.error);
  }

  const count = clones.status === 'loaded' ? clones.data.length : undefined;
  const actions = (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      {count !== undefined && <Badge tone="neutral">{count} saved</Badge>}
      <Button size="sm" variant="ghost" onClick={onRetry}>
        Refresh
      </Button>
    </span>
  );

  return (
    <SectionCard title="Saved repositories" subtitle="Branches saved by your team. Saving a branch again replaces its zip." actions={actions}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        {error && (
          <InlineMessage variant="danger" correlationId={error.correlationId}>
            {error.message}
          </InlineMessage>
        )}
        <PanelBody clones={clones} downloading={downloading} onDownload={(c) => void handleDownload(c)} onRetry={onRetry} />
      </div>
    </SectionCard>
  );
}
