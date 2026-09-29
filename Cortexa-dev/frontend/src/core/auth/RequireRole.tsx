import { Navigate, Outlet } from 'react-router-dom';
import { PermissionDenied } from '../../shared/ds/PermissionDenied';
import type { Role } from './authTypes';
import { useSession } from './useSession';

interface RequireRoleProps {
  permission?: string;
  anyRole?: Role[];
}

function isAllowed(
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

export function RequireRole({ permission, anyRole }: RequireRoleProps) {
  const { isAuthenticated, hasPermission, hasAnyRole } = useSession();

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  if (!isAllowed(hasPermission, hasAnyRole, permission, anyRole)) {
    return <PermissionDenied />;
  }

  return <Outlet />;
}
