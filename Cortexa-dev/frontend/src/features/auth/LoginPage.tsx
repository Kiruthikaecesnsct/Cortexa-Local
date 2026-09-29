import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AuthLayout } from '../../shared/layout/AuthLayout';
import { Button, Input } from '../../shared/ds';
import { useLoginStore } from './useLoginStore';
import { entraConfigured } from '../../core/auth/msalConfig';
import { loginWithEntra as msalLogin } from '../../core/auth/msalClient';
import { loginWithEntra as exchangeEntra } from './authRepository';
import { setAccessToken } from '../../core/auth/tokenStore';

export function LoginPage() {
  const navigate = useNavigate();
  const store = useLoginStore();
  const [entraBusy, setEntraBusy] = useState(false);
  const [entraError, setEntraError] = useState<string | null>(null);

  useEffect(() => {
    if (store.status === 'success') navigate('/dashboard', { replace: true });
  }, [store.status, navigate]);

  async function onEntra() {
    setEntraError(null);
    setEntraBusy(true);
    try {
      const idToken = await msalLogin();
      const result = await exchangeEntra({ entra_id_token: idToken });
      if (result.ok) {
        setAccessToken(result.data.access_token, result.data.expires_at);
        navigate('/dashboard', { replace: true });
      } else {
        setEntraError(result.error.message);
      }
    } catch {
      setEntraError('Microsoft sign-in was cancelled or failed.');
    } finally {
      setEntraBusy(false);
    }
  }

  const submitting = store.status === 'submitting';

  return (
    <AuthLayout>
      <div style={{ fontFamily: 'var(--font-display)', fontSize: 28, color: 'var(--text-primary)' }}>
        Welcome back to Cortexa™
      </div>

      {store.authError && (
        <div
          role="alert"
          style={{
            background: 'var(--status-danger-bg)',
            color: 'var(--status-danger-fg)',
            borderRadius: 'var(--radius-md)',
            padding: '12px 14px',
            fontSize: 13,
          }}
        >
          {store.authError.message}
          {store.authError.correlationId && (
            <div style={{ fontFamily: 'var(--font-mono)', fontSize: 11, opacity: 0.8, marginTop: 4 }}>
              ref: {store.authError.correlationId}
            </div>
          )}
        </div>
      )}

      <form
        onSubmit={(e) => {
          e.preventDefault();
          void store.submit();
        }}
        style={{ display: 'flex', flexDirection: 'column', gap: 14 }}
      >
        <Input
          label="Email"
          type="email"
          placeholder="you@lab.edu"
          value={store.email}
          error={store.fieldErrors.email}
          onChange={(e) => store.setEmail(e.target.value)}
        />
        <Input
          label="Password"
          type={store.passwordVisible ? 'text' : 'password'}
          placeholder="••••••••"
          value={store.password}
          error={store.fieldErrors.password}
          onChange={(e) => store.setPassword(e.target.value)}
        />
        <Button type="submit" variant="primary" size="lg" fullWidth disabled={submitting}>
          {submitting ? 'Signing in…' : 'Sign In'}
        </Button>
      </form>

      {entraConfigured && (
        <>
          <div style={{ textAlign: 'center', color: 'var(--text-muted)', fontSize: 13, margin: '2px 0' }}>or</div>
          {entraError && (
            <div role="alert" style={{ color: 'var(--status-danger-fg)', fontSize: 13, textAlign: 'center' }}>
              {entraError}
            </div>
          )}
          <Button variant="secondary" size="lg" fullWidth disabled={entraBusy} onClick={onEntra}>
            {entraBusy ? 'Connecting…' : 'Continue with Microsoft'}
          </Button>
        </>
      )}

      <div style={{ textAlign: 'center', fontSize: 14, color: 'var(--text-body)' }}>
        New to Cortexa?{' '}
        <a
          href="/signup"
          onClick={(e) => {
            e.preventDefault();
            navigate('/signup');
          }}
          style={{ fontWeight: 600 }}
        >
          Sign up
        </a>
      </div>
    </AuthLayout>
  );
}
