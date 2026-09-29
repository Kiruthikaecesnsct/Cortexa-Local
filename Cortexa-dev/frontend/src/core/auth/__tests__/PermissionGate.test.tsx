import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PermissionGate } from '../PermissionGate';
import * as useSessionModule from '../useSession';

vi.mock('../useSession');

const mockedUseSession = vi.mocked(useSessionModule.useSession);

function stubSession(overrides: Partial<ReturnType<typeof useSessionModule.useSession>>) {
  mockedUseSession.mockReturnValue({
    user: null,
    hasPermission: () => false,
    hasAnyRole: () => false,
    isSuperAdmin: false,
    isAuthenticated: true,
    ...overrides,
  });
}

describe('PermissionGate', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders children when permission check passes', () => {
    stubSession({ hasPermission: () => true });

    render(
      <PermissionGate permission="job:read">
        <span>secret</span>
      </PermissionGate>
    );

    expect(screen.getByText('secret')).toBeTruthy();
  });

  it('renders fallback when permission check fails', () => {
    stubSession({ hasPermission: () => false });

    render(
      <PermissionGate permission="job:read" fallback={<span>denied</span>}>
        <span>secret</span>
      </PermissionGate>
    );

    expect(screen.queryByText('secret')).toBeNull();
    expect(screen.getByText('denied')).toBeTruthy();
  });

  it('renders nothing by default when denied and no fallback provided', () => {
    stubSession({ hasPermission: () => false });

    const { container } = render(
      <PermissionGate permission="job:read">
        <span>secret</span>
      </PermissionGate>
    );

    expect(container.textContent).toBe('');
  });

  it('renders children when anyRole check passes', () => {
    stubSession({ hasAnyRole: (roles) => roles.includes('Admin') });

    render(
      <PermissionGate anyRole={['Admin', 'SuperAdmin']}>
        <span>secret</span>
      </PermissionGate>
    );

    expect(screen.getByText('secret')).toBeTruthy();
  });

  it('renders fallback when anyRole check fails', () => {
    stubSession({ hasAnyRole: () => false });

    render(
      <PermissionGate anyRole={['Admin']} fallback={<span>denied</span>}>
        <span>secret</span>
      </PermissionGate>
    );

    expect(screen.getByText('denied')).toBeTruthy();
  });

  it('requires both permission and anyRole to pass when both are provided', () => {
    stubSession({ hasPermission: () => true, hasAnyRole: () => false });

    render(
      <PermissionGate permission="job:read" anyRole={['Admin']} fallback={<span>denied</span>}>
        <span>secret</span>
      </PermissionGate>
    );

    expect(screen.getByText('denied')).toBeTruthy();
  });
});
