import { WIZARD_STEPS, type WizardState, type WizardStep } from './scanWizard';

export interface StepPromptCopy {
  stepLabel: string;
  title: string;
  message: string;
}

export const STEP_TITLES: Record<WizardStep, string> = {
  connect: 'Connect',
  repository: 'Repository',
  branch: 'Branch',
  files: 'Files',
};

function plural(count: number, singular: string, pluralWord: string): string {
  return `${count} ${count === 1 ? singular : pluralWord}`;
}

function connectCopy(): Omit<StepPromptCopy, 'stepLabel'> {
  return {
    title: 'Connect to GitHub',
    message: 'Enter your organization URL and a personal access token, then select Connect to find its repositories.',
  };
}

function repositoryCopy(state: WizardState): Omit<StepPromptCopy, 'stepLabel'> {
  const data = state.connection.status === 'loaded' ? state.connection.data : undefined;
  const count = data?.repositories.length ?? 0;
  return {
    title: 'Choose a repository',
    message: `We found ${plural(count, 'repository', 'repositories')} in ${data?.owner ?? 'this account'}. Pick the one you want to scan.`,
  };
}

function branchCopy(state: WizardState): Omit<StepPromptCopy, 'stepLabel'> {
  const name = state.repository?.name ?? 'this repository';
  if (state.branches.status !== 'loaded') {
    return { title: 'Choose a branch', message: `Loading the branches of ${name}…` };
  }
  return {
    title: 'Choose a branch',
    message: `${name} has ${plural(state.branches.data.length, 'branch', 'branches')}. Pick the branch whose files you want to see. The default branch is listed first.`,
  };
}

function filesCopy(state: WizardState): Omit<StepPromptCopy, 'stepLabel'> {
  return {
    title: 'Browse files',
    message: `Showing everything on ${state.branch ?? 'the selected branch'}. Search or expand folders, or step back to choose a different branch or repository.`,
  };
}

const COPY: Record<WizardStep, (state: WizardState) => Omit<StepPromptCopy, 'stepLabel'>> = {
  connect: connectCopy,
  repository: repositoryCopy,
  branch: branchCopy,
  files: filesCopy,
};

export function promptFor(state: WizardState): StepPromptCopy {
  const index = WIZARD_STEPS.indexOf(state.step);
  return { stepLabel: `Step ${index + 1} of ${WIZARD_STEPS.length}`, ...COPY[state.step](state) };
}
