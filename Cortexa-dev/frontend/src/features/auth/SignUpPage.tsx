import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AuthLayout } from '../../shared/layout/AuthLayout';
import { Button, Checkbox, Input, ProgressBar } from '../../shared/ds';
import { useSignUpStore } from './useSignUpStore';

function strength(password: string): { pct: number; color: string; label: string } {
  const pct = Math.min(100, password.length * 12);
  if (pct > 70) return { pct, color: 'var(--status-success-fg)', label: 'Strong' };
  if (pct > 35) return { pct, color: 'var(--status-warning-fg)', label: 'Fair' };
  return { pct, color: 'var(--status-danger-fg)', label: 'Weak' };
}

export function SignUpPage() {
  const navigate = useNavigate();
  const store = useSignUpStore();
  const [agree, setAgree] = useState(false);
  const meter = useMemo(() => strength(store.password), [store.password]);
  const submitting = store.status === 'submitting';

  return (
    <AuthLayout>
      <div style={{ fontFamily: 'var(--font-display)', fontSize: 28, color: 'var(--text-primary)' }}>
        Get Started with Cortexa™
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (agree) void store.submit();
        }}
        style={{ display: 'flex', flexDirection: 'column', gap: 14 }}
      >
        <Input
          label="Display Name"
          placeholder="Jane Chen"
          value={store.fullName}
          error={store.fieldErrors.fullName}
          onChange={(e) => store.setFullName(e.target.value)}
        />
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
          type="password"
          placeholder="••••••••"
          value={store.password}
          error={store.fieldErrors.password}
          onChange={(e) => store.setPassword(e.target.value)}
        />
        <Input
          label="Confirm Password"
          type="password"
          placeholder="••••••••"
          value={store.confirmPassword}
          error={store.fieldErrors.confirmPassword}
          onChange={(e) => store.setConfirmPassword(e.target.value)}
        />
        {store.password.length > 0 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: 'var(--text-muted)', fontWeight: 600 }}>
              <span>STRENGTH</span>
              <span style={{ color: meter.color }}>{meter.label}</span>
            </div>
            <ProgressBar pct={meter.pct} />
          </div>
        )}
        <Checkbox label="I agree to the Terms & Policy" checked={agree} onChange={setAgree} />
        <Button type="submit" variant="primary" size="lg" fullWidth disabled={!agree || submitting}>
          {submitting ? 'Creating account…' : 'Create Account'}
        </Button>
      </form>

      <div style={{ textAlign: 'center', fontSize: 14, color: 'var(--text-body)' }}>
        Already have an account?{' '}
        <a
          href="/login"
          onClick={(e) => {
            e.preventDefault();
            navigate('/login');
          }}
          style={{ fontWeight: 600 }}
        >
          Sign in
        </a>
      </div>
    </AuthLayout>
  );
}
