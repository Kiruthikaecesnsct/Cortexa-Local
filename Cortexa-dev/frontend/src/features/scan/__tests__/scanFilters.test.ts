import { describe, it, expect } from 'vitest';
import { filterRepositories, orderBranches } from '../scanFilters';
import type { BranchSummaryDto, RepositorySummaryDto } from '../scanTypes';

function repo(name: string, overrides: Partial<RepositorySummaryDto> = {}): RepositorySummaryDto {
  return {
    name,
    full_name: `acme/${name}`,
    private: false,
    default_branch: 'main',
    html_url: `https://github.com/acme/${name}`,
    description: null,
    size_kb: 0,
    updated_at: null,
    ...overrides,
  };
}

const repos = [
  repo('web', { updated_at: '2026-09-01T00:00:00Z', description: 'Customer portal' }),
  repo('Api', { private: true, updated_at: '2026-09-20T00:00:00Z' }),
  repo('docs', { updated_at: '2026-01-01T00:00:00Z' }),
];

describe('filterRepositories', () => {
  it('sorts by name case-insensitively', () => {
    expect(filterRepositories([...repos], { text: '', visibility: 'all', sort: 'name' }).map((r) => r.name)).toEqual(['Api', 'docs', 'web']);
  });

  it('sorts by most recently updated', () => {
    expect(filterRepositories([...repos], { text: '', visibility: 'all', sort: 'updated' }).map((r) => r.name)).toEqual(['Api', 'web', 'docs']);
  });

  it('filters by visibility', () => {
    expect(filterRepositories([...repos], { text: '', visibility: 'private', sort: 'name' }).map((r) => r.name)).toEqual(['Api']);
    expect(filterRepositories([...repos], { text: '', visibility: 'public', sort: 'name' }).map((r) => r.name)).toEqual(['docs', 'web']);
  });

  it('matches search text against name and description', () => {
    expect(filterRepositories([...repos], { text: ' PORTAL ', visibility: 'all', sort: 'name' }).map((r) => r.name)).toEqual(['web']);
    expect(filterRepositories([...repos], { text: 'ap', visibility: 'all', sort: 'name' }).map((r) => r.name)).toEqual(['Api']);
  });
});

describe('orderBranches', () => {
  const branch = (name: string): BranchSummaryDto => ({ name, protected: false, commit_sha: null });
  const branches = [branch('feature/x'), branch('develop'), branch('main'), branch('Bugfix')];

  it('puts the default branch first, then the rest alphabetically', () => {
    expect(orderBranches([...branches], 'main', '').map((b) => b.name)).toEqual(['main', 'Bugfix', 'develop', 'feature/x']);
  });

  it('filters by search text', () => {
    expect(orderBranches([...branches], 'main', 'FEAT').map((b) => b.name)).toEqual(['feature/x']);
  });

  it('handles a repository without a default branch', () => {
    expect(orderBranches([...branches], null, '').map((b) => b.name)).toEqual(['Bugfix', 'develop', 'feature/x', 'main']);
  });
});
