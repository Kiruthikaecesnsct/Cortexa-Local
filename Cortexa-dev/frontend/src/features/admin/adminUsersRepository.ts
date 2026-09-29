import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import type { Role } from '../../core/auth/authTypes';
import { apiBaseUrl } from '../../core/config/env';
import type { AdminUser, AdminUserError, CreateUserInput } from './adminUserTypes';

export const ADMIN_USERS_ENDPOINT = '/admin/users';
export const ADMIN_USER_ENDPOINT = (id: string): string => `/admin/users/${id}`;
export const ADMIN_USER_DISABLE_ENDPOINT = (id: string): string => `/admin/users/${id}/disable`;
export const ADMIN_USER_ENABLE_ENDPOINT = (id: string): string => `/admin/users/${id}/enable`;
export const ADMIN_USER_ROLE_ENDPOINT = (id: string): string => `/admin/users/${id}/role`;

export type ListUsersResult = { ok: true; data: AdminUser[] } | { ok: false; error: AdminUserError };
export type CreateUserResult = { ok: true; data: AdminUser } | { ok: false; error: AdminUserError };
export type UserMutationResult = { ok: true } | { ok: false; error: AdminUserError };

const client = new ApiClient(apiBaseUrl, getAccessToken);

function extractStatusCode(err: unknown): number | undefined {
  if (
    err !== null &&
    typeof err === 'object' &&
    'response' in err &&
    err.response !== null &&
    typeof err.response === 'object' &&
    'status' in err.response &&
    typeof (err.response as { status: unknown }).status === 'number'
  ) {
    return (err.response as { status: number }).status;
  }
  return undefined;
}

function extractCorrelationId(err: unknown): string | undefined {
  if (
    err !== null &&
    typeof err === 'object' &&
    'response' in err &&
    err.response !== null &&
    typeof err.response === 'object' &&
    'data' in err.response
  ) {
    const data = (err.response as { data?: ApiResponse<unknown> }).data;
    return data?.correlation_id;
  }
  return undefined;
}

function extractServerMessage(err: unknown, fallback: string): string {
  if (
    err !== null &&
    typeof err === 'object' &&
    'response' in err &&
    err.response !== null &&
    typeof err.response === 'object' &&
    'data' in err.response
  ) {
    const data = (err.response as { data?: ApiResponse<unknown> }).data;
    if (data?.message) return data.message;
  }
  return fallback;
}

function toAdminUserError(raw: ApiResponse<unknown>): AdminUserError {
  return {
    kind: 'server',
    message: raw.message ?? "Couldn't load users right now.",
    correlationId: raw.correlation_id,
    errorCode: raw.error_code,
  };
}

function networkError(correlationId?: string): AdminUserError {
  return {
    kind: 'network',
    message: "Couldn't complete the change. Try again in a moment.",
    correlationId,
  };
}

function forbiddenError(correlationId?: string): AdminUserError {
  return {
    kind: 'forbidden',
    message: 'You need the Admin role to manage users. Your access may have changed — try signing in again.',
    correlationId,
  };
}

function toMutationErrorResult(err: unknown): { ok: false; error: AdminUserError } {
  if (extractStatusCode(err) === 403) {
    return { ok: false, error: forbiddenError(extractCorrelationId(err)) };
  }
  return { ok: false, error: networkError(extractCorrelationId(err)) };
}

export async function listUsers(): Promise<ListUsersResult> {
  try {
    const response = await client.get<AdminUser[]>(ADMIN_USERS_ENDPOINT);
    if (!response.success || !response.data) {
      return { ok: false, error: toAdminUserError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    if (extractStatusCode(err) === 403) {
      return { ok: false, error: forbiddenError(extractCorrelationId(err)) };
    }
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function createUser(input: CreateUserInput): Promise<CreateUserResult> {
  try {
    const response = await client.post<AdminUser>(ADMIN_USERS_ENDPOINT, input);
    if (!response.success || !response.data) {
      return { ok: false, error: toAdminUserError(response) };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    if (extractStatusCode(err) === 403) {
      return { ok: false, error: forbiddenError(extractCorrelationId(err)) };
    }
    if (extractStatusCode(err) === 400) {
      return {
        ok: false,
        error: {
          kind: 'validation',
          message: extractServerMessage(err, "Couldn't create the user. Check the details and try again."),
          correlationId: extractCorrelationId(err),
        },
      };
    }
    return { ok: false, error: networkError(extractCorrelationId(err)) };
  }
}

export async function enableUser(id: string): Promise<UserMutationResult> {
  try {
    await client.patch<undefined>(ADMIN_USER_ENABLE_ENDPOINT(id), {});
    return { ok: true };
  } catch (err) {
    return toMutationErrorResult(err);
  }
}

export async function disableUser(id: string): Promise<UserMutationResult> {
  try {
    await client.patch<undefined>(ADMIN_USER_DISABLE_ENDPOINT(id), {});
    return { ok: true };
  } catch (err) {
    return toMutationErrorResult(err);
  }
}

export async function changeRole(id: string, role: Role): Promise<UserMutationResult> {
  try {
    await client.patch<undefined>(ADMIN_USER_ROLE_ENDPOINT(id), { role });
    return { ok: true };
  } catch (err) {
    return toMutationErrorResult(err);
  }
}
