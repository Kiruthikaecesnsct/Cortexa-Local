import { type ReactNode } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import { useSession } from '../../core/auth/useSession';
import { useThemeContext } from '../theme/ThemeContext';
import { logout } from '../../features/auth/authRepository';
import {
  IconDashboard,
  IconUpload,
  IconHistory,
  IconUser,
  IconSettings,
  IconUsers,
  IconShield,
  IconAudit,
  IconLogout,
} from '../ds/icons';

interface NavItemDef {
  to: string;
  label: string;
  icon: ReactNode;
  show: boolean;
  end?: boolean;
}

interface NavGroup {
  title: string;
  items: NavItemDef[];
}

const linkStyle = (active: boolean, muted = false): React.CSSProperties => ({
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  width: '100%',
  padding: '10px 12px',
  border: 'none',
  borderRadius: 'var(--radius-md)',
  cursor: 'pointer',
  fontFamily: 'var(--font-body)',
  fontWeight: 500,
  fontSize: 'var(--text-base)',
  letterSpacing: '-0.5px',
  textAlign: 'left',
  textDecoration: 'none',
  transition: 'background var(--duration-fast) var(--ease-standard)',
  background: active ? 'var(--accent-primary)' : 'transparent',
  color: active ? '#fff' : muted ? 'var(--text-muted)' : 'var(--text-primary)',
});

function SideLink({ item }: { item: NavItemDef }) {
  return (
    <NavLink to={item.to} end={item.end} style={({ isActive }) => linkStyle(isActive)}>
      <span style={{ width: 18, height: 18, display: 'inline-flex', flexShrink: 0 }}>{item.icon}</span>
      {item.label}
    </NavLink>
  );
}

const CRUMBS: { prefix: string; crumb: string }[] = [
  { prefix: '/dashboard', crumb: 'Dashboard' },
  { prefix: '/batches/new', crumb: 'Dashboard / New Analysis' },
  { prefix: '/batches', crumb: 'Dashboard / Batch History' },
  { prefix: '/profile', crumb: 'Account / Profile' },
  { prefix: '/settings', crumb: 'Account / Settings' },
  { prefix: '/admin/users', crumb: 'Administration / Users' },
  { prefix: '/admin/roles', crumb: 'Administration / Roles & Permissions' },
  { prefix: '/admin/audit', crumb: 'Administration / Audit Log' },
];

function breadcrumbFor(pathname: string): string {
  if (/^\/batches\/[^/]+\/results\/[^/]+/.test(pathname))
    return 'Dashboard / Batch History / Results / Opportunity Detail';
  if (/^\/batches\/[^/]+\/results/.test(pathname)) return 'Dashboard / Batch History / Results';
  if (/^\/batches\/[^/]+$/.test(pathname)) return 'Dashboard / Batch History / Progress';
  const match = [...CRUMBS].sort((a, b) => b.prefix.length - a.prefix.length).find((c) => pathname.startsWith(c.prefix));
  return match?.crumb ?? 'Dashboard';
}

export function AppShell({ children }: { children: ReactNode }) {
  const { user, hasPermission, isSuperAdmin } = useSession();
  const { themeMode, toggleTheme } = useThemeContext();
  const location = useLocation();

  const canUpload = hasPermission('jobs:submit');
  const showAdmin = hasPermission('admin:users:read') || isSuperAdmin;

  const groups: NavGroup[] = [
    {
      title: 'Workspace',
      items: [{ to: '/dashboard', label: 'Dashboard', icon: <IconDashboard />, show: true }],
    },
    {
      title: 'Analysis',
      items: [
        { to: '/batches/new', label: 'New Analysis', icon: <IconUpload />, show: canUpload },
        { to: '/batches', label: 'Batch History', icon: <IconHistory />, show: true, end: true },
      ],
    },
    {
      title: 'Account',
      items: [
        { to: '/profile', label: 'Profile', icon: <IconUser />, show: true },
        { to: '/settings', label: 'Settings', icon: <IconSettings />, show: true },
      ],
    },
    {
      title: 'Administration',
      items: [
        { to: '/admin/users', label: 'Users', icon: <IconUsers />, show: showAdmin },
        { to: '/admin/roles', label: 'Roles & Permissions', icon: <IconShield />, show: isSuperAdmin },
        { to: '/admin/audit', label: 'Audit Log', icon: <IconAudit />, show: isSuperAdmin },
      ],
    },
  ];

  const initials = (user?.email ?? 'U')
    .split('@')[0]!
    .split(/[.\-_]/)
    .map((p) => p[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();

  async function onLogout() {
    await logout();
    window.location.href = '/login';
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        background: 'var(--surface-canvas)',
        display: 'flex',
        fontFamily: 'var(--font-body)',
        color: 'var(--text-primary)',
      }}
    >
      <a
        href="#cortexa-main"
        style={{
          position: 'absolute',
          left: -9999,
          top: 0,
          zIndex: 999,
          background: 'var(--accent-grad)',
          color: '#fff',
          padding: '10px 16px',
          borderRadius: 'var(--radius-sm)',
        }}
        onFocus={(e) => {
          e.currentTarget.style.left = '12px';
          e.currentTarget.style.top = '12px';
        }}
        onBlur={(e) => {
          e.currentTarget.style.left = '-9999px';
        }}
      >
        Skip to main content
      </a>

      {/* Sidebar */}
      <nav
        aria-label="Primary"
        style={{
          width: 'var(--sidebar-width)',
          flexShrink: 0,
          display: 'flex',
          flexDirection: 'column',
          background: 'var(--sidebar-bg)',
          backdropFilter: 'blur(22px)',
          WebkitBackdropFilter: 'blur(22px)',
          borderRight: '1px solid var(--border-subtle)',
          padding: '14px 0',
          position: 'sticky',
          top: 0,
          height: '100vh',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '2px 16px 18px' }}>
          <img
            src="/brand/cortexa-icon-blue-tile.png"
            alt="Cortexa"
            style={{ width: 22, height: 22, objectFit: 'contain', borderRadius: 6, flexShrink: 0, transform: 'translateY(-1px)' }}
          />
          <span style={{ fontFamily: 'var(--font-display)', fontWeight: 500, fontSize: 16, letterSpacing: '-0.01em', color: 'var(--text-primary)' }}>
            Cortexa
          </span>
        </div>

        <div style={{ flex: 1, overflowY: 'auto' }}>
          {groups.map((group) => {
            const visible = group.items.filter((i) => i.show);
            if (visible.length === 0) return null;
            return (
              <div key={group.title} style={{ padding: '8px 10px 14px', display: 'flex', flexDirection: 'column', gap: 2 }}>
                <div
                  style={{
                    fontSize: 10.5,
                    fontWeight: 600,
                    letterSpacing: '0.06em',
                    textTransform: 'uppercase',
                    color: 'var(--text-muted)',
                    padding: '6px 10px 4px',
                  }}
                >
                  {group.title}
                </div>
                {visible.map((item) => (
                  <SideLink key={item.to} item={item} />
                ))}
              </div>
            );
          })}
        </div>

        <div style={{ borderTop: '1px solid var(--border-subtle)', padding: '10px 10px 2px' }}>
          <button type="button" onClick={onLogout} style={linkStyle(false, true)}>
            <span style={{ width: 18, height: 18, display: 'inline-flex', flexShrink: 0 }}>
              <IconLogout />
            </span>
            Logout
          </button>
        </div>
      </nav>

      {/* Main column */}
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <header
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '12px 28px',
            borderBottom: '1px solid var(--border-subtle)',
            background: 'var(--topbar-bg)',
            backdropFilter: 'blur(18px)',
            WebkitBackdropFilter: 'blur(18px)',
            gap: 10,
            position: 'sticky',
            top: 0,
            zIndex: 50,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, color: 'var(--text-muted)', fontSize: 13, fontWeight: 500 }}>
            {breadcrumbFor(location.pathname)}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexShrink: 0 }}>
            <button
              type="button"
              onClick={toggleTheme}
              title="Toggle theme"
              aria-label="Toggle theme"
              style={{
                width: 30,
                height: 30,
                borderRadius: 8,
                border: '1px solid var(--border-subtle)',
                background: 'var(--surface-sunken)',
                cursor: 'pointer',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                color: 'var(--text-primary)',
                fontSize: 14,
              }}
            >
              {themeMode === 'light' ? '☾' : '☀'}
            </button>
            <div
              title={user?.email}
              style={{
                width: 30,
                height: 30,
                borderRadius: '50%',
                background: 'var(--accent-grad)',
                color: '#fff',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontWeight: 600,
                fontSize: 12,
              }}
            >
              {initials}
            </div>
          </div>
        </header>

        <main id="cortexa-main" style={{ flex: 1, background: 'var(--surface-sunken)', padding: '28px 32px', overflow: 'auto' }}>
          <div style={{ maxWidth: 'var(--content-max)', margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 20 }}>
            {children}
          </div>
        </main>
      </div>
    </div>
  );
}
