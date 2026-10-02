import { Badge, IconBranch } from '../../shared/ds';
import { SectionCard } from '../../shared/layout/PageHeader';
import { savedRepositoryKey, type SavedRepositoryProvider, type SavedRepositorySelection } from './savedRepositories';

const PROVIDER_LABELS: Record<SavedRepositoryProvider, string> = {
  github: 'GitHub',
  'azure-devops': 'Azure DevOps',
};

interface SavedRepositoriesCardProps {
  repositories: SavedRepositorySelection[];
  maxCount: number;
  onRemove: (key: string) => void;
}

function SavedRepositoryRow({ repo, last, onRemove }: { repo: SavedRepositorySelection; last: boolean; onRemove: (key: string) => void }) {
  const key = savedRepositoryKey(repo);
  const label = `${repo.owner}/${repo.repository} (${repo.branch})`;
  return (
    <li
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        padding: '12px 0',
        borderBottom: last ? 'none' : '1px solid var(--border-subtle)',
      }}
    >
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
    </li>
  );
}

/** Saved repository folders loaded from Source Connectors, analyzed as code when the batch starts. */
export function SavedRepositoriesCard({ repositories, maxCount, onRemove }: SavedRepositoriesCardProps) {
  if (repositories.length === 0) return null;

  const overCap = repositories.length > maxCount;

  return (
    <SectionCard
      title={`Saved repositories (${repositories.length})`}
      subtitle="Each saved folder is analyzed as code in this batch."
    >
      {overCap && (
        <div style={{ color: 'var(--status-danger-fg)', fontSize: 13, marginBottom: 8 }}>
          At most {maxCount} saved repositories can be analyzed in one batch. Remove some to continue.
        </div>
      )}
      <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {repositories.map((repo, i) => (
          <SavedRepositoryRow key={savedRepositoryKey(repo)} repo={repo} last={i === repositories.length - 1} onRemove={onRemove} />
        ))}
      </ul>
    </SectionCard>
  );
}
