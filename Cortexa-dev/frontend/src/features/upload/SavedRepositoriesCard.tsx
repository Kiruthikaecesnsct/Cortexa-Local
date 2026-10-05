import { useState } from 'react';
import { Badge, IconBranch, Spinner } from '../../shared/ds';
import { SectionCard } from '../../shared/layout/PageHeader';
import { azureDevOpsCloneApi, githubCloneApi, type CloneApi } from '../scan/cloneApi';
import { SavedRepositoryFileTree } from './SavedRepositoryFileTree';
import { savedRepositoryKey, type SavedRepositoryProvider, type SavedRepositorySelection } from './savedRepositories';

const PROVIDER_LABELS: Record<SavedRepositoryProvider, string> = {
  github: 'GitHub',
  'azure-devops': 'Azure DevOps',
};

const PROVIDER_APIS: Record<SavedRepositoryProvider, CloneApi> = {
  github: githubCloneApi,
  'azure-devops': azureDevOpsCloneApi,
};

interface SavedRepositoriesCardProps {
  repositories: SavedRepositorySelection[];
  maxCount: number;
  onRemove: (key: string) => void;
  onSelectFiles: (key: string, files: string[] | undefined) => void;
}

type FilesState = { status: 'loading' } | { status: 'error'; message: string } | { status: 'loaded'; files: string[] };

function selectionSummary(repo: SavedRepositorySelection, filesState: FilesState | undefined): string | null {
  if (repo.selectedFiles === undefined) return null;
  const total = filesState?.status === 'loaded' ? filesState.files.length : undefined;
  return total === undefined ? `${repo.selectedFiles.length} file(s) selected` : `${repo.selectedFiles.length}/${total} files selected`;
}

function SavedRepositoryRow({
  repo,
  last,
  expanded,
  filesState,
  onRemove,
  onToggleExpand,
  onSelectFiles,
}: {
  repo: SavedRepositorySelection;
  last: boolean;
  expanded: boolean;
  filesState: FilesState | undefined;
  onRemove: (key: string) => void;
  onToggleExpand: (repo: SavedRepositorySelection) => void;
  onSelectFiles: (key: string, files: string[] | undefined) => void;
}) {
  const key = savedRepositoryKey(repo);
  const label = `${repo.owner}/${repo.repository} (${repo.branch})`;
  const summary = selectionSummary(repo, filesState);

  return (
    <li
      style={{
        padding: '12px 0',
        borderBottom: last ? 'none' : '1px solid var(--border-subtle)',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <span style={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', gap: 8, fontFamily: 'var(--font-mono)', fontSize: 'var(--text-sm)' }}>
          <Badge tone="neutral">{PROVIDER_LABELS[repo.provider]}</Badge>
          <span style={{ color: 'var(--text-muted)' }}>{repo.owner} /</span>
          <strong style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{repo.repository}</strong>
          <Badge tone="info" icon={<IconBranch size={11} />}>
            {repo.branch}
          </Badge>
        </span>
        <button
          type="button"
          onClick={() => onToggleExpand(repo)}
          style={{
            background: 'none',
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-sm)',
            padding: '4px 10px',
            fontSize: 12.5,
            color: 'var(--text-primary)',
            cursor: 'pointer',
            flexShrink: 0,
          }}
        >
          {expanded ? 'Hide files' : 'Select files'}
        </button>
        <button
          type="button"
          onClick={() => onRemove(key)}
          aria-label={`Remove ${label}`}
          style={{
            width: 28,
            height: 28,
            borderRadius: 'var(--radius-sm)',
            border: '1px solid var(--border-subtle)',
            background: 'var(--surface-card)',
            color: 'var(--status-danger-fg)',
            cursor: 'pointer',
            flexShrink: 0,
          }}
        >
          ✕
        </button>
      </div>
      {summary && <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>{summary}</div>}
      {expanded && (
        <div>
          {filesState?.status === 'loading' && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: 10 }}>
              <Spinner size={16} />
              <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)' }}>Loading files…</span>
            </div>
          )}
          {filesState?.status === 'error' && (
            <div style={{ color: 'var(--status-danger-fg)', fontSize: 13, padding: 10 }}>{filesState.message}</div>
          )}
          {filesState?.status === 'loaded' && (
            <SavedRepositoryFileTree
              files={filesState.files}
              selectedFiles={repo.selectedFiles}
              onChange={(files) => onSelectFiles(key, files)}
            />
          )}
        </div>
      )}
    </li>
  );
}

/** Saved repository folders loaded from Source Connectors, analyzed as code when the batch starts. */
export function SavedRepositoriesCard({ repositories, maxCount, onRemove, onSelectFiles }: SavedRepositoriesCardProps) {
  const [expandedKey, setExpandedKey] = useState<string | null>(null);
  const [filesByKey, setFilesByKey] = useState<Record<string, FilesState>>({});

  if (repositories.length === 0) return null;

  const overCap = repositories.length > maxCount;

  async function toggleExpand(repo: SavedRepositorySelection) {
    const key = savedRepositoryKey(repo);
    if (expandedKey === key) {
      setExpandedKey(null);
      return;
    }
    setExpandedKey(key);
    if (filesByKey[key]) return;
    setFilesByKey((prev) => ({ ...prev, [key]: { status: 'loading' } }));
    const result = await PROVIDER_APIS[repo.provider].files(repo.owner, repo.repository, repo.branch);
    setFilesByKey((prev) => ({
      ...prev,
      [key]: result.ok ? { status: 'loaded', files: result.data.files } : { status: 'error', message: result.error.message },
    }));
  }

  return (
    <SectionCard
      title={`Saved repositories (${repositories.length})`}
      subtitle="Each saved folder is analyzed as code in this batch. Select files to narrow what gets analyzed."
    >
      {overCap && (
        <div style={{ color: 'var(--status-danger-fg)', fontSize: 13, marginBottom: 8 }}>
          At most {maxCount} saved repositories can be analyzed in one batch. Remove some to continue.
        </div>
      )}
      <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {repositories.map((repo, i) => {
          const key = savedRepositoryKey(repo);
          return (
            <SavedRepositoryRow
              key={key}
              repo={repo}
              last={i === repositories.length - 1}
              expanded={expandedKey === key}
              filesState={filesByKey[key]}
              onRemove={onRemove}
              onToggleExpand={toggleExpand}
              onSelectFiles={onSelectFiles}
            />
          );
        })}
      </ul>
    </SectionCard>
  );
}
