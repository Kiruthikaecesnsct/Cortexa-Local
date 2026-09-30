import { useEffect, useState } from 'react';
import { Spinner } from '../../shared/ds';

const DEFAULT_SLOW_AFTER_MS = 6000;

function useIsSlow(delayMs: number): boolean {
  const [slow, setSlow] = useState(false);
  useEffect(() => {
    const timer = window.setTimeout(() => setSlow(true), delayMs);
    return () => window.clearTimeout(timer);
  }, [delayMs]);
  return slow;
}

function IndeterminateBar() {
  return (
    <div aria-hidden="true" style={{ width: '100%', maxWidth: 320, height: 4, borderRadius: 2, background: 'var(--border-subtle)', overflow: 'hidden' }}>
      <div
        style={{
          width: '40%',
          height: '100%',
          borderRadius: 2,
          background: 'var(--accent-grad, var(--accent-primary))',
          animation: 'cortexa-indeterminate 1.2s ease-in-out infinite',
        }}
      />
    </div>
  );
}

interface ScanLoaderProps {
  title: string;
  message: string;
  slowHint?: string;
  slowAfterMs?: number;
}

export function ScanLoader({ title, message, slowHint, slowAfterMs = DEFAULT_SLOW_AFTER_MS }: ScanLoaderProps) {
  const slow = useIsSlow(slowAfterMs);
  return (
    <div
      role="status"
      aria-live="polite"
      aria-busy="true"
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 12,
        padding: '36px 24px',
        textAlign: 'center',
        borderRadius: 'var(--radius-md)',
        border: '1px dashed var(--border-subtle)',
        background: 'var(--surface-sunken)',
      }}
    >
      <Spinner size={32} />
      <div style={{ fontWeight: 600, fontSize: 'var(--text-md)', color: 'var(--text-primary)' }}>{title}</div>
      <div style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)', maxWidth: 440, lineHeight: 1.5 }}>{message}</div>
      <IndeterminateBar />
      {slow && slowHint && <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', maxWidth: 440 }}>{slowHint}</div>}
    </div>
  );
}
