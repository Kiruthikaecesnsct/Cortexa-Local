import { describe, it, expect } from 'vitest';
import { INITIAL_WIZARD_STATE, canVisit, wizardReducer, type WizardAction, type WizardState } from '../scanWizard';
import type { RepositorySummaryDto } from '../scanTypes';

const repo: RepositorySummaryDto = {
  name: 'api',
  full_name: 'acme/api',
  private: false,
  default_branch: 'main',
  html_url: 'https://github.com/acme/api',
  description: null,
  size_kb: 1,
  updated_at: null,
};
const error = { message: 'boom', correlationId: 'cid' };
const tree = { tree: { repository: 'api', branch: 'main', entries: [], truncated: false }, nodes: [] };

function run(...actions: WizardAction[]): WizardState {
  return actions.reduce(wizardReducer, INITIAL_WIZARD_STATE);
}

const connected: WizardAction = { type: 'connectSucceeded', data: { owner: 'acme', repositories: [repo] } };

describe('wizardReducer', () => {
  it('moves to the repository step after connecting', () => {
    const state = run({ type: 'connectStarted' }, connected);

    expect(state.step).toBe('repository');
    expect(state.connection.status).toBe('loaded');
  });

  it('stays on connect and keeps the error when connecting fails', () => {
    const state = run({ type: 'connectStarted' }, { type: 'connectFailed', error });

    expect(state.step).toBe('connect');
    expect(state.connection).toEqual({ status: 'error', error });
  });

  it('selecting a repository loads branches and clears any previous branch', () => {
    const state = run(connected, { type: 'repositorySelected', repository: repo }, { type: 'branchSelected', branch: 'main' }, { type: 'repositorySelected', repository: repo });

    expect(state.step).toBe('branch');
    expect(state.branches.status).toBe('loading');
    expect(state.branch).toBeUndefined();
    expect(state.tree.status).toBe('idle');
  });

  it('records branch results and failures', () => {
    const base = run(connected, { type: 'repositorySelected', repository: repo });
    const branch = { name: 'main', protected: false, commit_sha: null };

    expect(wizardReducer(base, { type: 'branchesLoaded', branches: [branch] }).branches).toEqual({ status: 'loaded', data: [branch] });
    expect(wizardReducer(base, { type: 'branchesFailed', error }).branches).toEqual({ status: 'error', error });
  });

  it('selecting a branch moves to files and loads the tree', () => {
    const state = run(connected, { type: 'repositorySelected', repository: repo }, { type: 'branchSelected', branch: 'dev' });

    expect(state.step).toBe('files');
    expect(state.branch).toBe('dev');
    expect(state.tree.status).toBe('loading');
    expect(wizardReducer(state, { type: 'treeLoaded', tree }).tree).toEqual({ status: 'loaded', data: tree });
    expect(wizardReducer(state, { type: 'treeFailed', error }).tree).toEqual({ status: 'error', error });
  });

  it('goes back to earlier steps but not ahead of what was chosen', () => {
    const atBranch = run(connected, { type: 'repositorySelected', repository: repo });

    expect(wizardReducer(atBranch, { type: 'goToStep', step: 'repository' }).step).toBe('repository');
    expect(wizardReducer(atBranch, { type: 'goToStep', step: 'files' }).step).toBe('branch');
    expect(wizardReducer(INITIAL_WIZARD_STATE, { type: 'goToStep', step: 'repository' }).step).toBe('connect');
  });

  it('keeps selections when stepping back so the user can return', () => {
    const atFiles = run(connected, { type: 'repositorySelected', repository: repo }, { type: 'branchSelected', branch: 'main' });
    const back = wizardReducer(atFiles, { type: 'goToStep', step: 'branch' });

    expect(back.branch).toBe('main');
    expect(wizardReducer(back, { type: 'goToStep', step: 'files' }).step).toBe('files');
  });

  it('reset returns to the initial state', () => {
    expect(run(connected, { type: 'reset' })).toEqual(INITIAL_WIZARD_STATE);
  });
});

describe('canVisit', () => {
  it('always allows connect', () => {
    expect(canVisit(INITIAL_WIZARD_STATE, 'connect')).toBe(true);
  });

  it('requires a connection, repository, and branch for later steps', () => {
    const afterConnect = run(connected);
    const afterRepo = run(connected, { type: 'repositorySelected', repository: repo });

    expect(canVisit(INITIAL_WIZARD_STATE, 'repository')).toBe(false);
    expect(canVisit(afterConnect, 'repository')).toBe(true);
    expect(canVisit(afterConnect, 'branch')).toBe(false);
    expect(canVisit(afterRepo, 'branch')).toBe(true);
    expect(canVisit(afterRepo, 'files')).toBe(false);
  });
});
