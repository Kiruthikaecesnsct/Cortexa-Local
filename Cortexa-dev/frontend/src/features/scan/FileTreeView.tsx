import { useState } from 'react';
import { IconChevronRight, IconFile, IconFolder, IconGit } from '../../shared/ds';
import { formatBytes } from '../upload/uploadValidation';
import type { FileNode } from './scanTypes';

const INDENT_PX = 18;
const BASE_PADDING_PX = 10;

interface TreeOptions {
  defaultOpen: boolean;
  highlight: string;
}

const rowStyle = (depth: number): React.CSSProperties => ({
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  width: '100%',
  padding: `5px 10px 5px ${BASE_PADDING_PX + depth * INDENT_PX}px`,
  border: 'none',
  background: 'transparent',
  borderRadius: 'var(--radius-sm)',
  fontFamily: 'var(--font-mono)',
  fontSize: 'var(--text-sm)',
  color: 'var(--text-primary)',
  textAlign: 'left',
});

function describeLeaf(node: FileNode): string {
  if (node.kind === 'submodule') return 'submodule';
  return node.size === null ? '' : formatBytes(node.size);
}

function NodeIcon({ node }: { node: FileNode }) {
  if (node.kind === 'folder') return <span style={{ color: 'var(--accent-primary)', display: 'inline-flex' }}><IconFolder size={15} /></span>;
  if (node.kind === 'submodule') return <IconGit size={15} />;
  return <span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconFile size={15} /></span>;
}

function HighlightedName({ name, highlight }: { name: string; highlight: string }) {
  const needle = highlight.trim().toLowerCase();
  const at = needle ? name.toLowerCase().indexOf(needle) : -1;
  if (at < 0) return <>{name}</>;
  return (
    <>
      {name.slice(0, at)}
      <mark style={{ background: 'var(--status-warning-bg)', color: 'inherit', borderRadius: 2 }}>{name.slice(at, at + needle.length)}</mark>
      {name.slice(at + needle.length)}
    </>
  );
}

function Chevron({ open }: { open: boolean }) {
  return (
    <span style={{ display: 'inline-flex', transform: open ? 'rotate(90deg)' : undefined, transition: 'transform var(--duration-fast) var(--ease-standard)' }}>
      <IconChevronRight size={13} />
    </span>
  );
}

function FolderRow({ node, depth, options }: { node: FileNode; depth: number; options: TreeOptions }) {
  const [open, setOpen] = useState(options.defaultOpen);
  return (
    <li role="treeitem" aria-expanded={open} aria-selected={false}>
      <button type="button" onClick={() => setOpen((v) => !v)} style={{ ...rowStyle(depth), cursor: 'pointer' }}>
        <Chevron open={open} />
        <NodeIcon node={node} />
        <span style={{ fontWeight: 500 }}>
          <HighlightedName name={node.name} highlight={options.highlight} />
        </span>
        <span style={{ color: 'var(--text-muted)', fontSize: 'var(--text-xs)' }}>{node.children.length}</span>
      </button>
      {open && <FileTreeList nodes={node.children} depth={depth + 1} options={options} />}
    </li>
  );
}

function LeafRow({ node, depth, options }: { node: FileNode; depth: number; options: TreeOptions }) {
  return (
    <li role="treeitem" aria-selected={false}>
      <div style={rowStyle(depth)}>
        <span style={{ width: 13, flexShrink: 0 }} />
        <NodeIcon node={node} />
        <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          <HighlightedName name={node.name} highlight={options.highlight} />
        </span>
        <span style={{ color: 'var(--text-muted)', fontSize: 'var(--text-xs)' }}>{describeLeaf(node)}</span>
      </div>
    </li>
  );
}

function FileTreeList({ nodes, depth, options }: { nodes: FileNode[]; depth: number; options: TreeOptions }) {
  return (
    <ul role="group" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {nodes.map((node) =>
        node.kind === 'folder' ? (
          <FolderRow key={node.path} node={node} depth={depth} options={options} />
        ) : (
          <LeafRow key={node.path} node={node} depth={depth} options={options} />
        )
      )}
    </ul>
  );
}

interface FileTreeViewProps {
  nodes: FileNode[];
  label: string;
  defaultOpen?: boolean;
  highlight?: string;
}

export function FileTreeView({ nodes, label, defaultOpen = false, highlight = '' }: FileTreeViewProps) {
  return (
    <div role="tree" aria-label={label} style={{ maxHeight: 520, overflow: 'auto', padding: '6px 0' }}>
      <FileTreeList nodes={nodes} depth={0} options={{ defaultOpen, highlight }} />
    </div>
  );
}
