import { useMemo, useState } from 'react';
import { Badge, Button, IconBranch, IconChevronRight, IconSearch, InlineMessage, Input } from '../../shared/ds';
import { orderBranches } from './scanFilters';
import { ScanLoader } from './ScanLoader';
import type { BranchSummaryDto, Loadable, RepositorySummaryDto } from './scanTypes';

const SHORT_SHA_LENGTH = 7;

function BranchRow({ branch, isDefault, selected, onSelect }: { branch: BranchSummaryDto; isDefault: boolean; selected: boolean; onSelect: () => void }) {
  return (
    <li>
      <button
        type="button"
        onClick={onSelect}
        aria-pressed={selected}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 10,
          width: '100%',
          padding: '12px 14px',
          textAlign: 'left',
          cursor: 'pointer',
          fontFamily: 'var(--font-body)',
          color: 'var(--text-primary)',
          background: selected ? 'var(--status-info-bg)' : 'var(--surface-card)',
          border: `1px solid ${selected ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
          borderRadius: 'var(--radius-md)',
        }}
      >
        <span style={{ color: 'var(--accent-primary)', display: 'inline-flex' }}>
          <IconBranch size={16} />
        </span>
        <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-sm)', fontWeight: 600, flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {branch.name}
        </span>
        {isDefault && <Badge tone="brand">Default</Badge>}
        {branch.protected && <Badge tone="info">Protected</Badge>}
        {branch.commit_sha && (
          <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>{branch.commit_sha.slice(0, SHORT_SHA_LENGTH)}</span>
        )}
        <IconChevronRight size={14} />
      </button>
    </li>
  );
}

function BranchList({ repository, branches, selected, onSelect }: { repository: RepositorySummaryDto; branches: BranchSummaryDto[]; selected?: string; onSelect: (name: string) => void }) {
  const [text, setText] = useState('');
  const visible = useMemo(() => orderBranches([...branches], repository.default_branch, text), [branches, repository.default_branch, text]);

  if (branches.length === 0) {
    return <div style={{ color: 'var(--text-muted)', padding: '16px 0' }}>{repository.name} has no branches yet, so there are no files to list. Choose another repository.</div>;
  }
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <Input
        aria-label="Search branches"
        placeholder="Search branches"
        leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconSearch size={16} /></span>}
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      {visible.length === 0 ? (
        <div style={{ color: 'var(--text-muted)' }}>No branches match your search.</div>
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: 8, maxHeight: 440, overflow: 'auto' }}>
          {visible.map((b) => (
            <BranchRow key={b.name} branch={b} isDefault={b.name === repository.default_branch} selected={b.name === selected} onSelect={() => onSelect(b.name)} />
          ))}
        </ul>
      )}
    </div>
  );
}

interface BranchPickerProps {
  repository: RepositorySummaryDto;
  branches: Loadable<BranchSummaryDto[]>;
  selected?: string;
  onSelect: (branch: string) => void;
  onRetry: () => void;
}

export function BranchPicker({ repository, branches, selected, onSelect, onRetry }: BranchPickerProps) {
  if (branches.status === 'error') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10, alignItems: 'flex-start' }}>
        <InlineMessage variant="danger" correlationId={branches.error.correlationId}>
          {branches.error.message}
        </InlineMessage>
        <Button size="sm" variant="secondary" onClick={onRetry}>
          Try again
        </Button>
      </div>
    );
  }
  if (branches.status !== 'loaded') {
    return (
      <ScanLoader
        title="Loading branches"
        message={`Reading the branches of ${repository.name} from GitHub.`}
        slowHint="Repositories with many branches can take a little longer."
      />
    );
  }
  return <BranchList repository={repository} branches={branches.data} selected={selected} onSelect={onSelect} />;
}
