import type { BranchSummaryDto, RepositorySummaryDto } from './scanTypes';

export type VisibilityFilter = 'all' | 'public' | 'private';
export type RepositorySort = 'name' | 'updated';

export interface RepositoryQuery {
  text: string;
  visibility: VisibilityFilter;
  sort: RepositorySort;
}

function matchesVisibility(repo: RepositorySummaryDto, visibility: VisibilityFilter): boolean {
  if (visibility === 'all') return true;
  return visibility === 'private' ? repo.private : !repo.private;
}

function matchesText(repo: RepositorySummaryDto, needle: string): boolean {
  if (!needle) return true;
  return repo.name.toLowerCase().includes(needle) || (repo.description ?? '').toLowerCase().includes(needle);
}

function byUpdatedDesc(a: RepositorySummaryDto, b: RepositorySummaryDto): number {
  return (b.updated_at ?? '').localeCompare(a.updated_at ?? '');
}

function byName(a: { name: string }, b: { name: string }): number {
  return a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });
}

export function filterRepositories(repositories: RepositorySummaryDto[], query: RepositoryQuery): RepositorySummaryDto[] {
  const needle = query.text.trim().toLowerCase();
  const visible = repositories.filter((r) => matchesVisibility(r, query.visibility) && matchesText(r, needle));
  return visible.sort(query.sort === 'updated' ? byUpdatedDesc : byName);
}

/** Default branch first, then the rest alphabetically; optionally narrowed by a search string. */
export function orderBranches(branches: BranchSummaryDto[], defaultBranch: string | null, text: string): BranchSummaryDto[] {
  const needle = text.trim().toLowerCase();
  const visible = branches.filter((b) => !needle || b.name.toLowerCase().includes(needle));
  const rank = (b: BranchSummaryDto) => (b.name === defaultBranch ? 0 : 1);
  return visible.sort((a, b) => rank(a) - rank(b) || byName(a, b));
}
