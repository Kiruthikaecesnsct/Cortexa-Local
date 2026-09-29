import { describe, it, expect, vi, beforeEach } from 'vitest';

const { mockGet, mockPost, mockPatch } = vi.hoisted(() => ({
  mockGet: vi.fn(),
  mockPost: vi.fn(),
  mockPatch: vi.fn(),
}));

vi.mock('../../../core/api/client', () => ({
  ApiClient: vi.fn().mockImplementation(() => ({
    get: mockGet,
    post: mockPost,
    patch: mockPatch,
  })),
}));

vi.mock('../../../core/auth/tokenStore', () => ({
  getAccessToken: () => 'test-token',
}));

vi.mock('../../../core/config/env', () => ({
  apiBaseUrl: 'http://api.test',
}));

import {
  listUsers,
  createUser,
  enableUser,
  disableUser,
  changeRole,
  ADMIN_USERS_ENDPOINT,
  ADMIN_USER_ENABLE_ENDPOINT,
  ADMIN_USER_DISABLE_ENDPOINT,
  ADMIN_USER_ROLE_ENDPOINT,
} from '../adminUsersRepository';
import type { CreateUserInput } from '../adminUserTypes';

const TestUser = {
  id: '1',
  email: 'a@company.com',
  username: 'jsmith',
  role: 'Researcher' as const,
  isEnabled: true,
  organizationId: 'org-1',
};

describe('adminUsersRepository', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('listUsers issues a GET against the admin users endpoint', async () => {
    mockGet.mockResolvedValue({ success: true, data: [TestUser], correlation_id: 'c1' });

    const result = await listUsers();

    expect(mockGet).toHaveBeenCalledWith(ADMIN_USERS_ENDPOINT);
    expect(result).toEqual({ ok: true, data: [TestUser] });
  });

  it('maps a 403 on listUsers to a forbidden result', async () => {
    mockGet.mockRejectedValue({ response: { status: 403, data: { correlation_id: 'c2' } } });

    const result = await listUsers();

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('forbidden');
    }
  });

  it('maps a network failure on listUsers to a network result', async () => {
    mockGet.mockRejectedValue(new Error('boom'));

    const result = await listUsers();

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('network');
    }
  });

  it('createUser posts the input body to the admin users endpoint', async () => {
    const input: CreateUserInput = {
      email: 'a@company.com',
      username: 'jsmith',
      password: 'temp1234',
      role: 'Researcher',
    };
    mockPost.mockResolvedValue({ success: true, data: TestUser, correlation_id: 'c3' });

    const result = await createUser(input);

    expect(mockPost).toHaveBeenCalledWith(ADMIN_USERS_ENDPOINT, input);
    expect(result).toEqual({ ok: true, data: TestUser });
  });

  it('maps a 400 on createUser to a validation result carrying the server message', async () => {
    mockPost.mockRejectedValue({
      response: { status: 400, data: { message: 'Email already exists.', correlation_id: 'c4' } },
    });

    const result = await createUser({
      email: 'a@company.com',
      username: 'jsmith',
      password: 'temp1234',
      role: 'Researcher',
    });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('validation');
      expect(result.error.message).toBe('Email already exists.');
    }
  });

  it('maps a 403 on createUser to a forbidden result', async () => {
    mockPost.mockRejectedValue({ response: { status: 403 } });

    const result = await createUser({
      email: 'a@company.com',
      username: 'jsmith',
      password: 'temp1234',
      role: 'Researcher',
    });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('forbidden');
    }
  });

  it('enableUser issues a PATCH against the enable endpoint', async () => {
    mockPatch.mockResolvedValue({ success: true });

    const result = await enableUser('u1');

    expect(mockPatch).toHaveBeenCalledWith(ADMIN_USER_ENABLE_ENDPOINT('u1'), {});
    expect(result).toEqual({ ok: true });
  });

  it('disableUser issues a PATCH against the disable endpoint', async () => {
    mockPatch.mockResolvedValue({ success: true });

    const result = await disableUser('u1');

    expect(mockPatch).toHaveBeenCalledWith(ADMIN_USER_DISABLE_ENDPOINT('u1'), {});
    expect(result).toEqual({ ok: true });
  });

  it('changeRole issues a PATCH with the new role body', async () => {
    mockPatch.mockResolvedValue({ success: true });

    const result = await changeRole('u1', 'Admin');

    expect(mockPatch).toHaveBeenCalledWith(ADMIN_USER_ROLE_ENDPOINT('u1'), { role: 'Admin' });
    expect(result).toEqual({ ok: true });
  });

  it('maps a 403 on a mutation call to a forbidden result', async () => {
    mockPatch.mockRejectedValue({ response: { status: 403 } });

    const result = await disableUser('u1');

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('forbidden');
    }
  });

  it('maps a network failure on a mutation call to a network result', async () => {
    mockPatch.mockRejectedValue(new Error('boom'));

    const result = await enableUser('u1');

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.kind).toBe('network');
    }
  });
});
