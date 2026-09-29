import { useNavigate } from 'react-router-dom';
import { Button } from './primitives';

export function PermissionDenied({ role }: { role?: string }) {
  const navigate = useNavigate();
  return (
    <div
      style={{
        background: 'var(--surface-card)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-xs)',
        padding: 48,
        textAlign: 'center',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 10,
        fontFamily: 'var(--font-body)',
      }}
    >
      <div
        style={{
          width: 48,
          height: 48,
          borderRadius: '50%',
          background: 'var(--status-danger-bg)',
          color: 'var(--status-danger-fg)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 22,
        }}
      >
        🔒
      </div>
      <div style={{ fontFamily: 'var(--font-display)', fontSize: 'var(--text-xl)', color: 'var(--text-primary)' }}>
        Permission required
      </div>
      <div style={{ color: 'var(--text-muted)', fontSize: 'var(--text-base)', maxWidth: 440 }}>
        {role ? `Your current role (${role}) doesn't` : "You don't"} have access to this page. Ask an Admin for access.
      </div>
      <Button variant="secondary" onClick={() => navigate('/dashboard')}>
        Back to Dashboard
      </Button>
    </div>
  );
}
