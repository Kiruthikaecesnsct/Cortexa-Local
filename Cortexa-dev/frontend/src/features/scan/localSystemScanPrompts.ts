import type { LocalScanState, LocalScanStep } from './localSystemScanState';
import type { StepPromptCopy } from './scanPrompts';

const STEP_LABELS: Record<LocalScanStep, string> = { connect: 'Step 1 of 2', files: 'Step 2 of 2' };

function connectCopy(): Omit<StepPromptCopy, 'stepLabel'> {
  return {
    title: 'Connect to a VM',
    message: "Enter the VM's IP address, an SSH private key, and the folder to browse, then select Connect.",
  };
}

function filesCopy(state: LocalScanState): Omit<StepPromptCopy, 'stepLabel'> {
  if (state.listing.status !== 'loaded') {
    return { title: 'Browse files', message: `Loading ${state.path || 'the folder'}…` };
  }
  const count = state.listing.data.entries.length;
  return {
    title: 'Browse files',
    message: `Showing ${count} item${count === 1 ? '' : 's'} in ${state.path}. Select a folder to open it, or Up to go back.`,
  };
}

export function promptFor(state: LocalScanState): StepPromptCopy {
  const body = state.step === 'connect' ? connectCopy() : filesCopy(state);
  return { stepLabel: STEP_LABELS[state.step], ...body };
}
