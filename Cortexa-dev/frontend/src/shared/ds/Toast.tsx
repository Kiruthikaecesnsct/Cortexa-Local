import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

export type ToastVariant = 'success' | 'info' | 'warning' | 'error';

export interface ToastItem {
  id: string;
  variant: ToastVariant;
  title: string;
  message?: string;
  action?: { label: string; onClick: () => void };
  duration?: number;
}

export interface ToastContextValue {
  show: (options: Omit<ToastItem, 'id'>) => void;
  dismiss: (id: string) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used within ToastProvider');
  return ctx;
}

const dotColor: Record<ToastVariant, string> = {
  success: 'var(--status-success-fg)',
  info: 'var(--accent-primary)',
  warning: 'var(--status-warning-fg)',
  error: 'var(--status-danger-fg)',
};

let seq = 0;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const timers = useRef(new Map<string, number>());

  const dismiss = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
    const timer = timers.current.get(id);
    if (timer) {
      window.clearTimeout(timer);
      timers.current.delete(id);
    }
  }, []);

  const show = useCallback(
    (options: Omit<ToastItem, 'id'>) => {
      seq += 1;
      const id = `t_${seq}`;
      const item: ToastItem = { id, ...options };
      setToasts((prev) => [...prev, item]);
      const duration = options.duration ?? 4500;
      if (duration > 0) {
        const timer = window.setTimeout(() => dismiss(id), duration);
        timers.current.set(id, timer);
      }
    },
    [dismiss]
  );

  return (
    <ToastContext.Provider value={{ show, dismiss }}>
      {children}
      {createPortal(
        <div
          aria-live="polite"
          style={{
            position: 'fixed',
            top: 20,
            right: 24,
            zIndex: 2000,
            display: 'flex',
            flexDirection: 'column',
            gap: 8,
            width: 340,
            maxWidth: 'calc(100vw - 48px)',
          }}
        >
          {toasts.map((t) => (
            <div
              key={t.id}
              role="status"
              style={{
                display: 'flex',
                alignItems: 'flex-start',
                gap: 12,
                padding: '12px 16px',
                borderRadius: 'var(--radius-md)',
                background: 'var(--surface-card-solid)',
                boxShadow: 'var(--shadow-md)',
                border: '1px solid var(--border-subtle)',
                fontFamily: 'var(--font-body)',
                fontSize: 'var(--text-sm)',
                color: 'var(--text-primary)',
              }}
            >
              <span
                style={{
                  width: 8,
                  height: 8,
                  borderRadius: '50%',
                  background: dotColor[t.variant],
                  flexShrink: 0,
                  marginTop: 5,
                }}
              />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontWeight: 600 }}>{t.title}</div>
                {t.message && <div style={{ color: 'var(--text-muted)', marginTop: 2 }}>{t.message}</div>}
                {t.action && (
                  <button
                    type="button"
                    onClick={() => {
                      t.action?.onClick();
                      dismiss(t.id);
                    }}
                    style={{
                      marginTop: 6,
                      background: 'none',
                      border: 'none',
                      padding: 0,
                      color: 'var(--accent-primary)',
                      fontWeight: 600,
                      fontSize: 'var(--text-sm)',
                      cursor: 'pointer',
                    }}
                  >
                    {t.action.label}
                  </button>
                )}
              </div>
              <button
                type="button"
                onClick={() => dismiss(t.id)}
                aria-label="Dismiss"
                style={{ cursor: 'pointer', color: 'var(--text-muted)', background: 'none', border: 'none', padding: 0 }}
              >
                ✕
              </button>
            </div>
          ))}
        </div>,
        document.body
      )}
    </ToastContext.Provider>
  );
}
