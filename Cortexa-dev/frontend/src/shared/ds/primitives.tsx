/* ============================================================
   Cortexa Design System — core primitives
   Token-driven, inline-styled to mirror the v2 handoff exactly.
   One styling system for the whole app (no Fluent, no CSS modules).
   ============================================================ */
import {
  type CSSProperties,
  type ReactNode,
  useState,
  forwardRef,
  type InputHTMLAttributes,
  type SelectHTMLAttributes,
} from 'react';

/* ---------------- Badge ---------------- */
export type BadgeTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand';

const badgeTone: Record<BadgeTone, { bg: string; fg: string }> = {
  neutral: { bg: 'var(--gray-100)', fg: 'var(--text-body)' },
  success: { bg: 'var(--status-success-bg)', fg: 'var(--status-success-fg)' },
  warning: { bg: 'var(--status-warning-bg)', fg: 'var(--status-warning-fg)' },
  danger: { bg: 'var(--status-danger-bg)', fg: 'var(--status-danger-fg)' },
  info: { bg: 'var(--status-info-bg)', fg: 'var(--status-info-fg)' },
  brand: { bg: 'var(--accent-primary)', fg: '#fff' },
};

export function Badge({
  children,
  tone = 'neutral',
  icon = null,
}: {
  children: ReactNode;
  tone?: BadgeTone;
  icon?: ReactNode;
}) {
  const t = badgeTone[tone] ?? badgeTone.neutral;
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        padding: '4px 12px',
        borderRadius: 'var(--radius-full)',
        background: t.bg,
        color: t.fg,
        fontFamily: 'var(--font-body)',
        fontWeight: 600,
        fontSize: 'var(--text-xs)',
        whiteSpace: 'nowrap',
      }}
    >
      {icon}
      {children}
    </span>
  );
}

/* ---------------- Button ---------------- */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

const btnSize: Record<ButtonSize, CSSProperties> = {
  sm: { padding: '8px 14px', fontSize: 'var(--text-sm)' },
  md: { padding: '10px 16px', fontSize: 'var(--text-base)' },
  lg: { padding: '13px 20px', fontSize: 'var(--text-md)' },
};

function baseVariant(variant: ButtonVariant, disabled: boolean): CSSProperties {
  if (disabled) {
    return { background: 'var(--gray-100)', color: 'var(--text-muted)', border: '1px solid var(--border-subtle)' };
  }
  switch (variant) {
    case 'secondary':
      return { background: 'var(--surface-card)', color: 'var(--accent-primary)', border: '1px solid var(--accent-primary)' };
    case 'ghost':
      return { background: 'transparent', color: 'var(--text-body)', border: '1px solid transparent' };
    case 'danger':
      return { background: 'var(--status-danger-fg)', color: '#fff', border: '1px solid transparent' };
    default:
      return { background: 'var(--accent-primary)', color: 'var(--text-on-brand)', border: '1px solid transparent' };
  }
}

export const Button = forwardRef<
  HTMLButtonElement,
  {
    children: ReactNode;
    variant?: ButtonVariant;
    size?: ButtonSize;
    disabled?: boolean;
    icon?: ReactNode;
    fullWidth?: boolean;
    onClick?: () => void;
    type?: 'button' | 'submit';
    title?: string;
    ariaDisabled?: boolean;
    describedById?: string;
    'data-testid'?: string;
  }
>(function Button(
  {
    children,
    variant = 'primary',
    size = 'md',
    disabled = false,
    icon = null,
    fullWidth = false,
    onClick,
    type = 'button',
    title,
    ariaDisabled = false,
    describedById,
    'data-testid': dataTestId,
  },
  ref
) {
  const [hover, setHover] = useState(false);
  const [active, setActive] = useState(false);
  const actualDisabled = disabled || ariaDisabled;
  const base = baseVariant(variant, actualDisabled);
  let bg = base.background as string;
  if (!actualDisabled && variant === 'primary') {
    if (active) bg = 'var(--accent-primary-active)';
    else if (hover) bg = 'var(--accent-primary-hover)';
  } else if (!actualDisabled && (variant === 'secondary' || variant === 'ghost') && hover) {
    bg = 'var(--accent-primary-subtle)';
  } else if (!actualDisabled && variant === 'danger' && hover) {
    bg = 'var(--status-danger-fg)';
  }

  const handleClick = () => {
    if (ariaDisabled) return;
    onClick?.();
  };

  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled}
      aria-disabled={ariaDisabled || undefined}
      aria-describedby={describedById}
      title={title}
      onClick={handleClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => {
        setHover(false);
        setActive(false);
      }}
      onMouseDown={() => setActive(true)}
      data-testid={dataTestId}
      onMouseUp={() => setActive(false)}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 8,
        width: fullWidth ? '100%' : 'auto',
        borderRadius: 'var(--radius-md)',
        fontFamily: 'var(--font-body)',
        fontWeight: 500,
        letterSpacing: '-0.3px',
        cursor: actualDisabled ? 'not-allowed' : 'pointer',
        transition: 'background var(--duration-fast) var(--ease-standard), transform var(--duration-fast) var(--ease-standard)',
        transform: active && !actualDisabled ? 'scale(0.98)' : 'scale(1)',
        filter: hover && !actualDisabled && variant === 'danger' ? 'brightness(1.08)' : undefined,
        ...btnSize[size],
        ...base,
        background: bg,
      }}
    >
      {icon}
      {children}
    </button>
  );
});

/* ---------------- Card ---------------- */
export function Card({
  children,
  padding = 24,
  style = {},
  onClick,
  interactive = false,
}: {
  children: ReactNode;
  padding?: number | string;
  style?: CSSProperties;
  onClick?: () => void;
  interactive?: boolean;
}) {
  const [hover, setHover] = useState(false);
  return (
    <div
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        background: 'var(--surface-card)',
        backdropFilter: 'blur(16px)',
        WebkitBackdropFilter: 'blur(16px)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-xs)',
        padding,
        fontFamily: 'var(--font-body)',
        cursor: interactive ? 'pointer' : undefined,
        transition: 'transform var(--duration-base) var(--ease-standard), box-shadow var(--duration-base) var(--ease-standard), border-color var(--duration-base) var(--ease-standard)',
        transform: interactive && hover ? 'translateY(-2px)' : undefined,
        borderColor: interactive && hover ? 'var(--border-strong)' : undefined,
        ...(interactive && hover ? { boxShadow: 'var(--shadow-hover)' } : {}),
        ...style,
      }}
    >
      {children}
    </div>
  );
}

/* ---------------- Input ---------------- */
interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'onChange'> {
  label?: string;
  error?: string;
  trailing?: ReactNode;
  leading?: ReactNode;
  onChange?: (e: React.ChangeEvent<HTMLInputElement>) => void;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { label, error, trailing, leading, disabled, ...rest },
  ref
) {
  const [focused, setFocused] = useState(false);
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontFamily: 'var(--font-body)' }}>
      {label && (
        <label style={{ fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' }}>{label}</label>
      )}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          padding: '10px 14px',
          borderRadius: 'var(--radius-md)',
          background: disabled ? 'var(--gray-75)' : 'var(--surface-card)',
          border: `1px solid ${error ? 'var(--status-danger-fg)' : focused ? 'var(--border-focus)' : 'var(--border-subtle)'}`,
          boxShadow: focused ? 'var(--focus-ring)' : 'none',
          transition: 'box-shadow var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard)',
        }}
      >
        {leading}
        <input
          ref={ref}
          disabled={disabled}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          style={{
            flex: 1,
            border: 'none',
            outline: 'none',
            background: 'transparent',
            fontFamily: 'var(--font-body)',
            fontSize: 'var(--text-base)',
            color: 'var(--text-primary)',
            minWidth: 0,
          }}
          {...rest}
        />
        {trailing}
      </div>
      {error && <span style={{ fontSize: 'var(--text-xs)', color: 'var(--status-danger-fg)' }}>{error}</span>}
    </div>
  );
});

/* ---------------- Select ---------------- */
export interface SelectOption {
  value: string;
  label: string;
}

interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'onChange'> {
  label?: string;
  options: (SelectOption | string)[];
  placeholder?: string;
  onChange?: (e: React.ChangeEvent<HTMLSelectElement>) => void;
}

export function Select({ label, options, placeholder = 'Select', value, disabled, onChange, ...rest }: SelectProps) {
  const [focused, setFocused] = useState(false);
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontFamily: 'var(--font-body)' }}>
      {label && (
        <label style={{ fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' }}>{label}</label>
      )}
      <div style={{ position: 'relative' }}>
        <select
          value={value}
          disabled={disabled}
          onChange={onChange}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          style={{
            width: '100%',
            appearance: 'none',
            padding: '10px 36px 10px 14px',
            borderRadius: 'var(--radius-md)',
            background: disabled ? 'var(--gray-75)' : 'var(--surface-card)',
            border: `1px solid ${focused ? 'var(--border-focus)' : 'var(--border-subtle)'}`,
            boxShadow: focused ? 'var(--focus-ring)' : 'none',
            fontFamily: 'var(--font-body)',
            fontSize: 'var(--text-base)',
            color: value ? 'var(--text-primary)' : 'var(--text-muted)',
            transition: 'box-shadow var(--duration-fast) var(--ease-standard)',
            cursor: disabled ? 'not-allowed' : 'pointer',
          }}
          {...rest}
        >
          {placeholder && (
            <option value="" disabled hidden>
              {placeholder}
            </option>
          )}
          {options.map((opt) => {
            const v = typeof opt === 'string' ? opt : opt.value;
            const l = typeof opt === 'string' ? opt : opt.label;
            return (
              <option key={v} value={v}>
                {l}
              </option>
            );
          })}
        </select>
        <span
          style={{
            position: 'absolute',
            right: 14,
            top: '50%',
            transform: 'translateY(-50%)',
            pointerEvents: 'none',
            color: 'var(--text-muted)',
            fontSize: 12,
          }}
        >
          ▾
        </span>
      </div>
    </div>
  );
}

/* ---------------- Checkbox ---------------- */
export function Checkbox({
  label,
  checked,
  onChange,
  disabled = false,
}: {
  label?: ReactNode;
  checked: boolean;
  onChange?: (next: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <label
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 10,
        fontFamily: 'var(--font-body)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.6 : 1,
      }}
    >
      <span
        onClick={() => !disabled && onChange?.(!checked)}
        style={{
          width: 18,
          height: 18,
          borderRadius: 5,
          border: `1.5px solid ${checked ? 'var(--accent-primary)' : 'var(--border-strong)'}`,
          background: checked ? 'var(--accent-primary)' : 'var(--surface-card)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          transition: 'all var(--duration-fast) var(--ease-standard)',
          flexShrink: 0,
        }}
      >
        {checked && (
          <svg width="11" height="9" viewBox="0 0 11 9" fill="none">
            <path d="M1 4.5L4 7.5L10 1" stroke="white" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        )}
      </span>
      {label && <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-body)' }}>{label}</span>}
    </label>
  );
}

/* ---------------- Switch ---------------- */
export function Switch({
  checked,
  onChange,
  disabled = false,
  label,
  ariaLabel,
}: {
  checked: boolean;
  onChange?: (next: boolean) => void;
  disabled?: boolean;
  label?: ReactNode;
  ariaLabel?: string;
}) {
  return (
    <label
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 10,
        fontFamily: 'var(--font-body)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.6 : 1,
      }}
    >
      <span
        role="switch"
        aria-checked={checked}
        aria-label={label ? undefined : ariaLabel}
        aria-disabled={disabled || undefined}
        tabIndex={disabled ? -1 : 0}
        onClick={() => !disabled && onChange?.(!checked)}
        onKeyDown={(e) => {
          if (disabled) return;
          if (e.key === ' ' || e.key === 'Enter') {
            e.preventDefault();
            onChange?.(!checked);
          }
        }}
        style={{
          width: 40,
          height: 22,
          borderRadius: 'var(--radius-full)',
          background: checked ? 'var(--accent-primary)' : 'var(--gray-300)',
          position: 'relative',
          transition: 'background var(--duration-base) var(--ease-standard)',
          flexShrink: 0,
        }}
      >
        <span
          style={{
            position: 'absolute',
            top: 2,
            left: checked ? 20 : 2,
            width: 18,
            height: 18,
            borderRadius: '50%',
            background: '#fff',
            boxShadow: 'var(--shadow-xs)',
            transition: 'left var(--duration-base) var(--ease-out)',
          }}
        />
      </span>
      {label && <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-body)' }}>{label}</span>}
    </label>
  );
}

/* ---------------- Tabs ---------------- */
export function Tabs({
  tabs,
  active,
  onChange,
  fullWidth = false,
}: {
  tabs: string[];
  active: string;
  onChange?: (tab: string) => void;
  fullWidth?: boolean;
}) {
  return (
    <div
      role="tablist"
      style={{
        display: fullWidth ? 'flex' : 'inline-flex',
        gap: 4,
        padding: 4,
        background: 'var(--gray-75)',
        borderRadius: 'var(--radius-md)',
        fontFamily: 'var(--font-body)',
      }}
    >
      {tabs.map((tab) => {
        const isActive = tab === active;
        return (
          <button
            key={tab}
            role="tab"
            aria-selected={isActive}
            onClick={() => onChange?.(tab)}
            style={{
              flex: fullWidth ? 1 : undefined,
              border: 'none',
              cursor: 'pointer',
              padding: '8px 16px',
              borderRadius: 'var(--radius-sm)',
              fontFamily: 'var(--font-body)',
              fontWeight: 600,
              fontSize: 'var(--text-sm)',
              background: isActive ? 'var(--accent-primary)' : 'transparent',
              color: isActive ? '#fff' : 'var(--text-body)',
              transition: 'all var(--duration-fast) var(--ease-standard)',
            }}
          >
            {tab}
          </button>
        );
      })}
    </div>
  );
}

/* ---------------- ProgressBar ---------------- */
export function ProgressBar({ pct, height = 6, ariaLabel }: { pct: number; height?: number; ariaLabel?: string }) {
  const clamped = Math.max(0, Math.min(100, pct));
  const a11yProps = ariaLabel
    ? { role: 'progressbar' as const, 'aria-valuenow': clamped, 'aria-valuemin': 0, 'aria-valuemax': 100, 'aria-label': ariaLabel }
    : {};
  return (
    <div style={{ height, borderRadius: 'var(--radius-full)', background: 'var(--gray-100)', overflow: 'hidden' }} {...a11yProps}>
      <div
        style={{
          width: `${clamped}%`,
          height: '100%',
          background: 'var(--accent-grad)',
          borderRadius: 'var(--radius-full)',
          transition: 'width var(--duration-slow) var(--ease-out)',
        }}
      />
    </div>
  );
}
