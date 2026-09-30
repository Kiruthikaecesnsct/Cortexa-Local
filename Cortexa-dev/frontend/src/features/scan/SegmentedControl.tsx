export interface SegmentOption<T extends string> {
  value: T;
  label: string;
}

interface SegmentedControlProps<T extends string> {
  label: string;
  options: SegmentOption<T>[];
  value: T;
  onChange: (value: T) => void;
}

export function SegmentedControl<T extends string>({ label, options, value, onChange }: SegmentedControlProps<T>) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      style={{
        display: 'inline-flex',
        padding: 3,
        gap: 2,
        borderRadius: 'var(--radius-md)',
        background: 'var(--surface-sunken)',
        border: '1px solid var(--border-subtle)',
      }}
    >
      {options.map((option) => {
        const selected = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={selected}
            onClick={() => onChange(option.value)}
            style={{
              border: 'none',
              cursor: 'pointer',
              padding: '6px 12px',
              borderRadius: 'var(--radius-sm)',
              fontFamily: 'var(--font-body)',
              fontSize: 'var(--text-sm)',
              fontWeight: 500,
              background: selected ? 'var(--surface-card)' : 'transparent',
              color: selected ? 'var(--text-primary)' : 'var(--text-muted)',
              boxShadow: selected ? 'var(--shadow-xs)' : 'none',
            }}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
