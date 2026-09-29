import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { RequireRole } from '../RequireRole';
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

function renderWithGuard(guardProps: { permission?: string; anyRole?: Array<'Researcher' | 'Reviewer' | 'Admin' | 'SuperAdmin'> }) {
  return render(
    <MemoryRouter initialEntries={['/protected']}>
      <Routes>
        <Route path="/login" element={<span>login page</span>} />
        <Route element={<RequireRole {...guardProps} />}>
          <Route path="/protected" element={<span>protected content</span>} />
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

describe('RequireRole', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('redirects to /login when not authenticated', () => {
    stubSession({ isAuthenticated: false });

    renderWithGuard({ permission: 'job:read' });

    expect(screen.getByText('login page')).toBeTruthy();
  });

  it('renders PermissionDenied when authenticated but permission check fails', () => {
    stubSession({ isAuthenticated: true, hasPermission: () => false });

    renderWithGuard({ permission: 'job:read' });

    expect(screen.queryByText('protected content')).toBeNull();
    expect(screen.getByText(/have access to this page/i)).toBeTruthy();
  });

  it('renders children when authenticated and permission check passes', () => {
    stubSession({ isAuthenticated: true, hasPermission: () => true });

    renderWithGuard({ permission: 'job:read' });

    expect(screen.getByText('protected content')).toBeTruthy();
  });

  it('renders children when authenticated and anyRole check passes', () => {
    stubSession({ isAuthenticated: true, hasAnyRole: (roles) => roles.includes('Admin') });

    renderWithGuard({ anyRole: ['Admin'] });

    expect(screen.getByText('protected content')).toBeTruthy();
  });

  it('renders PermissionDenied when anyRole check fails', () => {
    stubSession({ isAuthenticated: true, hasAnyRole: () => false });

    renderWithGuard({ anyRole: ['SuperAdmin'] });

    expect(screen.getByText(/have access to this page/i)).toBeTruthy();
  });
});
