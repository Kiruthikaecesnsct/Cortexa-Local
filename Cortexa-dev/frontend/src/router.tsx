import { createBrowserRouter, Navigate, Outlet } from 'react-router-dom';
import { getAccessToken } from './core/auth/tokenStore';
import { RequireRole } from './core/auth/RequireRole';
import { LoginPage, SignUpPage } from './features/auth';
import { DashboardPage } from './features/dashboard';
import { JobProgressPage, JobHistoryPage } from './features/jobs';
import { ResultsDashboardPage } from './features/results';
import { OpportunityDetailPage } from './features/opportunity';
import { UploadPage } from './features/upload';
import { ProfilePage } from './features/profile';
import { SettingsPage } from './features/settings';
import { AdminUsersPage, AdminRolesPage, AdminAuditPage } from './features/admin';
import { ScanPage } from './features/scan';
import { NotFoundPage } from './shared/layout/NotFoundPage';

function RequireAuth() {
  return getAccessToken() !== undefined ? <Outlet /> : <Navigate to="/login" replace />;
}

function RootRedirect() {
  return <Navigate to={getAccessToken() !== undefined ? '/dashboard' : '/login'} replace />;
}

export const router = createBrowserRouter([
  { path: '/', element: <RootRedirect /> },
  { path: '/login', element: <LoginPage /> },
  { path: '/signup', element: <SignUpPage /> },
  {
    element: <RequireAuth />,
    children: [
      { path: '/dashboard', element: <DashboardPage /> },
      { path: '/batches/new', element: <UploadPage /> },
      { path: '/batches', element: <JobHistoryPage /> },
      { path: '/batches/:batchId', element: <JobProgressPage /> },
      { path: '/batches/:batchId/results', element: <ResultsDashboardPage /> },
      { path: '/batches/:batchId/results/:candidateId', element: <OpportunityDetailPage /> },
      { path: '/profile', element: <ProfilePage /> },
      { path: '/settings', element: <SettingsPage /> },
      { path: '/settings/:tabId', element: <SettingsPage /> },
      {
        element: <RequireRole permission="jobs:submit" />,
        children: [{ path: '/scan', element: <ScanPage /> }],
      },
      {
        element: <RequireRole permission="admin:users:read" anyRole={['Admin', 'SuperAdmin']} />,
        children: [{ path: '/admin/users', element: <AdminUsersPage /> }],
      },
      {
        element: <RequireRole anyRole={['SuperAdmin']} />,
        children: [
          { path: '/admin/roles', element: <AdminRolesPage /> },
          { path: '/admin/audit', element: <AdminAuditPage /> },
        ],
      },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
]);
