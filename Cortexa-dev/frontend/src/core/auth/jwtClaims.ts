import type { Permission, Role, SessionUser } from './authTypes';

interface RawJwtPayload {
  sub?: unknown;
  email?: unknown;
  role?: unknown;
  roles?: unknown;
  perms?: unknown;
  org_id?: unknown;
  exp?: unknown;
}

function decodeBase64Url(segment: string): string | null {
  try {
    const normalized = segment.replace(/-/g, '+').replace(/_/g, '/');
    const padded = normalized.padEnd(normalized.length + ((4 - (normalized.length % 4)) % 4), '=');
    return atob(padded);
  } catch {
    return null;
  }
}

function parsePayload(token: string): RawJwtPayload | null {
  const parts = token.split('.');
  const payloadSegment = parts[1];
  if (parts.length !== 3 || payloadSegment === undefined) {
    return null;
  }
  const decoded = decodeBase64Url(payloadSegment);
  if (decoded === null) {
    return null;
  }
  try {
    return JSON.parse(decoded) as RawJwtPayload;
  } catch {
    return null;
  }
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string');
}

type ValidClaims = Omit<Required<RawJwtPayload>, 'org_id'> & { org_id?: unknown };

function isValidClaims(payload: RawJwtPayload): payload is ValidClaims {
  return (
    typeof payload.sub === 'string' &&
    typeof payload.email === 'string' &&
    typeof payload.role === 'string' &&
    isStringArray(payload.roles) &&
    isStringArray(payload.perms) &&
    // org_id is optional: system users such as SuperAdmin have no organization,
    // and the identity service omits the claim entirely for them.
    (payload.org_id === undefined || typeof payload.org_id === 'string') &&
    typeof payload.exp === 'number'
  );
}

function isExpired(exp: number): boolean {
  return exp * 1000 <= Date.now();
}

function toSessionUser(payload: ValidClaims): SessionUser {
  return {
    sub: payload.sub as string,
    email: payload.email as string,
    role: payload.role as Role,
    roles: payload.roles as Role[],
    perms: payload.perms as Permission[],
    orgId: typeof payload.org_id === 'string' ? payload.org_id : undefined,
    exp: payload.exp as number,
  };
}

export function decodeAccessToken(token: string): SessionUser | null {
  const payload = parsePayload(token);
  if (payload === null || !isValidClaims(payload)) {
    return null;
  }
  if (isExpired(payload.exp as number)) {
    return null;
  }
  return toSessionUser(payload);
}
