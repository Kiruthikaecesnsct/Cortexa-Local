import type { DirectoryListingDto, Loadable, ScanError } from './scanTypes';

export type LocalScanStep = 'connect' | 'files';

export interface LocalScanState {
  step: LocalScanStep;
  connection: Loadable<DirectoryListingDto>;
  // The path currently being viewed or attempted — kept up to date even while
  // loading or after a failed navigation, so breadcrumbs and retry work.
  path: string;
  listing: Loadable<DirectoryListingDto>;
}

export type LocalScanAction =
  | { type: 'connectStarted'; path: string }
  | { type: 'connectSucceeded'; data: DirectoryListingDto }
  | { type: 'connectFailed'; error: ScanError }
  | { type: 'navigateStarted'; path: string }
  | { type: 'navigateSucceeded'; data: DirectoryListingDto }
  | { type: 'navigateFailed'; error: ScanError }
  | { type: 'reset' };

export const INITIAL_LOCAL_SCAN_STATE: LocalScanState = {
  step: 'connect',
  connection: { status: 'idle' },
  path: '',
  listing: { status: 'idle' },
};

const LOADING = { status: 'loading' } as const;

type ActionOf<K extends LocalScanAction['type']> = Extract<LocalScanAction, { type: K }>;
type HandlerMap = {
  [K in LocalScanAction['type']]: (state: LocalScanState, action: ActionOf<K>) => LocalScanState;
};

const HANDLERS: HandlerMap = {
  connectStarted: (_s, a) => ({ ...INITIAL_LOCAL_SCAN_STATE, connection: LOADING, path: a.path }),
  connectSucceeded: (_s, a) => ({
    step: 'files',
    connection: { status: 'loaded', data: a.data },
    listing: { status: 'loaded', data: a.data },
    path: a.data.path,
  }),
  connectFailed: (s, a) => ({ ...s, connection: { status: 'error', error: a.error } }),
  navigateStarted: (s, a) => ({ ...s, listing: LOADING, path: a.path }),
  navigateSucceeded: (s, a) => ({ ...s, listing: { status: 'loaded', data: a.data }, path: a.data.path }),
  navigateFailed: (s, a) => ({ ...s, listing: { status: 'error', error: a.error } }),
  reset: () => INITIAL_LOCAL_SCAN_STATE,
};

export function localScanReducer(state: LocalScanState, action: LocalScanAction): LocalScanState {
  const handler = HANDLERS[action.type] as (s: LocalScanState, a: LocalScanAction) => LocalScanState;
  return handler(state, action);
}
