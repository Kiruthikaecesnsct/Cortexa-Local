/* Domain-specific badges built on the base Badge primitive.
   Centralizes the status→tone and label mappings used across pages. */
import { Badge, type BadgeTone } from './primitives';
import { IconCheck, IconAlertTriangle } from './icons';
import type { BatchStatus } from '../../features/jobs/jobTypes';
import type { Role } from '../../core/auth/authTypes';

/* ---- Batch / document status ---- */
const STATUS_TONE: Record<string, BadgeTone> = {
  Completed: 'success',
  Running: 'info',
  Failed: 'danger',
  PartiallyFailed: 'warning',
  Cancelled: 'neutral',
  NoCandidates: 'warning',
  Created: 'neutral',
};

export function statusTone(status: string): BadgeTone {
  return STATUS_TONE[status] ?? 'neutral';
}

export function BatchStatusBadge({ status }: { status: BatchStatus | string }) {
  return <Badge tone={statusTone(status)}>{status}</Badge>;
}

export function DocumentStatusBadge({ status }: { status: string }) {
  const tone: BadgeTone =
    status === 'Failed' ? 'danger' : status === 'Completed' || status === 'EngineDone' || status === 'Scored' ? 'success' : status === 'Cancelled' || status === 'NoCandidates' ? 'neutral' : 'info';
  return <Badge tone={tone}>{status}</Badge>;
}

/* ---- Maturity ---- */
export function MaturityBadge({ maturity }: { maturity: string }) {
  const key = maturity.toLowerCase();
  const tone: BadgeTone = key === 'mature' || key === 'strong' ? 'success' : key === 'emerging' ? 'warning' : 'neutral';
  const label = maturity.charAt(0).toUpperCase() + maturity.slice(1);
  return <Badge tone={tone}>{label}</Badge>;
}

/* ---- Category (seeding opportunity classification) ---- */
const CATEGORY_TONE: Record<string, BadgeTone> = {
  whitespace: 'success',
  adjacent: 'brand',
  defensive: 'info',
  continuation: 'neutral',
};

export function CategoryBadge({ category }: { category?: string | null }) {
  if (!category) return null;
  const key = category.toLowerCase();
  const tone = CATEGORY_TONE[key];
  if (!tone) return null;
  const label = category.charAt(0).toUpperCase() + category.slice(1);
  return <Badge tone={tone}>{label}</Badge>;
}

/* ---- Recommendation ---- */
const REC_TONE: Record<string, BadgeTone> = {
  pursue: 'success',
  Pursue: 'success',
  investigate: 'warning',
  Investigate: 'warning',
  review: 'warning',
  Review: 'warning',
  abandon: 'danger',
  Abandon: 'danger',
  reject: 'danger',
  Reject: 'danger',
};

export function recommendationLabel(rec: string): string {
  return rec.charAt(0).toUpperCase() + rec.slice(1);
}

export function RecommendationBadge({ recommendation }: { recommendation: string }) {
  return <Badge tone={REC_TONE[recommendation] ?? 'neutral'}>{recommendationLabel(recommendation)}</Badge>;
}

/* ---- Role ---- */
const ROLE_TONE: Record<Role, BadgeTone> = {
  Researcher: 'info',
  Reviewer: 'brand',
  Admin: 'warning',
  SuperAdmin: 'danger',
};

export function RoleBadge({ role }: { role: Role }) {
  return <Badge tone={ROLE_TONE[role] ?? 'neutral'}>{role}</Badge>;
}

/* ---- Seeding provenance ---- */
export function SeedingModeBadge({ mode }: { mode: 'legacy' | 'deep' }) {
  const tone: BadgeTone = mode === 'deep' ? 'brand' : 'neutral';
  const label = mode === 'deep' ? 'Deep seeding' : 'Legacy seeding';
  return <Badge tone={tone}>{label}</Badge>;
}

/* ---- Patent source credential status (color-independent: icon + label, never color alone) ---- */
export function PatentStatusBadge({
  status,
  setLabel = 'Key set',
  notSetLabel = 'No key',
}: {
  status: 'set' | 'not_set';
  setLabel?: string;
  notSetLabel?: string;
}) {
  const isSet = status === 'set';
  const tone: BadgeTone = isSet ? 'success' : 'warning';
  const icon = isSet ? <IconCheck size={13} /> : <IconAlertTriangle size={13} />;
  return (
    <Badge tone={tone} icon={icon}>
      {isSet ? setLabel : notSetLabel}
    </Badge>
  );
}

/* ---- Evidence availability ---- */
export type AvailabilityState = 'available' | 'active_uncited' | 'unavailable';

export function AvailabilityBadge({
  state,
  sourceLabel
}: {
  state: AvailabilityState;
  sourceLabel?: string;
}) {
  const tone = state === 'unavailable' ? 'neutral' : 'success';
  const label = state === 'unavailable' ? 'Unavailable' : 'Available';
  const ariaLabel = sourceLabel
    ? `${sourceLabel}: ${state === 'unavailable' ? 'unavailable' : state === 'active_uncited' ? 'available, contributed but not cited' : 'available and cited'}`
    : label;

  const t = tone === 'neutral' ? { bg: 'var(--gray-100)', fg: 'var(--text-body)' } : { bg: 'var(--status-success-bg)', fg: 'var(--status-success-fg)' };
  return (
    <span
      aria-label={ariaLabel}
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
      {label}
    </span>
  );
}
