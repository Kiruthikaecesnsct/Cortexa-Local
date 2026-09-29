/* ============================================================
   Cortexa Design System — feedback & state components
   Modal, StatCard, EmptyState, ErrorState, Spinner, Skeleton,
   ScoreGauge, StatusDot.
   ============================================================ */
import { type CSSProperties, type ReactNode, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { Button } from './primitives';
import { IconCheck, IconAlertCircle } from './icons';

/* ---------------- Spinner ---------------- */
export function Spinner({ size = 20, fullscreen = false }: { size?: number; fullscreen?: boolean }) {
  const el = (
    <span
      role="status"
      aria-label="Loading"
      style={{
        width: size,
        height: size,
        borderRadius: '50%',
        border: `${Math.max(2, Math.round(size / 9))}px solid var(--border-subtle)`,
        borderTopColor: 'var(--accent-primary)',
        display: 'inline-block',
        animation: 'cortexa-spin 0.8s linear infinite',
      }}
    />
  );
  if (!fullscreen) return el;
  return (
    <div style={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>{el}</div>
  );
}

/* ---------------- StatCard ---------------- */
export function StatCard({
  label,
  value,
  sublabel,
  loading = false,
}: {
  label: string;
  value: ReactNode;
  sublabel?: string;
  loading?: boolean;
}) {
  if (loading) {
    return (
      <div
        style={{
          height: 100,
          borderRadius: 'var(--radius-lg)',
          background: 'var(--surface-card)',
          boxShadow: 'var(--shadow-sm)',
          animation: 'cortexa-pulse 1.4s ease-in-out infinite',
        }}
      />
    );
  }
  return (
    <div
      style={{
        background: 'var(--surface-card)',
        backdropFilter: 'blur(16px)',
        WebkitBackdropFilter: 'blur(16px)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-xs)',
        padding: 20,
        minHeight: 100,
        fontFamily: 'var(--font-body)',
      }}
    >
      <div
        style={{
          fontSize: 'var(--text-xs)',
          fontWeight: 700,
          letterSpacing: '0.06em',
          color: 'var(--text-muted)',
          textTransform: 'uppercase',
        }}
      >
        {label}
      </div>
      <div style={{ fontFamily: 'var(--font-display)', fontSize: 'var(--text-3xl)', color: 'var(--text-primary)', marginTop: 8 }}>
        {value}
      </div>
      {sublabel && <div style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)', marginTop: 4 }}>{sublabel}</div>}
    </div>
  );
}

/* ---------------- Panel (card wrapper for states) ---------------- */
function StatePanel({ children, style }: { children: ReactNode; style?: CSSProperties }) {
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
        ...style,
      }}
    >
      {children}
    </div>
  );
}

/* ---------------- EmptyState ---------------- */
export function EmptyState({
  title,
  message,
  action,
}: {
  title: string;
  message?: string;
  action?: { label: string; onClick: () => void };
}) {
  return (
    <StatePanel style={{ padding: 56 }}>
      <div style={{ fontFamily: 'var(--font-display)', fontSize: 'var(--text-lg)', color: 'var(--text-primary)' }}>{title}</div>
      {message && <div style={{ color: 'var(--text-muted)', fontSize: 'var(--text-base)', maxWidth: 460 }}>{message}</div>}
      {action && (
        <Button variant="primary" onClick={action.onClick}>
          {action.label}
        </Button>
      )}
    </StatePanel>
  );
}

/* ---------------- ErrorState ---------------- */
export function ErrorState({
  title = "Something went wrong",
  message,
  correlationId,
  onRetry,
}: {
  title?: string;
  message?: string;
  correlationId?: string;
  onRetry?: () => void;
}) {
  return (
    <StatePanel>
      <div style={{ color: 'var(--status-danger-fg)', fontWeight: 600 }}>{title}</div>
      {message && <div style={{ color: 'var(--text-muted)', fontSize: 'var(--text-sm)', maxWidth: 460 }}>{message}</div>}
      {correlationId && <CorrelationTag correlationId={correlationId} />}
      {onRetry && (
        <Button variant="secondary" onClick={onRetry}>
          Retry
        </Button>
      )}
    </StatePanel>
  );
}

/* ---------------- CorrelationTag (copyable) ---------------- */
export function CorrelationTag({ correlationId }: { correlationId: string }) {
  return (
    <button
      type="button"
      onClick={() => {
        void navigator.clipboard?.writeText(correlationId);
      }}
      title="Copy correlation ID"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--text-xs)',
        color: 'var(--text-muted)',
        background: 'var(--surface-sunken)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-sm)',
        padding: '4px 10px',
        cursor: 'pointer',
      }}
    >
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
        <rect x="9" y="9" width="11" height="11" rx="2" />
        <path d="M5 15V5a2 2 0 0 1 2-2h10" />
      </svg>
      {correlationId}
    </button>
  );
}

/* ---------------- InlineMessage (MessageBar equivalent) ---------------- */
export type InlineMessageVariant = 'success' | 'danger' | 'info';

const inlineMessageTone: Record<InlineMessageVariant, { bg: string; fg: string; role: 'status' | 'alert' }> = {
  success: { bg: 'var(--status-success-bg)', fg: 'var(--status-success-fg)', role: 'status' },
  danger: { bg: 'var(--status-danger-bg)', fg: 'var(--status-danger-fg)', role: 'alert' },
  info: { bg: 'var(--status-info-bg)', fg: 'var(--status-info-fg)', role: 'status' },
};

function InlineMessageIcon({ variant, loading }: { variant: InlineMessageVariant; loading: boolean }) {
  if (loading) return <Spinner size={14} />;
  if (variant === 'success') return <IconCheck size={16} />;
  if (variant === 'danger') return <IconAlertCircle size={16} />;
  return null;
}

// aria-live is polite for success/info (non-interruptive updates) and the role
// itself carries assertive semantics for danger via role="alert".
export function InlineMessage({
  variant,
  children,
  loading = false,
  correlationId,
}: {
  variant: InlineMessageVariant;
  children: ReactNode;
  loading?: boolean;
  correlationId?: string;
}) {
  const tone = inlineMessageTone[variant];
  return (
    <div
      role={tone.role}
      aria-live="polite"
      style={{
        display: 'flex',
        gap: 10,
        alignItems: 'flex-start',
        padding: '10px 14px',
        borderRadius: 'var(--radius-md)',
        fontSize: 'var(--text-sm)',
        lineHeight: 1.5,
        background: tone.bg,
        color: tone.fg,
        fontFamily: 'var(--font-body)',
      }}
    >
      <span style={{ flexShrink: 0, marginTop: 2, display: 'inline-flex' }}>
        <InlineMessageIcon variant={variant} loading={loading} />
      </span>
      <span style={{ flex: 1 }}>{children}</span>
      {correlationId && <CorrelationTag correlationId={correlationId} />}
    </div>
  );
}

/* ---------------- Skeleton ---------------- */
export function Skeleton({ height = 56, radius = 'var(--radius-md)', style }: { height?: number; radius?: string; style?: CSSProperties }) {
  return (
    <div
      style={{
        height,
        borderRadius: radius,
        background: 'var(--surface-sunken)',
        animation: 'cortexa-pulse 1.4s ease-in-out infinite',
        ...style,
      }}
    />
  );
}

/* ---------------- Modal ---------------- */
export function Modal({
  open,
  title,
  subtitle,
  children,
  footer,
  onClose,
  width = 480,
}: {
  open: boolean;
  title?: ReactNode;
  subtitle?: ReactNode;
  children?: ReactNode;
  footer?: ReactNode;
  onClose?: () => void;
  width?: number;
}) {
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose?.();
    };
    document.addEventListener('keydown', handler);
    return () => document.removeEventListener('keydown', handler);
  }, [open, onClose]);

  if (!open) return null;
  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      onClick={onClose}
      style={{
        position: 'fixed',
        inset: 0,
        background: 'var(--surface-overlay)',
        backdropFilter: 'blur(4px)',
        WebkitBackdropFilter: 'blur(4px)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1000,
        fontFamily: 'var(--font-body)',
        padding: 16,
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width,
          maxWidth: '92vw',
          maxHeight: '90vh',
          overflowY: 'auto',
          background: 'var(--surface-card-solid)',
          border: '1px solid var(--border-subtle)',
          borderRadius: 'var(--radius-xl)',
          boxShadow: 'var(--shadow-lg)',
          padding: 28,
        }}
      >
        {title && (
          <div style={{ fontFamily: 'var(--font-display)', fontSize: 'var(--text-2xl)', fontWeight: 500, color: 'var(--text-primary)' }}>
            {title}
          </div>
        )}
        {subtitle && <div style={{ marginTop: 8, fontSize: 'var(--text-sm)', color: 'var(--text-muted)' }}>{subtitle}</div>}
        {children && <div style={{ marginTop: 20, display: 'flex', flexDirection: 'column', gap: 16 }}>{children}</div>}
        {footer && <div style={{ marginTop: 20, display: 'flex', gap: 10 }}>{footer}</div>}
      </div>
    </div>,
    document.body
  );
}

/* ---------------- ScoreGauge (conic radial dial) ---------------- */
export function ScoreGauge({ pct, size = 108 }: { pct: number; size?: number }) {
  const clamped = Math.max(0, Math.min(100, pct));
  return (
    <div
      style={{
        width: size,
        height: size,
        borderRadius: '50%',
        background: `conic-gradient(from -90deg, #3E8BFF 0%, #6E7BFF ${clamped * 0.5}%, #A46BFF ${clamped}%, var(--gray-100) ${clamped}%)`,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        position: 'relative',
        animation: 'cortexa-glow 3.5s ease-in-out infinite',
      }}
    >
      <div
        style={{
          position: 'absolute',
          inset: 8,
          borderRadius: '50%',
          background: 'var(--surface-card-solid)',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, fontSize: 27, color: 'var(--text-primary)', lineHeight: 1.1 }}>
          {Math.round(clamped)}
        </span>
        <span style={{ fontSize: 9.5, fontWeight: 600, letterSpacing: '0.1em', color: 'var(--text-muted)' }}>/ 100</span>
      </div>
    </div>
  );
}
