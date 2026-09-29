import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { AdminUserError } from './adminUserTypes';
import type { Role } from '../../core/auth/authTypes';

const client = new ApiClient(apiBaseUrl, getAccessToken);

export interface PermissionDto {
  id: string;
  name: string;
  description: string;
}

export interface RolePermissionsDto {
  role: string;
  permissions: PermissionDto[];
}

type PermsResult = { ok: true; data: PermissionDto[] } | { ok: false; error: AdminUserError };
type RolePermsResult = { ok: true; data: RolePermissionsDto } | { ok: false; error: AdminUserError };
type MutationResult = { ok: true } | { ok: false; error: AdminUserError };

export const ADMIN_PERMISSIONS_ENDPOINT = '/admin/permissions';
export const ROLE_PERMISSIONS_ENDPOINT = (roleId: string): string => `/admin/roles/${roleId}/permissions`;

function extractCorrelationId(err: unknown): string | undefined {
  if (err !== null && typeof err === 'object' && 'response' in err) {
    return (err as { response?: { data?: ApiResponse<unknown> } }).response?.data?.correlation_id;
  }
  return undefined;
}

function extractStatus(err: unknown): number | undefined {
  if (err !== null && typeof err === 'object' && 'response' in err) {
    return (err as { response?: { status?: number } }).response?.status;
  }
  return undefined;
}

function toError(err: unknown, fallback: string): AdminUserError {
  const kind = extractStatus(err) === 403 ? 'forbidden' : 'network';
  return { kind, message: fallback, correlationId: extractCorrelationId(err) };
}

export async function fetchPermissions(): Promise<PermsResult> {
  try {
    const response = await client.get<PermissionDto[]>(ADMIN_PERMISSIONS_ENDPOINT);
    if (!response.success || !response.data) {
      return { ok: false, error: { kind: 'server', message: response.message ?? 'Could not load permissions.', correlationId: response.correlation_id } };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: toError(err, "Can't load the permission catalog right now.") };
  }
}

export async function fetchRolePermissions(roleId: Role): Promise<RolePermsResult> {
  try {
    const response = await client.get<RolePermissionsDto>(ROLE_PERMISSIONS_ENDPOINT(roleId));
    if (!response.success || !response.data) {
      return { ok: false, error: { kind: 'server', message: response.message ?? 'Could not load role permissions.', correlationId: response.correlation_id } };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    return { ok: false, error: toError(err, "Can't load role permissions right now.") };
  }
}

export async function replaceRolePermissions(roleId: Role, permissionNames: string[]): Promise<MutationResult> {
  try {
    const response = await client.put<unknown>(ROLE_PERMISSIONS_ENDPOINT(roleId), { permissionNames });
    if (!response.success) {
      return { ok: false, error: { kind: 'server', message: response.message ?? 'Could not update role.', correlationId: response.correlation_id } };
    }
    return { ok: true };
  } catch (err) {
    return { ok: false, error: toError(err, "Can't update role permissions right now.") };
  }
}
