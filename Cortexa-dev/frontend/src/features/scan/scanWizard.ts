import type {
  BranchSummaryDto,
  ListRepositoriesDto,
  Loadable,
  LoadedTree,
  RepositorySummaryDto,
  ScanError,
} from './scanTypes';

export type WizardStep = 'connect' | 'repository' | 'branch' | 'files';

export const WIZARD_STEPS: WizardStep[] = ['connect', 'repository', 'branch', 'files'];

export interface WizardState {
  step: WizardStep;
  connection: Loadable<ListRepositoriesDto>;
  repository?: RepositorySummaryDto;
  branches: Loadable<BranchSummaryDto[]>;
  branch?: string;
  tree: Loadable<LoadedTree>;
}

export type WizardAction =
  | { type: 'connectStarted' }
  | { type: 'connectSucceeded'; data: ListRepositoriesDto }
  | { type: 'connectFailed'; error: ScanError }
  | { type: 'repositorySelected'; repository: RepositorySummaryDto }
  | { type: 'branchesLoaded'; branches: BranchSummaryDto[] }
  | { type: 'branchesFailed'; error: ScanError }
  | { type: 'branchSelected'; branch: string }
  | { type: 'treeLoaded'; tree: LoadedTree }
  | { type: 'treeFailed'; error: ScanError }
  | { type: 'goToStep'; step: WizardStep }
  | { type: 'reset' };

export const INITIAL_WIZARD_STATE: WizardState = {
  step: 'connect',
  connection: { status: 'idle' },
  branches: { status: 'idle' },
  tree: { status: 'idle' },
};

const IDLE = { status: 'idle' } as const;
const LOADING = { status: 'loading' } as const;

/** A step is reachable once everything it depends on has been chosen. */
export function canVisit(state: WizardState, step: WizardStep): boolean {
  if (step === 'connect') return true;
  if (state.connection.status !== 'loaded') return false;
  if (step === 'repository') return true;
  if (!state.repository) return false;
  return step === 'branch' || state.branch !== undefined;
}

function goToStep(state: WizardState, step: WizardStep): WizardState {
  return canVisit(state, step) ? { ...state, step } : state;
}

type ActionOf<K extends WizardAction['type']> = Extract<WizardAction, { type: K }>;
type HandlerMap = { [K in WizardAction['type']]: (state: WizardState, action: ActionOf<K>) => WizardState };

const HANDLERS: HandlerMap = {
  connectStarted: () => ({ ...INITIAL_WIZARD_STATE, connection: LOADING }),
  connectSucceeded: (_s, a) => ({
    ...INITIAL_WIZARD_STATE,
    step: 'repository',
    connection: { status: 'loaded', data: a.data },
  }),
  connectFailed: (_s, a) => ({ ...INITIAL_WIZARD_STATE, connection: { status: 'error', error: a.error } }),
  repositorySelected: (s, a) => ({
    ...s,
    step: 'branch',
    repository: a.repository,
    branches: LOADING,
    branch: undefined,
    tree: IDLE,
  }),
  branchesLoaded: (s, a) => ({ ...s, branches: { status: 'loaded', data: a.branches } }),
  branchesFailed: (s, a) => ({ ...s, branches: { status: 'error', error: a.error } }),
  branchSelected: (s, a) => ({ ...s, step: 'files', branch: a.branch, tree: LOADING }),
  treeLoaded: (s, a) => ({ ...s, tree: { status: 'loaded', data: a.tree } }),
  treeFailed: (s, a) => ({ ...s, tree: { status: 'error', error: a.error } }),
  goToStep: (s, a) => goToStep(s, a.step),
  reset: () => INITIAL_WIZARD_STATE,
};

export function wizardReducer(state: WizardState, action: WizardAction): WizardState {
  const handler = HANDLERS[action.type] as (s: WizardState, a: WizardAction) => WizardState;
  return handler(state, action);
}
