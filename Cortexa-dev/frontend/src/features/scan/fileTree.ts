import type { FileNode, FileNodeKind, TreeEntryDto, TreeEntryType } from './scanTypes';

const KIND_BY_TYPE: Record<TreeEntryType, FileNodeKind> = {
  tree: 'folder',
  blob: 'file',
  commit: 'submodule',
};

function newNode(name: string, path: string, kind: FileNodeKind, size: number | null = null): FileNode {
  return { name, path, kind, size, children: [] };
}

function childFolder(parent: FileNode, name: string, index: Map<string, FileNode>): FileNode {
  const path = parent.path ? `${parent.path}/${name}` : name;
  const existing = index.get(path);
  if (existing) return existing;
  const folder = newNode(name, path, 'folder');
  parent.children.push(folder);
  index.set(path, folder);
  return folder;
}

function insertEntry(root: FileNode, entry: TreeEntryDto, index: Map<string, FileNode>): void {
  const segments = entry.path.split('/').filter(Boolean);
  const leafName = segments.pop();
  if (leafName === undefined) return;
  const parent = segments.reduce((node, name) => childFolder(node, name, index), root);
  if (entry.type === 'tree') {
    childFolder(parent, leafName, index);
    return;
  }
  parent.children.push(newNode(leafName, entry.path, KIND_BY_TYPE[entry.type], entry.size));
}

function compareNodes(a: FileNode, b: FileNode): number {
  const aFolder = a.kind === 'folder' ? 0 : 1;
  const bFolder = b.kind === 'folder' ? 0 : 1;
  return aFolder - bFolder || a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });
}

function sortTree(nodes: FileNode[]): FileNode[] {
  nodes.sort(compareNodes);
  nodes.forEach((node) => sortTree(node.children));
  return nodes;
}

/** Turns GitHub's flat recursive tree listing into nested folders, folders first. */
export function buildFileTree(entries: TreeEntryDto[]): FileNode[] {
  const root = newNode('', '', 'folder');
  const index = new Map<string, FileNode>();
  entries.forEach((entry) => insertEntry(root, entry, index));
  return sortTree(root.children);
}

function pruneNode(node: FileNode, needle: string): FileNode | null {
  if (node.name.toLowerCase().includes(needle)) return node;
  const children = filterNodes(node.children, needle);
  return children.length > 0 ? { ...node, children } : null;
}

function filterNodes(nodes: FileNode[], needle: string): FileNode[] {
  return nodes.map((n) => pruneNode(n, needle)).filter((n): n is FileNode => n !== null);
}

/** Keeps nodes whose name matches, plus the folders that lead to them. */
export function filterFileTree(nodes: FileNode[], text: string): FileNode[] {
  const needle = text.trim().toLowerCase();
  return needle ? filterNodes(nodes, needle) : nodes;
}

export function countEntries(entries: TreeEntryDto[]): { files: number; folders: number } {
  const folders = entries.filter((e) => e.type === 'tree').length;
  return { files: entries.length - folders, folders };
}
