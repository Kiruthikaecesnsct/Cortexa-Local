import { describe, it, expect } from 'vitest';
import { promptFor } from '../scanPrompts';
import { INITIAL_WIZARD_STATE, wizardReducer, type WizardAction, type WizardState } from '../scanWizard';
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

function run(...actions: WizardAction[]): WizardState {
  return actions.reduce(wizardReducer, INITIAL_WIZARD_STATE);
}

describe('promptFor', () => {
  it('asks the user to connect first', () => {
    expect(promptFor(INITIAL_WIZARD_STATE)).toMatchObject({ stepLabel: 'Step 1 of 4', title: 'Connect to GitHub' });
  });

  it('tells the user how many repositories were found', () => {
    const one = promptFor(run({ type: 'connectSucceeded', data: { owner: 'acme', repositories: [repo] } }));
    const two = promptFor(run({ type: 'connectSucceeded', data: { owner: 'acme', repositories: [repo, repo] } }));

    expect(one.stepLabel).toBe('Step 2 of 4');
    expect(one.message).toContain('1 repository in acme');
    expect(two.message).toContain('2 repositories in acme');
  });

  it('reports loading and then the branch count', () => {
    const loading = run({ type: 'connectSucceeded', data: { owner: 'acme', repositories: [repo] } }, { type: 'repositorySelected', repository: repo });
    const loaded = wizardReducer(loading, { type: 'branchesLoaded', branches: [{ name: 'main', protected: false, commit_sha: null }] });

    expect(promptFor(loading).message).toContain('Loading the branches of api');
    expect(promptFor(loaded).message).toContain('api has 1 branch.');
  });

  it('names the branch being browsed', () => {
    const state = run(
      { type: 'connectSucceeded', data: { owner: 'acme', repositories: [repo] } },
      { type: 'repositorySelected', repository: repo },
      { type: 'branchSelected', branch: 'develop' }
    );

    expect(promptFor(state)).toMatchObject({ stepLabel: 'Step 4 of 4', title: 'Browse files' });
    expect(promptFor(state).message).toContain('develop');
  });
});
