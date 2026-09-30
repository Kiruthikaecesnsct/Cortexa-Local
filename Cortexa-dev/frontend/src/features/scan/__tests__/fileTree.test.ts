import { describe, it, expect } from 'vitest';
import { buildFileTree, countEntries, filterFileTree } from '../fileTree';
import type { TreeEntryDto } from '../scanTypes';

const entries: TreeEntryDto[] = [
  { path: 'src', type: 'tree', size: null },
  { path: 'src/main.py', type: 'blob', size: 42 },
  { path: 'README.md', type: 'blob', size: 10 },
  { path: 'src/api', type: 'tree', size: null },
  { path: 'src/api/routes.py', type: 'blob', size: 7 },
  { path: 'vendor/lib', type: 'commit', size: null },
  { path: 'assets', type: 'tree', size: null },
];

describe('buildFileTree', () => {
  it('nests entries and sorts folders before files', () => {
    const tree = buildFileTree(entries);

    expect(tree.map((n) => n.name)).toEqual(['assets', 'src', 'vendor', 'README.md']);
    const src = tree.find((n) => n.name === 'src');
    expect(src?.children.map((n) => n.name)).toEqual(['api', 'main.py']);
    expect(src?.children[0]?.children[0]).toMatchObject({ name: 'routes.py', path: 'src/api/routes.py', kind: 'file', size: 7 });
  });

  it('creates missing parent folders when the parent entry is absent', () => {
    const vendor = buildFileTree(entries).find((n) => n.name === 'vendor');

    expect(vendor?.kind).toBe('folder');
    expect(vendor?.children[0]).toMatchObject({ name: 'lib', kind: 'submodule' });
  });

  it('does not duplicate a folder listed after its children', () => {
    const tree = buildFileTree([
      { path: 'a/b.txt', type: 'blob', size: 1 },
      { path: 'a', type: 'tree', size: null },
    ]);

    expect(tree).toHaveLength(1);
    expect(tree[0]?.children).toHaveLength(1);
  });

  it('returns an empty list for an empty repository', () => {
    expect(buildFileTree([])).toEqual([]);
  });
});

describe('filterFileTree', () => {
  const tree = buildFileTree(entries);

  it('returns the full tree when the search is blank', () => {
    expect(filterFileTree(tree, '  ')).toBe(tree);
  });

  it('keeps matching files and the folders that lead to them', () => {
    const result = filterFileTree(tree, 'ROUTES');

    expect(result.map((n) => n.name)).toEqual(['src']);
    expect(result[0]?.children.map((n) => n.name)).toEqual(['api']);
    expect(result[0]?.children[0]?.children.map((n) => n.name)).toEqual(['routes.py']);
  });

  it('keeps a matching folder with all its children', () => {
    const result = filterFileTree(tree, 'api');

    expect(result[0]?.children[0]?.children).toHaveLength(1);
  });

  it('returns nothing when no name matches', () => {
    expect(filterFileTree(tree, 'zzz')).toEqual([]);
  });
});

describe('countEntries', () => {
  it('counts files and folders separately', () => {
    expect(countEntries(entries)).toEqual({ files: 4, folders: 3 });
  });
});
