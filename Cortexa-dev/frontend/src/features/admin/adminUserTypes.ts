import type { Role } from '../../core/auth/authTypes';

export interface AdminUser {
  id: string;
  email: string;
  username: string;
  role: Role;
  isEnabled: boolean;
  organizationId: string;
}

export interface CreateUserInput {
  email: string;
  username: string;
  password: string;
  role: Role;
}

export type AdminUserErrorKind = 'network' | 'server' | 'forbidden' | 'validation';

export interface AdminUserError {
  kind: AdminUserErrorKind;
  message: string;
  correlationId?: string;
  errorCode?: string;
}

export const ROLE_OPTIONS: Role[] = ['Researcher', 'Reviewer', 'Admin', 'SuperAdmin'];
