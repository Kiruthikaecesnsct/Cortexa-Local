import type { Role, SessionUser } from './authTypes';
import { decodeAccessToken } from './jwtClaims';
import { getAccessToken } from './tokenStore';

export interface UseSessionResult {
  user: SessionUser | null;
  hasPermission: (permission: string) => boolean;
  hasAnyRole: (roles: Role[]) => boolean;
  isSuperAdmin: boolean;
  isAuthenticated: boolean;
}

function resolveUser(): SessionUser | null {
  const token = getAccessToken();
  if (token === undefined) {
    return null;
  }
  return decodeAccessToken(token);
}

function getEffectiveRoles(user: SessionUser): Role[] {
  return Array.from(new Set<Role>([user.role, ...user.roles]));
}

export function useSession(): UseSessionResult {
  const user = resolveUser();

  return {
    user,
    hasPermission: (permission: string) => user?.perms.includes(permission) ?? false,
    hasAnyRole: (roles: Role[]) =>
      user !== null && getEffectiveRoles(user).some((effectiveRole) => roles.includes(effectiveRole)),
    isSuperAdmin: user?.role === 'SuperAdmin',
    isAuthenticated: user !== null,
  };
}
