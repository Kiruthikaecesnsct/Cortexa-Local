/* APG tabs pattern (https://www.w3.org/WAI/ARIA/apg/patterns/tabs/) — roving
   tabindex, arrow/Home/End navigation, one tab focusable at a time. Generic
   enough for any tabbed hub; not settings-specific despite the name, which
   only reflects where it was first needed. */
import { type KeyboardEvent, type ReactNode, useRef } from 'react';
import { Badge } from './primitives';

export interface SettingsTabDef {
  id: string;
  label: string;
  icon?: ReactNode;
  status?: 'available' | 'future';
}

const tablistStyle: React.CSSProperties = {
  display: 'flex',
  gap: 4,
  borderBottom: '1px solid var(--border-subtle)',
  fontFamily: 'var(--font-body)',
};

function tabButtonStyle(selected: boolean): React.CSSProperties {
  return {
    position: 'relative',
    border: 'none',
    background: 'transparent',
    cursor: 'pointer',
    padding: '12px 18px',
    fontFamily: 'var(--font-body)',
    fontWeight: 600,
    fontSize: 'var(--text-base)',
    color: selected ? 'var(--accent-primary)' : 'var(--text-muted)',
    display: 'inline-flex',
    alignItems: 'center',
    gap: 8,
    borderRadius: 'var(--radius-sm) var(--radius-sm) 0 0',
    borderBottom: selected ? '2.5px solid var(--accent-primary)' : '2.5px solid transparent',
    marginBottom: -1,
    minHeight: 44,
    transition: 'color var(--duration-fast) var(--ease-standard)',
  };
}

const futureTabStyle: React.CSSProperties = {
  color: 'var(--text-muted)',
  opacity: 0.5,
  cursor: 'default',
  padding: '12px 18px',
  display: 'inline-flex',
  alignItems: 'center',
  gap: 8,
  fontSize: 'var(--text-base)',
  fontWeight: 600,
};

function nextIndex(key: string, current: number, count: number): number | null {
  if (key === 'ArrowRight') return (current + 1) % count;
  if (key === 'ArrowLeft') return (current - 1 + count) % count;
  if (key === 'Home') return 0;
  if (key === 'End') return count - 1;
  return null;
}

export function SettingsTabList({
  tabs,
  activeId,
  onChange,
  ariaLabel = 'Settings sections',
}: {
  tabs: SettingsTabDef[];
  activeId: string;
  onChange: (id: string) => void;
  ariaLabel?: string;
}) {
  const availableTabs = tabs.filter((t) => t.status !== 'future');
  const futureTabs = tabs.filter((t) => t.status === 'future');
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});

  function focusAndSelect(id: string) {
    onChange(id);
    tabRefs.current[id]?.focus();
  }

  function handleKeyDown(e: KeyboardEvent<HTMLButtonElement>, index: number) {
    const target = nextIndex(e.key, index, availableTabs.length);
    if (target === null) return;
    e.preventDefault();
    focusAndSelect(availableTabs[target]!.id);
  }

  return (
    <div role="tablist" aria-label={ariaLabel} style={tablistStyle}>
      {availableTabs.map((tab, index) => {
        const selected = tab.id === activeId;
        return (
          <button
            key={tab.id}
            ref={(el) => {
              tabRefs.current[tab.id] = el;
            }}
            role="tab"
            id={`tab-${tab.id}`}
            type="button"
            aria-selected={selected}
            aria-controls={`panel-${tab.id}`}
            tabIndex={selected ? 0 : -1}
            onClick={() => onChange(tab.id)}
            onKeyDown={(e) => handleKeyDown(e, index)}
            style={tabButtonStyle(selected)}
          >
            {tab.icon}
            {tab.label}
          </button>
        );
      })}
      {futureTabs.map((tab) => (
        <span key={tab.id} role="presentation" aria-hidden="true" style={futureTabStyle}>
          {tab.icon}
          {tab.label}
          <Badge tone="neutral">future</Badge>
        </span>
      ))}
    </div>
  );
}
