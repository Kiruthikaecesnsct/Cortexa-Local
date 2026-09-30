import { useMemo, useState, type ReactNode } from 'react';
import { Badge, Button, IconBranch, IconSearch, InlineMessage, Input } from '../../shared/ds';
import { countEntries, filterFileTree } from './fileTree';
import { FileTreeView } from './FileTreeView';
import { ScanLoader } from './ScanLoader';
import type { Loadable, LoadedTree } from './scanTypes';

interface ExplorerHeaderProps {
  owner: string;
  repository: string;
  branch: string;
  counts?: { files: number; folders: number };
}

function ExplorerHeader({ owner, repository, branch, counts }: ExplorerHeaderProps) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap', flex: 1, minWidth: 0 }}>
      <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-md)' }}>
        <span style={{ color: 'var(--text-muted)' }}>{owner} / </span>
        <strong>{repository}</strong>
      </span>
      <Badge tone="info" icon={<IconBranch size={12} />}>
        {branch}
      </Badge>
      {counts && (
        <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>
          {counts.folders} folders · {counts.files} files
        </span>
      )}
    </div>
  );
}

function LoadedExplorer({ data, repository }: { data: LoadedTree; repository: string }) {
  const [search, setSearch] = useState('');
  const [expandAll, setExpandAll] = useState(false);
  const visible = useMemo(() => filterFileTree(data.nodes, search), [data.nodes, search]);
  const searching = search.trim().length > 0;

  if (data.nodes.length === 0) {
    return <div style={{ color: 'var(--text-muted)', padding: '16px 0' }}>This branch has no files yet. Choose another branch or repository.</div>;
  }
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {data.tree.truncated && (
        <InlineMessage variant="info">GitHub shortened this listing because the repository is very large. Some files are not shown.</InlineMessage>
      )}
      <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
        <div style={{ flex: '1 1 260px' }}>
          <Input
            aria-label="Search files and folders"
            placeholder="Search files and folders"
            leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconSearch size={16} /></span>}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <Button size="sm" variant="ghost" onClick={() => setExpandAll(true)} disabled={searching}>
          Expand all
        </Button>
        <Button size="sm" variant="ghost" onClick={() => setExpandAll(false)} disabled={searching}>
          Collapse all
        </Button>
      </div>
      <div style={{ border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-md)', background: 'var(--surface-sunken)' }}>
        {visible.length === 0 ? (
          <div style={{ color: 'var(--text-muted)', padding: 16 }}>Nothing matches “{search}”.</div>
        ) : (
          <FileTreeView
            key={`${search}|${expandAll}`}
            nodes={visible}
            label={`Files in ${repository}`}
            defaultOpen={searching || expandAll}
            highlight={search}
          />
        )}
      </div>
    </div>
  );
}

interface FileExplorerProps extends Omit<ExplorerHeaderProps, 'counts'> {
  tree: Loadable<LoadedTree>;
  onRetry: () => void;
  action?: ReactNode;
}

export function FileExplorer({ owner, repository, branch, tree, onRetry, action }: FileExplorerProps) {
  const counts = tree.status === 'loaded' ? countEntries(tree.data.tree.entries) : undefined;
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 16 }}>
        <ExplorerHeader owner={owner} repository={repository} branch={branch} counts={counts} />
        {action}
      </div>
      {tree.status === 'loaded' && <LoadedExplorer data={tree.data} repository={repository} />}
      {tree.status === 'error' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10, alignItems: 'flex-start' }}>
          <InlineMessage variant="danger" correlationId={tree.error.correlationId}>
            {tree.error.message}
          </InlineMessage>
          <Button size="sm" variant="secondary" onClick={onRetry}>
            Try again
          </Button>
        </div>
      )}
      {(tree.status === 'loading' || tree.status === 'idle') && (
        <ScanLoader
          title="Reading files"
          message={`Listing every file and folder on ${branch} in ${repository}.`}
          slowHint="Large repositories can take several seconds. Hang tight."
        />
      )}
    </div>
  );
}
