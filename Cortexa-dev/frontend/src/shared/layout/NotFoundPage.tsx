import { useNavigate } from 'react-router-dom';
import { getAccessToken } from '../../core/auth/tokenStore';
import { Button } from '../ds';

export function NotFoundPage() {
  const navigate = useNavigate();
  const authed = getAccessToken() !== undefined;
  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--surface-canvas)',
        padding: 24,
        fontFamily: 'var(--font-body)',
      }}
    >
      <div
        style={{
          background: 'var(--surface-card)',
          border: '1px solid var(--border-subtle)',
          borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--shadow-xs)',
          padding: 64,
          textAlign: 'center',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: 10,
          maxWidth: 460,
        }}
      >
        <div style={{ fontFamily: 'var(--font-display)', fontSize: 56, color: 'var(--accent-primary)' }}>404</div>
        <div style={{ fontSize: 16, color: 'var(--text-muted)' }}>This page doesn't exist.</div>
        <Button variant="primary" onClick={() => navigate(authed ? '/dashboard' : '/login')}>
          {authed ? 'Back to Dashboard' : 'Go to Sign in'}
        </Button>
      </div>
    </div>
  );
}
