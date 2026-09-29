import type { ReactNode } from 'react';
import type { Role } from './authTypes';
import { useSession } from './useSession';

interface PermissionGateProps {
  permission?: string;
  anyRole?: Role[];
  fallback?: ReactNode;
  children: ReactNode;
}

function checkAccess(
  hasPermission: (permission: string) => boolean,
  hasAnyRole: (roles: Role[]) => boolean,
  permission?: string,
  anyRole?: Role[]
): boolean {
  if (permission !== undefined && !hasPermission(permission)) {
    return false;
  }
  if (anyRole !== undefined && !hasAnyRole(anyRole)) {
    return false;
  }
  return true;
}

export function PermissionGate({
  permission,
  anyRole,
  fallback = null,
  children,
}: PermissionGateProps) {
  const { hasPermission, hasAnyRole } = useSession();
  const allowed = checkAccess(hasPermission, hasAnyRole, permission, anyRole);
  return <>{allowed ? children : fallback}</>;
}
