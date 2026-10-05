import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';

export type SavedRepositoryProvider = 'github' | 'azure-devops';

/** A branch saved to repository storage, picked for analysis from the Saved repositories list. */
export interface SavedRepositorySelection {
  provider: SavedRepositoryProvider;
  owner: string;
  repository: string;
  branch: string;
  /** Repo-relative paths to analyze. Undefined (the default) means the whole folder. */
  selectedFiles?: string[];
}

/** Router state key the Saved repositories list uses to hand folders to New Analysis. */
const STATE_KEY = 'savedRepositories';
const NEW_ANALYSIS_PATH = '/batches/new';
const PROVIDERS: ReadonlySet<string> = new Set<SavedRepositoryProvider>(['github', 'azure-devops']);

export function savedRepositoryKey(repo: SavedRepositorySelection): string {
  return `${repo.provider}/${repo.owner}/${repo.repository}@${repo.branch}`;
}

/** Sets (or clears, when `files` is undefined) the file selection for one saved repository. */
export function withSavedRepositoryFiles(
  current: SavedRepositorySelection[],
  key: string,
  files: string[] | undefined
): SavedRepositorySelection[] {
  return current.map((repo) => (savedRepositoryKey(repo) === key ? { ...repo, selectedFiles: files } : repo));
}

/** Appends the incoming repositories that are not already in the list. */
export function mergeSavedRepositories(
  current: SavedRepositorySelection[],
  incoming: SavedRepositorySelection[]
): SavedRepositorySelection[] {
  const seen = new Set(current.map(savedRepositoryKey));
  const added = incoming.filter((repo) => {
    const key = savedRepositoryKey(repo);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
  return added.length === 0 ? current : [...current, ...added];
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isSelection(value: unknown): value is SavedRepositorySelection {
  if (typeof value !== 'object' || value === null) return false;
  const repo = value as Record<string, unknown>;
  return (
    isNonEmptyString(repo.provider) &&
    PROVIDERS.has(repo.provider) &&
    isNonEmptyString(repo.owner) &&
    isNonEmptyString(repo.repository) &&
    isNonEmptyString(repo.branch)
  );
}

/** Reads saved repositories handed over through router state; anything malformed is dropped. */
export function readSavedRepositoriesFromState(state: unknown): SavedRepositorySelection[] {
  if (typeof state !== 'object' || state === null) return [];
  const list = (state as Record<string, unknown>)[STATE_KEY];
  if (!Array.isArray(list)) return [];
  return list.filter(isSelection).map(({ provider, owner, repository, branch }) => ({ provider, owner, repository, branch }));
}

/** Opens New Analysis with the given saved repositories loaded and ready to start. */
export function useOpenInNewAnalysis() {
  const navigate = useNavigate();
  return useCallback(
    (repositories: SavedRepositorySelection[]) => {
      const selections = repositories.map(({ provider, owner, repository, branch }) => ({ provider, owner, repository, branch }));
      navigate(NEW_ANALYSIS_PATH, { state: { [STATE_KEY]: selections } });
    },
    [navigate]
  );
}
