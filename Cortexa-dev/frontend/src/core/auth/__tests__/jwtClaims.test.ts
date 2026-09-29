import { describe, it, expect } from 'vitest';
import { decodeAccessToken } from '../jwtClaims';

function base64url(input: string): string {
  return Buffer.from(input, 'utf-8')
    .toString('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

function makeToken(payload: Record<string, unknown>): string {
  const header = base64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const body = base64url(JSON.stringify(payload));
  return `${header}.${body}.signature`;
}

const VALID_PAYLOAD = {
  sub: 'user-1',
  email: 'user@example.com',
  role: 'Researcher',
  roles: ['Researcher'],
  perms: ['job:read'],
  org_id: 'org-1',
  exp: Math.floor(Date.now() / 1000) + 3600,
};

describe('decodeAccessToken', () => {
  it('decodes a valid token into a SessionUser', () => {
    const token = makeToken(VALID_PAYLOAD);

    const claims = decodeAccessToken(token);

    expect(claims).toEqual({
      sub: 'user-1',
      email: 'user@example.com',
      role: 'Researcher',
      roles: ['Researcher'],
      perms: ['job:read'],
      orgId: 'org-1',
      exp: VALID_PAYLOAD.exp,
    });
  });

  it('decodes a system SuperAdmin token that omits org_id', () => {
    const noOrg: Record<string, unknown> = { ...VALID_PAYLOAD, role: 'SuperAdmin', roles: ['SuperAdmin'] };
    delete noOrg['org_id'];
    const token = makeToken(noOrg);

    const claims = decodeAccessToken(token);

    expect(claims).not.toBeNull();
    expect(claims?.role).toBe('SuperAdmin');
    expect(claims?.orgId).toBeUndefined();
  });

  it('returns null for an expired token', () => {
    const token = makeToken({ ...VALID_PAYLOAD, exp: Math.floor(Date.now() / 1000) - 60 });

    expect(decodeAccessToken(token)).toBeNull();
  });

  it('returns null for a malformed token with wrong segment count', () => {
    expect(decodeAccessToken('not-a-jwt')).toBeNull();
  });

  it('returns null when the payload segment is not valid base64', () => {
    expect(decodeAccessToken('header.!!!not-base64!!!.sig')).toBeNull();
  });

  it('returns null when the payload is missing required claims', () => {
    const token = makeToken({ sub: 'user-1', email: 'user@example.com' });

    expect(decodeAccessToken(token)).toBeNull();
  });

  it('returns null when roles or perms are not string arrays', () => {
    const token = makeToken({ ...VALID_PAYLOAD, perms: [1, 2, 3] });

    expect(decodeAccessToken(token)).toBeNull();
  });
});
