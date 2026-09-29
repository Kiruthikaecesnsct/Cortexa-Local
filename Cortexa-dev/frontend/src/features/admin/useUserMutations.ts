import { useCallback, useRef, useState } from 'react';
import type { Role } from '../../core/auth/authTypes';
import type { ToastContextValue } from '../../shared/ds/Toast';
import { changeRole, disableUser, enableUser } from './adminUsersRepository';
import type { UserMutationResult } from './adminUsersRepository';
import type { AdminUserError } from './adminUserTypes';

export type UserMutationAction =
  | { kind: 'enable'; userId: string; username: string }
  | { kind: 'disable'; userId: string; username: string }
  | { kind: 'changeRole'; userId: string; username: string; newRole: Role };

export interface UseUserMutationsOptions {
  refetch: () => Promise<void>;
  onToast: ToastContextValue['show'];
}

export interface UseUserMutationsResult {
  run: (action: UserMutationAction) => Promise<void>;
  isRunning: boolean;
  hasPermissionError: boolean;
}

type ToastPayload = Parameters<ToastContextValue['show']>[0];

function callMutation(action: UserMutationAction): Promise<UserMutationResult> {
  if (action.kind === 'enable') return enableUser(action.userId);
  if (action.kind === 'disable') return disableUser(action.userId);
  return changeRole(action.userId, action.newRole);
}

function resolveSuccessToast(action: UserMutationAction): ToastPayload {
  if (action.kind === 'enable') {
    return {
      variant: 'success',
      title: 'User enabled',
      message: `${action.username} can sign in again.`,
    };
  }
  if (action.kind === 'disable') {
    return {
      variant: 'success',
      title: 'User disabled',
      message: `${action.username} can no longer sign in.`,
    };
  }
  return {
    variant: 'success',
    title: 'Role updated',
    message: `${action.username} is now ${action.newRole}.`,
  };
}

function resolveErrorToast(error: AdminUserError): ToastPayload {
  if (error.kind === 'forbidden') {
    return { variant: 'error', title: 'Permission denied', message: error.message };
  }
  return {
    variant: 'error',
    title: 'Something went wrong',
    message: "Couldn't complete the change. Try again in a moment.",
  };
}

export function useUserMutations({ refetch, onToast }: UseUserMutationsOptions): UseUserMutationsResult {
  const [isRunning, setIsRunning] = useState(false);
  const [hasPermissionError, setHasPermissionError] = useState(false);
  const inFlightRef = useRef(false);

  const run = useCallback(
    async (action: UserMutationAction) => {
      if (inFlightRef.current) return;
      inFlightRef.current = true;
      setIsRunning(true);

      const result = await callMutation(action);

      if (result.ok) {
        onToast(resolveSuccessToast(action));
      } else {
        if (result.error.kind === 'forbidden') {
          setHasPermissionError(true);
        }
        onToast(resolveErrorToast(result.error));
      }

      await refetch();
      setIsRunning(false);
      inFlightRef.current = false;
    },
    [onToast, refetch]
  );

  return { run, isRunning, hasPermissionError };
}
