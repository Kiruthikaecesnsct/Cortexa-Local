import { useCallback, useEffect, useReducer } from 'react';
import { listUsers } from './adminUsersRepository';
import type { AdminUser, AdminUserError } from './adminUserTypes';

interface UseOrgUsersState {
  data: AdminUser[] | null;
  isLoading: boolean;
  error: AdminUserError | null;
}

type UseOrgUsersAction =
  | { type: 'FETCH_START' }
  | { type: 'FETCH_SUCCESS'; data: AdminUser[] }
  | { type: 'FETCH_ERROR'; error: AdminUserError };

const initialState: UseOrgUsersState = {
  data: null,
  isLoading: true,
  error: null,
};

function reducer(state: UseOrgUsersState, action: UseOrgUsersAction): UseOrgUsersState {
  switch (action.type) {
    case 'FETCH_START':
      return { ...state, isLoading: true };
    case 'FETCH_SUCCESS':
      return { data: action.data, isLoading: false, error: null };
    case 'FETCH_ERROR':
      return { ...state, isLoading: false, error: action.error };
    default:
      return state;
  }
}

export interface UseOrgUsersResult extends UseOrgUsersState {
  isForbidden: boolean;
  refetch: () => Promise<void>;
}

export function useOrgUsers(enabled: boolean): UseOrgUsersResult {
  const [state, dispatch] = useReducer(reducer, initialState);

  const fetch = useCallback(async () => {
    if (!enabled) return;
    dispatch({ type: 'FETCH_START' });
    const result = await listUsers();
    if (result.ok) {
      dispatch({ type: 'FETCH_SUCCESS', data: result.data });
    } else {
      dispatch({ type: 'FETCH_ERROR', error: result.error });
    }
  }, [enabled]);

  useEffect(() => {
    void fetch();
  }, [fetch]);

  return {
    ...state,
    isForbidden: state.error?.kind === 'forbidden' && state.data === null,
    refetch: fetch,
  };
}
