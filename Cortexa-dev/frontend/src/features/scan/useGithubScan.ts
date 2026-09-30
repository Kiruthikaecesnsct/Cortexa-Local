import { useCallback, useReducer, useRef } from 'react';
import { buildFileTree } from './fileTree';
import { fetchRepositoryTree, listBranches, listRepositories } from './scanRepository';
import type { GithubCredentials, RepositorySummaryDto } from './scanTypes';
import { INITIAL_WIZARD_STATE, wizardReducer, type WizardAction, type WizardStep } from './scanWizard';

export function useGithubScan() {
  const [state, rawDispatch] = useReducer(wizardReducer, INITIAL_WIZARD_STATE);
  // The PAT lives only in memory for the current page session; it is never persisted.
  const credentialsRef = useRef<GithubCredentials | null>(null);
  // Bumped whenever a newer request supersedes older ones, so late responses are dropped.
  const requestRef = useRef(0);

  const begin = useCallback(() => {
    requestRef.current += 1;
    const id = requestRef.current;
    return (action: WizardAction) => {
      if (id === requestRef.current) rawDispatch(action);
    };
  }, []);

  const connect = useCallback(
    async (credentials: GithubCredentials) => {
      const dispatch = begin();
      credentialsRef.current = credentials;
      dispatch({ type: 'connectStarted' });
      const result = await listRepositories(credentials);
      if (!result.ok) credentialsRef.current = null;
      dispatch(result.ok ? { type: 'connectSucceeded', data: result.data } : { type: 'connectFailed', error: result.error });
    },
    [begin]
  );

  const selectRepository = useCallback(
    async (repository: RepositorySummaryDto) => {
      const credentials = credentialsRef.current;
      if (!credentials) return;
      const dispatch = begin();
      dispatch({ type: 'repositorySelected', repository });
      const result = await listBranches(credentials, repository.name);
      dispatch(result.ok ? { type: 'branchesLoaded', branches: result.data.branches } : { type: 'branchesFailed', error: result.error });
    },
    [begin]
  );

  const selectBranch = useCallback(
    async (repository: RepositorySummaryDto, branch: string) => {
      const credentials = credentialsRef.current;
      if (!credentials) return;
      const dispatch = begin();
      dispatch({ type: 'branchSelected', branch });
      const result = await fetchRepositoryTree(credentials, repository.name, branch);
      dispatch(
        result.ok
          ? { type: 'treeLoaded', tree: { tree: result.data, nodes: buildFileTree(result.data.entries) } }
          : { type: 'treeFailed', error: result.error }
      );
    },
    [begin]
  );

  const goToStep = useCallback((step: WizardStep) => rawDispatch({ type: 'goToStep', step }), []);

  const disconnect = useCallback(() => {
    begin();
    credentialsRef.current = null;
    rawDispatch({ type: 'reset' });
  }, [begin]);

  const credentials = useCallback(() => credentialsRef.current, []);

  return { state, connect, selectRepository, selectBranch, goToStep, disconnect, credentials };
}
