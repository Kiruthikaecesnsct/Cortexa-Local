import type { ReactNode } from 'react';

// Lays form sections side by side on wide screens and stacks them on narrow ones.
export const FORM_SECTION_GRID_STYLE = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fit, minmax(340px, 1fr))',
  gap: 16,
  alignItems: 'stretch',
} as const;

export function FieldIcon({ children }: { children: ReactNode }) {
  return <span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}>{children}</span>;
}

export function FieldHint({ children }: { children: ReactNode }) {
  return <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', lineHeight: 1.5 }}>{children}</span>;
}

interface FormSectionProps {
  step: number;
  title: string;
  hint: string;
  children: ReactNode;
}

function StepBadge({ step }: { step: number }) {
  return (
    <span
      aria-hidden="true"
      style={{
        width: 26,
        height: 26,
        flexShrink: 0,
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: '50%',
        fontSize: 'var(--text-xs)',
        fontWeight: 600,
        color: 'var(--accent-primary)',
        background: 'var(--accent-primary-subtle)',
      }}
    >
      {step}
    </span>
  );
}

export function FormSection({ step, title, hint, children }: FormSectionProps) {
  return (
    <fieldset
      style={{
        margin: 0,
        minWidth: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: 16,
        padding: 20,
        fontFamily: 'var(--font-body)',
        background: 'var(--surface-card)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-xs)',
      }}
    >
      <legend style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>{title}</legend>
      <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
        <StepBadge step={step} />
        <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <span style={{ fontWeight: 600, fontSize: 'var(--text-md)', color: 'var(--text-primary)' }}>{title}</span>
          <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)' }}>{hint}</span>
        </div>
      </div>
      {children}
    </fieldset>
  );
}
