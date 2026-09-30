import { Fragment } from 'react';
import { IconCheck } from '../../shared/ds';
import { STEP_TITLES } from './scanPrompts';
import { WIZARD_STEPS, canVisit, type WizardState, type WizardStep } from './scanWizard';

type StepStatus = 'done' | 'current' | 'upcoming';

function statusOf(state: WizardState, step: WizardStep): StepStatus {
  const current = WIZARD_STEPS.indexOf(state.step);
  const index = WIZARD_STEPS.indexOf(step);
  if (index === current) return 'current';
  return index < current || canVisit(state, step) ? 'done' : 'upcoming';
}

function detailOf(state: WizardState, step: WizardStep): string {
  if (step === 'connect') return state.connection.status === 'loaded' ? state.connection.data.owner : 'Organization & token';
  if (step === 'repository') return state.repository?.name ?? 'Pick one';
  if (step === 'branch') return state.branch ?? 'Pick one';
  return state.tree.status === 'loaded' ? `${state.tree.data.tree.entries.length} items` : 'Browse';
}

const CIRCLE_STYLE: Record<StepStatus, React.CSSProperties> = {
  done: { background: 'var(--accent-primary)', color: '#fff', border: '1px solid var(--accent-primary)' },
  current: { background: 'var(--surface-card)', color: 'var(--accent-primary)', border: '2px solid var(--accent-primary)' },
  upcoming: { background: 'var(--surface-sunken)', color: 'var(--text-muted)', border: '1px solid var(--border-subtle)' },
};

function StepItem({ state, step, onSelect }: { state: WizardState; step: WizardStep; onSelect: (step: WizardStep) => void }) {
  const status = statusOf(state, step);
  const clickable = status !== 'current' && canVisit(state, step);
  return (
    <button
      type="button"
      onClick={() => onSelect(step)}
      disabled={!clickable}
      aria-current={status === 'current' ? 'step' : undefined}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 10,
        border: 'none',
        background: 'transparent',
        padding: 0,
        cursor: clickable ? 'pointer' : 'default',
        textAlign: 'left',
        fontFamily: 'var(--font-body)',
        minWidth: 0,
      }}
    >
      <span
        style={{
          width: 30,
          height: 30,
          borderRadius: '50%',
          flexShrink: 0,
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontWeight: 600,
          fontSize: 13,
          ...CIRCLE_STYLE[status],
        }}
      >
        {status === 'done' ? <IconCheck size={15} /> : WIZARD_STEPS.indexOf(step) + 1}
      </span>
      <span style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <span style={{ fontWeight: 600, fontSize: 'var(--text-sm)', color: status === 'upcoming' ? 'var(--text-muted)' : 'var(--text-primary)' }}>
          {STEP_TITLES[step]}
        </span>
        <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: 160 }}>
          {detailOf(state, step)}
        </span>
      </span>
    </button>
  );
}

export function ScanStepper({ state, onSelect }: { state: WizardState; onSelect: (step: WizardStep) => void }) {
  return (
    <nav aria-label="Scan progress" style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
      {WIZARD_STEPS.map((step, i) => (
        <Fragment key={step}>
          {i > 0 && (
            <span
              aria-hidden="true"
              style={{
                flex: 1,
                height: 2,
                minWidth: 16,
                borderRadius: 1,
                background: statusOf(state, step) === 'upcoming' ? 'var(--border-subtle)' : 'var(--accent-primary)',
              }}
            />
          )}
          <StepItem state={state} step={step} onSelect={onSelect} />
        </Fragment>
      ))}
    </nav>
  );
}
