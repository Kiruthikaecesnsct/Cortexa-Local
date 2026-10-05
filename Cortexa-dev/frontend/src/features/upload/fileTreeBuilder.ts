/** Folder/file tree built client-side from the flat list of repo-relative paths a saved clone reports. */
export interface FileTreeNode {
  name: string;
  path: string;
  type: 'file' | 'folder';
  children: FileTreeNode[];
}

function ensureFolder(children: FileTreeNode[], name: string, path: string): FileTreeNode {
  const existing = children.find((c) => c.type === 'folder' && c.name === name);
  if (existing) return existing;
  const folder: FileTreeNode = { name, path, type: 'folder', children: [] };
  children.push(folder);
  return folder;
}

/** Builds a sorted folder/file tree from a flat list of repo-relative file paths. */
export function buildFileTree(files: string[]): FileTreeNode[] {
  const root: FileTreeNode[] = [];
  for (const file of files) {
    const segments = file.split('/').filter(Boolean);
    if (segments.length === 0) continue;
    let level = root;
    let path = '';
    for (let i = 0; i < segments.length - 1; i++) {
      path = path ? `${path}/${segments[i]}` : segments[i];
      level = ensureFolder(level, segments[i], path).children;
    }
    const name = segments[segments.length - 1];
    level.push({ name, path: file, type: 'file', children: [] });
  }
  sortTree(root);
  return root;
}

function sortTree(nodes: FileTreeNode[]): void {
  nodes.sort((a, b) => (a.type === b.type ? a.name.localeCompare(b.name) : a.type === 'folder' ? -1 : 1));
  for (const node of nodes) sortTree(node.children);
}

/** Every file path reachable under a node (itself, if it is a file). */
export function filesUnder(node: FileTreeNode): string[] {
  if (node.type === 'file') return [node.path];
  return node.children.flatMap(filesUnder);
}
