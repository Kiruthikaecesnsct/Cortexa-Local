export interface LoginCredentials {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface TokenPair {
  access_token: string;
  expires_at: string;
  refresh_token?: string;
}

export interface EntraLoginRequest {
  entra_id_token: string;
}

export type AuthErrorKind = 'credential' | 'network' | 'validation';

export interface AuthError {
  kind: AuthErrorKind;
  message: string;
  correlationId?: string;
}

export interface RegisterFormValues {
  fullName: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
}

export interface RegisterResult {
  registered: true;
  email: string;
}

export type Role = 'Researcher' | 'Reviewer' | 'Admin' | 'SuperAdmin';

export type Permission = string;

export interface SessionUser {
  sub: string;
  email: string;
  role: Role;
  roles: Role[];
  perms: Permission[];
  orgId?: string;
  exp: number;
}
