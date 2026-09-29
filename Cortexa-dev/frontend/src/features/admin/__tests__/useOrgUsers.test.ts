import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useOrgUsers } from '../useOrgUsers';
import type { AdminUser } from '../adminUserTypes';

vi.mock('../adminUsersRepository', () => ({
  listUsers: vi.fn(),
}));

import { listUsers } from '../adminUsersRepository';

const mockListUsers = listUsers as ReturnType<typeof vi.fn>;

const TestUsers: AdminUser[] = [
  {
    id: '1',
    email: 'a@company.com',
    username: 'jsmith',
    role: 'Researcher',
    isEnabled: true,
    organizationId: 'org-1',
  },
];

async function flush(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('useOrgUsers', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('does not call listUsers when disabled', async () => {
    renderHook(() => useOrgUsers(false));
    await flush();

    expect(mockListUsers).not.toHaveBeenCalled();
  });

  it('fetches on mount when enabled and exposes the data', async () => {
    mockListUsers.mockResolvedValue({ ok: true, data: TestUsers });

    const { result } = renderHook(() => useOrgUsers(true));
    await flush();

    expect(mockListUsers).toHaveBeenCalledTimes(1);
    expect(result.current.data).toEqual(TestUsers);
    expect(result.current.isLoading).toBe(false);
    expect(result.current.isForbidden).toBe(false);
  });

  it('sets isForbidden when the initial load returns a forbidden error', async () => {
    mockListUsers.mockResolvedValue({ ok: false, error: { kind: 'forbidden', message: 'nope' } });

    const { result } = renderHook(() => useOrgUsers(true));
    await flush();

    expect(result.current.isForbidden).toBe(true);
    expect(result.current.data).toBeNull();
  });

  it('sets error without isForbidden for a network failure', async () => {
    mockListUsers.mockResolvedValue({ ok: false, error: { kind: 'network', message: 'down' } });

    const { result } = renderHook(() => useOrgUsers(true));
    await flush();

    expect(result.current.isForbidden).toBe(false);
    expect(result.current.error?.kind).toBe('network');
  });

  it('refetch triggers another call to listUsers', async () => {
    mockListUsers.mockResolvedValue({ ok: true, data: TestUsers });

    const { result } = renderHook(() => useOrgUsers(true));
    await flush();

    await act(async () => {
      await result.current.refetch();
    });

    expect(mockListUsers).toHaveBeenCalledTimes(2);
  });
});
