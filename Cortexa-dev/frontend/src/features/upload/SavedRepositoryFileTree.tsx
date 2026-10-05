import { useState } from 'react';
import { Checkbox, IconChevronRight, IconFile, IconFolder } from '../../shared/ds';
import { buildFileTree, filesUnder, type FileTreeNode } from './fileTreeBuilder';

interface SavedRepositoryFileTreeProps {
  files: string[];
  /** Undefined means every file is selected. */
  selectedFiles: string[] | undefined;
  onChange: (files: string[] | undefined) => void;
}

function toggledSelection(allFiles: string[], current: string[] | undefined, paths: string[], select: boolean): string[] | undefined {
  const set = new Set(current ?? allFiles);
  for (const path of paths) {
    if (select) set.add(path);
    else set.delete(path);
  }
  return set.size === allFiles.length ? undefined : [...set];
}

function TreeRow({
  node,
  depth,
  allFiles,
  selected,
  onToggle,
}: {
  node: FileTreeNode;
  depth: number;
  allFiles: string[];
  selected: Set<string> | undefined;
  onToggle: (paths: string[], select: boolean) => void;
}) {
  const [open, setOpen] = useState(depth < 1);
  const descendants = node.type === 'folder' ? filesUnder(node) : [node.path];
  const checked = selected === undefined || descendants.every((f) => selected.has(f));

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '4px 0', paddingLeft: depth * 18 }}>
        {node.type === 'folder' ? (
          <button
            type="button"
            onClick={() => setOpen((v) => !v)}
            aria-label={open ? `Collapse ${node.name}` : `Expand ${node.name}`}
            style={{
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              padding: 0,
              display: 'flex',
              color: 'var(--text-muted)',
              transform: open ? 'rotate(90deg)' : 'none',
              transition: 'transform var(--duration-fast) var(--ease-standard)',
            }}
          >
            <IconChevronRight size={12} />
          </button>
        ) : (
          <span style={{ width: 12 }} />
        )}
        <Checkbox checked={checked} onChange={(next) => onToggle(descendants, next)} />
        <span style={{ color: 'var(--text-muted)', display: 'flex' }}>
          {node.type === 'folder' ? <IconFolder size={14} /> : <IconFile size={14} />}
        </span>
        <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-primary)', fontFamily: node.type === 'file' ? 'var(--font-mono)' : undefined }}>
          {node.name}
        </span>
      </div>
      {node.type === 'folder' && open && (
        <div>
          {node.children.map((child) => (
            <TreeRow key={child.path} node={child} depth={depth + 1} allFiles={allFiles} selected={selected} onToggle={onToggle} />
          ))}
        </div>
      )}
    </div>
  );
}

/** Lets the user check/uncheck individual files or whole folders from a saved repository before analysis. */
export function SavedRepositoryFileTree({ files, selectedFiles, onChange }: SavedRepositoryFileTreeProps) {
  const tree = buildFileTree(files);
  const selected = selectedFiles === undefined ? undefined : new Set(selectedFiles);
  const allChecked = selectedFiles === undefined;

  function handleToggle(paths: string[], select: boolean) {
    onChange(toggledSelection(files, selectedFiles, paths, select));
  }

  return (
    <div
      style={{
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-md)',
        padding: '8px 12px',
        maxHeight: 280,
        overflowY: 'auto',
        marginTop: 8,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '4px 0', borderBottom: '1px solid var(--border-subtle)', marginBottom: 4 }}>
        <span style={{ width: 12 }} />
        <Checkbox checked={allChecked} onChange={(next) => onChange(next ? undefined : [])} label="Select all" />
        <span style={{ fontSize: 12, color: 'var(--text-muted)', marginLeft: 'auto' }}>
          {(selectedFiles ?? files).length} / {files.length} selected
        </span>
      </div>
      {tree.map((node) => (
        <TreeRow key={node.path} node={node} depth={0} allFiles={files} selected={selected} onToggle={handleToggle} />
      ))}
    </div>
  );
}
