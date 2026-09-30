import { type ReactNode } from 'react';
import type { StepPromptCopy } from './scanPrompts';

export function StepPrompt({ copy, actions }: { copy: StepPromptCopy; actions?: ReactNode }) {
  return (
    <div
      role="status"
      aria-live="polite"
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 16,
        padding: '14px 18px',
        borderRadius: 'var(--radius-md)',
        background: 'var(--status-info-bg)',
        borderLeft: '4px solid var(--accent-primary)',
      }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={{ fontSize: 11, fontWeight: 600, letterSpacing: '0.06em', textTransform: 'uppercase', color: 'var(--accent-primary)' }}>
          {copy.stepLabel} · What's next
        </span>
        <span style={{ fontWeight: 600, fontSize: 'var(--text-md)', color: 'var(--text-primary)' }}>{copy.title}</span>
        <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-body)', lineHeight: 1.5 }}>{copy.message}</span>
      </div>
      {actions && <div style={{ display: 'flex', gap: 8, flexShrink: 0 }}>{actions}</div>}
    </div>
  );
}
