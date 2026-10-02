import { describe, it, expect } from 'vitest';
import { mergeSavedRepositories, readSavedRepositoriesFromState, savedRepositoryKey } from '../savedRepositories';

const api = { provider: 'github' as const, owner: 'acme', repository: 'api', branch: 'main' };
const web = { provider: 'azure-devops' as const, owner: 'contoso', repository: 'Platform/web', branch: 'release/1' };

describe('savedRepositories', () => {
  it('keys a repository by provider, owner, repository and branch', () => {
    expect(savedRepositoryKey(web)).toBe('azure-devops/contoso/Platform/web@release/1');
  });

  it('merges without duplicates and keeps the same list when nothing is new', () => {
    const current = [api];
    expect(mergeSavedRepositories(current, [api, web, web])).toEqual([api, web]);
    expect(mergeSavedRepositories(current, [api])).toBe(current);
  });

  it('reads valid selections from router state and drops malformed ones', () => {
    const state = {
      savedRepositories: [
        { ...api, extra: 'ignored' },
        { provider: 'gitlab', owner: 'a', repository: 'b', branch: 'c' },
        { provider: 'github', owner: '', repository: 'b', branch: 'c' },
        'nonsense',
        web,
      ],
    };

    expect(readSavedRepositoriesFromState(state)).toEqual([api, web]);
  });

  it.each([null, undefined, 'x', {}, { savedRepositories: 'x' }])('returns nothing for %p', (state) => {
    expect(readSavedRepositoriesFromState(state)).toEqual([]);
  });
});
