import { describe, it, expect } from 'vitest';
import { buildFileTree, filesUnder } from '../fileTreeBuilder';

describe('buildFileTree', () => {
  it('groups nested paths into folders, folders before files, alphabetically', () => {
    const tree = buildFileTree(['README.md', 'src/app.py', 'src/utils/strings.py', 'src/main.py']);

    expect(tree.map((n) => n.name)).toEqual(['src', 'README.md']);
    const src = tree[0];
    expect(src.type).toBe('folder');
    expect(src.children.map((n) => n.name)).toEqual(['utils', 'app.py', 'main.py']);
  });

  it('ignores empty path segments', () => {
    expect(buildFileTree(['', 'a.py'])).toHaveLength(1);
  });
});

describe('filesUnder', () => {
  it('collects every file path reachable under a node', () => {
    const [folder] = buildFileTree(['src/app.py', 'src/utils/strings.py']);
    expect(filesUnder(folder).sort()).toEqual(['src/app.py', 'src/utils/strings.py']);
  });

  it('returns itself for a file node', () => {
    const tree = buildFileTree(['README.md']);
    expect(filesUnder(tree[0])).toEqual(['README.md']);
  });
});
