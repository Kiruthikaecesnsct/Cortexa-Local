import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader } from '../../shared/layout/PageHeader';
import { Input, Switch } from '../../shared/ds';
import { useSession } from '../../core/auth/useSession';
import { useThemeContext } from '../../shared/theme/ThemeContext';
import { usePreferences } from './usePreferences';

const cardStyle: React.CSSProperties = {
  background: 'var(--surface-card)',
  border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-lg)',
  boxShadow: 'var(--shadow-xs)',
  padding: 28,
};

export function ProfilePage() {
  const { user } = useSession();
  const { themeMode, applyTheme } = useThemeContext();
  const { notifications, setNotification } = usePreferences();

  const displayName = user?.email.split('@')[0] ?? '—';
  const initials = displayName
    .split(/[.\-_]/)
    .map((p) => p[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();

  return (
    <AppShell>
      <PageHeader title="Profile" />

      <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 900 }}>
        <div style={cardStyle}>
          <div style={{ fontWeight: 600, fontSize: 18, color: 'var(--text-primary)', marginBottom: 20 }}>Personal Details</div>
          <div
            style={{
              width: 84,
              height: 84,
              borderRadius: '50%',
              background: 'var(--accent-grad)',
              marginBottom: 24,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: '#fff',
              fontSize: 28,
              fontWeight: 600,
            }}
          >
            {initials}
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: 20 }}>
            <Input label="Email" value={user?.email ?? ''} disabled readOnly />
            <Input label="Name" value={displayName} disabled readOnly />
            <Input label="Role" value={user?.role ?? ''} disabled readOnly />
          </div>
          <div
            style={{
              marginTop: 20,
              padding: '14px 16px',
              borderRadius: 'var(--radius-md)',
              background: 'var(--status-warning-bg)',
              color: 'var(--status-warning-fg)',
              fontSize: 13,
              lineHeight: 1.5,
            }}
          >
            Profile editing isn't available yet — the account API isn't routed by the gateway in this build. This is an
            honest placeholder, not a working form.
          </div>
        </div>

        <div style={{ ...cardStyle, display: 'flex', flexDirection: 'column', gap: 18 }}>
          <div style={{ fontWeight: 600, fontSize: 18, color: 'var(--text-primary)' }}>Preferences</div>
          <div>
            <div style={{ fontSize: 11, fontWeight: 600, letterSpacing: '0.04em', textTransform: 'uppercase', color: 'var(--text-muted)', marginBottom: 10 }}>
              Appearance
            </div>
            <Switch label="Dark mode" checked={themeMode === 'dark'} onChange={(v) => applyTheme(v ? 'dark' : 'light')} />
          </div>
          <div style={{ borderTop: '1px solid var(--border-subtle)', paddingTop: 16 }}>
            <div style={{ fontSize: 11, fontWeight: 600, letterSpacing: '0.04em', textTransform: 'uppercase', color: 'var(--text-muted)', marginBottom: 10 }}>
              Notifications
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <Switch label="Batch completion" checked={notifications.batchCompletion} onChange={(v) => setNotification('batchCompletion', v)} />
              <Switch label="New opportunities" checked={notifications.newOpportunities} onChange={(v) => setNotification('newOpportunities', v)} />
            </div>
          </div>
        </div>
      </div>
    </AppShell>
  );
}
