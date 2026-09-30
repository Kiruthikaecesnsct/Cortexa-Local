import { useMemo, useState } from 'react';
import { Badge, IconBranch, IconChevronRight, IconLock, IconSearch, Input } from '../../shared/ds';
import { relativeTime } from '../../shared/utils';
import { filterRepositories, type RepositoryQuery, type RepositorySort, type VisibilityFilter } from './scanFilters';
import type { RepositorySummaryDto } from './scanTypes';
import { SegmentedControl, type SegmentOption } from './SegmentedControl';

const VISIBILITY_OPTIONS: SegmentOption<VisibilityFilter>[] = [
  { value: 'all', label: 'All' },
  { value: 'public', label: 'Public' },
  { value: 'private', label: 'Private' },
];

const SORT_OPTIONS: SegmentOption<RepositorySort>[] = [
  { value: 'name', label: 'Name' },
  { value: 'updated', label: 'Recently updated' },
];

const INITIAL_QUERY: RepositoryQuery = { text: '', visibility: 'all', sort: 'name' };

const clampTwoLines: React.CSSProperties = {
  display: '-webkit-box',
  WebkitLineClamp: 2,
  WebkitBoxOrient: 'vertical',
  overflow: 'hidden',
};

// The list scrolls on its own so the search and filters stay in view.
// The small padding keeps the selected card's focus ring from being clipped.
const REPOSITORY_LIST_STYLE: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
  alignContent: 'start',
  gap: 12,
  maxHeight: 'min(560px, 60vh)',
  overflowY: 'auto',
  padding: 4,
  paddingRight: 8,
};

function RepositoryCard({ repo, selected, onSelect }: { repo: RepositorySummaryDto; selected: boolean; onSelect: () => void }) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={selected}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 8,
        padding: 16,
        textAlign: 'left',
        cursor: 'pointer',
        fontFamily: 'var(--font-body)',
        color: 'var(--text-primary)',
        background: 'var(--surface-card)',
        borderRadius: 'var(--radius-md)',
        border: `1px solid ${selected ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
        boxShadow: selected ? 'var(--focus-ring)' : 'var(--shadow-xs)',
      }}
    >
      <span style={{ display: 'flex', alignItems: 'center', gap: 8, width: '100%' }}>
        {repo.private && <IconLock size={14} />}
        <span style={{ fontWeight: 600, flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{repo.name}</span>
        <Badge tone={repo.private ? 'warning' : 'neutral'}>{repo.private ? 'Private' : 'Public'}</Badge>
        <IconChevronRight size={14} />
      </span>
      <span style={{ ...clampTwoLines, fontSize: 'var(--text-sm)', color: 'var(--text-muted)', minHeight: 20 }}>
        {repo.description || 'No description'}
      </span>
      <span style={{ display: 'flex', gap: 14, fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>
        {repo.default_branch && (
          <span style={{ display: 'inline-flex', gap: 4, alignItems: 'center' }}>
            <IconBranch size={12} /> {repo.default_branch}
          </span>
        )}
        {repo.updated_at && <span>Updated {relativeTime(repo.updated_at)}</span>}
      </span>
    </button>
  );
}

interface RepositoryPickerProps {
  repositories: RepositorySummaryDto[];
  selectedName?: string;
  onSelect: (repo: RepositorySummaryDto) => void;
}

export function RepositoryPicker({ repositories, selectedName, onSelect }: RepositoryPickerProps) {
  const [query, setQuery] = useState<RepositoryQuery>(INITIAL_QUERY);
  const visible = useMemo(() => filterRepositories([...repositories], query), [repositories, query]);

  if (repositories.length === 0) {
    return (
      <div style={{ color: 'var(--text-muted)', padding: '24px 0' }}>
        This token can't see any repositories here. Check that it has access to the organization, then connect again.
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
        <div style={{ flex: '1 1 260px' }}>
          <Input
            aria-label="Search repositories"
            placeholder="Search by name or description"
            leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconSearch size={16} /></span>}
            value={query.text}
            onChange={(e) => setQuery((q) => ({ ...q, text: e.target.value }))}
          />
        </div>
        <SegmentedControl label="Visibility" options={VISIBILITY_OPTIONS} value={query.visibility} onChange={(visibility) => setQuery((q) => ({ ...q, visibility }))} />
        <SegmentedControl label="Sort by" options={SORT_OPTIONS} value={query.sort} onChange={(sort) => setQuery((q) => ({ ...q, sort }))} />
      </div>
      <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>
        Showing {visible.length} of {repositories.length}
      </div>
      {visible.length === 0 ? (
        <div style={{ color: 'var(--text-muted)', padding: '16px 0' }}>No repositories match your search.</div>
      ) : (
        <div role="region" aria-label="Repositories" tabIndex={0} style={REPOSITORY_LIST_STYLE}>
          {visible.map((repo) => (
            <RepositoryCard key={repo.full_name} repo={repo} selected={repo.name === selectedName} onSelect={() => onSelect(repo)} />
          ))}
        </div>
      )}
    </div>
  );
}
