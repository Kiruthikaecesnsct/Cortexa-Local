import { useCallback, useReducer, useRef } from 'react';
import { listDirectory } from './localSystemScanRepository';
import {
  INITIAL_LOCAL_SCAN_STATE,
  localScanReducer,
  type LocalScanAction,
} from './localSystemScanState';
import type { LocalSystemCredentials } from './scanTypes';

function parentOf(path: string): string {
  if (path === '/' || path === '') return '/';
  const trimmed = path.replace(/\/+$/, '');
  const at = trimmed.lastIndexOf('/');
  return at <= 0 ? '/' : trimmed.slice(0, at);
}

export function useLocalSystemScan() {
  const [state, rawDispatch] = useReducer(localScanReducer, INITIAL_LOCAL_SCAN_STATE);
  // The SSH key lives only in memory for the current page session; it is never persisted.
  const credentialsRef = useRef<LocalSystemCredentials | null>(null);
  // Bumped whenever a newer request supersedes older ones, so late responses are dropped.
  const requestRef = useRef(0);

  const begin = useCallback(() => {
    requestRef.current += 1;
    const id = requestRef.current;
    return (action: LocalScanAction) => {
      if (id === requestRef.current) rawDispatch(action);
    };
  }, []);

  const connect = useCallback(
    async (credentials: LocalSystemCredentials, path: string) => {
      const dispatch = begin();
      credentialsRef.current = credentials;
      dispatch({ type: 'connectStarted', path });
      const result = await listDirectory(credentials, path);
      if (!result.ok) credentialsRef.current = null;
      dispatch(result.ok ? { type: 'connectSucceeded', data: result.data } : { type: 'connectFailed', error: result.error });
    },
    [begin]
  );

  const navigate = useCallback(
    async (path: string) => {
      const credentials = credentialsRef.current;
      if (!credentials) return;
      const dispatch = begin();
      dispatch({ type: 'navigateStarted', path });
      const result = await listDirectory(credentials, path);
      dispatch(result.ok ? { type: 'navigateSucceeded', data: result.data } : { type: 'navigateFailed', error: result.error });
    },
    [begin]
  );

  const goUp = useCallback(() => void navigate(parentOf(state.path)), [navigate, state.path]);
  const retry = useCallback(() => void navigate(state.path), [navigate, state.path]);

  const disconnect = useCallback(() => {
    begin();
    credentialsRef.current = null;
    rawDispatch({ type: 'reset' });
  }, [begin]);

  return { state, connect, navigate, goUp, retry, disconnect };
}
