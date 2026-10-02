import { useState } from 'react';
import { Badge, Button, IconBranch, InlineMessage, Spinner } from '../../shared/ds';
import { SectionCard } from '../../shared/layout/PageHeader';
import { relativeTime } from '../../shared/utils';
import { useOpenInNewAnalysis } from '../upload/savedRepositories';
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

/** What a row can do; shared by every row in the list. */
interface RowActions {
  downloading: string | null;
  selected: ReadonlySet<string>;
  onDownload: (clone: RepositoryCloneDto) => void;
  onToggle: (clone: RepositoryCloneDto) => void;
  onAnalyze: (clones: RepositoryCloneDto[]) => void;
}

function cloneLabel(clone: RepositoryCloneDto): string {
  return `${clone.owner}/${clone.repository} (${clone.branch})`;
}

function SelectBox({ clone, actions }: { clone: RepositoryCloneDto; actions: RowActions }) {
  const stored = clone.status === 'stored';
  return (
    <input
      type="checkbox"
      checked={stored && actions.selected.has(clone.clone_id)}
      disabled={!stored}
      onChange={() => actions.onToggle(clone)}
      aria-label={`Select ${cloneLabel(clone)} for analysis`}
      style={{ width: 16, height: 16, flexShrink: 0, cursor: stored ? 'pointer' : 'not-allowed', accentColor: 'var(--accent-primary)' }}
    />
  );
}

function StoredActions({ clone, actions }: { clone: RepositoryCloneDto; actions: RowActions }) {
  const downloading = actions.downloading === clone.clone_id;
  return (
    <span style={{ display: 'inline-flex', gap: 8 }}>
      <Button size="sm" variant="primary" onClick={() => actions.onAnalyze([clone])} title={`Analyze ${cloneLabel(clone)}`}>
        Analyze
      </Button>
      <Button size="sm" variant="secondary" disabled={downloading} onClick={() => actions.onDownload(clone)}>
        {downloading ? 'Downloading…' : 'Download .zip'}
      </Button>
    </span>
  );
}

function CloneRow({ clone, actions }: { clone: RepositoryCloneDto; actions: RowActions }) {
  return (
    <li style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 4px', borderTop: '1px solid var(--border-subtle)' }}>
      <SelectBox clone={clone} actions={actions} />
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
      {clone.status === 'stored' && <StoredActions clone={clone} actions={actions} />}
    </li>
  );
}

interface PanelBodyProps {
  clones: Loadable<RepositoryCloneDto[]>;
  actions: RowActions;
  onRetry: () => void;
}

function PanelBody({ clones, actions, onRetry }: PanelBodyProps) {
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
        <CloneRow key={clone.clone_id} clone={clone} actions={actions} />
      ))}
    </ul>
  );
}

function selectedStoredClones(clones: Loadable<RepositoryCloneDto[]>, selected: ReadonlySet<string>): RepositoryCloneDto[] {
  if (clones.status !== 'loaded') return [];
  return clones.data.filter((c) => c.status === 'stored' && selected.has(c.clone_id));
}

function toggled(selected: ReadonlySet<string>, id: string): Set<string> {
  const next = new Set(selected);
  if (!next.delete(id)) next.add(id);
  return next;
}

interface ClonesPanelProps {
  clones: Loadable<RepositoryCloneDto[]>;
  downloading: string | null;
  onDownload: (clone: RepositoryCloneDto) => Promise<CloneActionResult>;
  onRetry: () => void;
}

export function ClonesPanel({ clones, downloading, onDownload, onRetry }: ClonesPanelProps) {
  const [error, setError] = useState<ScanError | undefined>();
  const [selected, setSelected] = useState<ReadonlySet<string>>(() => new Set());
  const openInNewAnalysis = useOpenInNewAnalysis();

  async function handleDownload(clone: RepositoryCloneDto) {
    setError(undefined);
    const result = await onDownload(clone);
    if (!result.ok) setError(result.error);
  }

  const chosen = selectedStoredClones(clones, selected);
  const actions: RowActions = {
    downloading,
    selected,
    onDownload: (c) => void handleDownload(c),
    onToggle: (c) => setSelected((current) => toggled(current, c.clone_id)),
    onAnalyze: openInNewAnalysis,
  };

  const count = clones.status === 'loaded' ? clones.data.length : undefined;
  const headerActions = (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      {count !== undefined && <Badge tone="neutral">{count} saved</Badge>}
      {chosen.length > 0 && (
        <Button size="sm" variant="primary" onClick={() => openInNewAnalysis(chosen)}>
          Analyze selected ({chosen.length})
        </Button>
      )}
      <Button size="sm" variant="ghost" onClick={onRetry}>
        Refresh
      </Button>
    </span>
  );

  return (
    <SectionCard
      title="Saved repositories"
      subtitle="Branches saved by your team as folders. Saving a branch again replaces its folder. Select one or more to start a new analysis."
      actions={headerActions}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        {error && (
          <InlineMessage variant="danger" correlationId={error.correlationId}>
            {error.message}
          </InlineMessage>
        )}
        <PanelBody clones={clones} actions={actions} onRetry={onRetry} />
      </div>
    </SectionCard>
  );
}
