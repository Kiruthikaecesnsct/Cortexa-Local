import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useSession } from './useSession';
import * as jwtClaimsModule from './jwtClaims';
import * as tokenStoreModule from './tokenStore';
import type { SessionUser } from './authTypes';

vi.mock('./jwtClaims');
vi.mock('./tokenStore');

const mockedDecodeAccessToken = vi.mocked(jwtClaimsModule.decodeAccessToken);
const mockedGetAccessToken = vi.mocked(tokenStoreModule.getAccessToken);

function stubUser(overrides: Partial<SessionUser>): void {
  mockedGetAccessToken.mockReturnValue('token');
  mockedDecodeAccessToken.mockReturnValue({
    sub: 'user-1',
    email: 'user@example.com',
    role: 'Researcher',
    roles: [],
    perms: [],
    orgId: 'org-1',
    exp: Math.floor(Date.now() / 1000) + 3600,
    ...overrides,
  });
}

describe('useSession hasAnyRole', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns true when the requested role matches the scalar role field only', () => {
    stubUser({ role: 'Admin', roles: [] });

    const { hasAnyRole } = useSession();

    expect(hasAnyRole(['Admin', 'SuperAdmin'])).toBe(true);
  });

  it('returns true when the requested role is present only in the roles array', () => {
    stubUser({ role: 'Researcher', roles: ['SuperAdmin'] });

    const { hasAnyRole } = useSession();

    expect(hasAnyRole(['Admin', 'SuperAdmin'])).toBe(true);
  });

  it('returns true when the requested role is present in both the scalar role and the roles array', () => {
    stubUser({ role: 'Admin', roles: ['Admin'] });

    const { hasAnyRole } = useSession();

    expect(hasAnyRole(['Admin', 'SuperAdmin'])).toBe(true);
  });

  it('returns false when the requested role is present in neither the scalar role nor the roles array', () => {
    stubUser({ role: 'Researcher', roles: ['Reviewer'] });

    const { hasAnyRole } = useSession();

    expect(hasAnyRole(['Admin', 'SuperAdmin'])).toBe(false);
  });

  it('returns false when the roles array is empty and the scalar role does not match', () => {
    stubUser({ role: 'Researcher', roles: [] });

    const { hasAnyRole } = useSession();

    expect(hasAnyRole(['Admin', 'SuperAdmin'])).toBe(false);
  });

  it('returns false when there is no authenticated user', () => {
    mockedGetAccessToken.mockReturnValue(undefined);

    const { hasAnyRole } = useSession();

    expect(hasAnyRole(['Admin', 'SuperAdmin'])).toBe(false);
  });
});
