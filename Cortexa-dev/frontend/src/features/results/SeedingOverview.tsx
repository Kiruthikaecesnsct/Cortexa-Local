import type { CSSProperties } from 'react';
import { SectionCard } from '../../shared/layout/PageHeader';
import { Badge } from '../../shared/ds';
import { CategoryBadge } from '../../shared/ds/badges';
import type { ConceptMapEntryDto } from '../../core/api/types';
import type { CategorizedOpportunity } from './resultsTypes';

type DensityLevel = 'sparse' | 'moderate' | 'dense' | 'crowded';

const DENSITY_LEVELS: DensityLevel[] = ['sparse', 'moderate', 'dense', 'crowded'];

function normalizeDensity(value: number | string | undefined): DensityLevel {
  if (typeof value === 'string') {
    const key = value.toLowerCase();
    const match = DENSITY_LEVELS.find((l) => l === key);
    if (match) return match;
    const parsed = Number(value);
    if (Number.isNaN(parsed)) return 'sparse';
    return bucketDensity(parsed);
  }
  return bucketDensity(value ?? 0);
}

function bucketDensity(n: number): DensityLevel {
  const x = n > 1 ? Math.min(1, n / 100) : Math.max(0, n);
  if (x < 0.25) return 'sparse';
  if (x < 0.5) return 'moderate';
  if (x < 0.75) return 'dense';
  return 'crowded';
}

function levelLabel(level: DensityLevel): string {
  return level.charAt(0).toUpperCase() + level.slice(1);
}

function DensityTrack({ label, value }: { label: 'Corpus' | 'Live'; value: number | string | undefined }) {
  const level = normalizeDensity(value);
  const filled = DENSITY_LEVELS.indexOf(level) + 1;
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }} aria-label={`${label} landscape: ${level}`}>
      <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', width: 48, flexShrink: 0 }}>{label}</span>
      <div style={{ display: 'flex', gap: 3, flex: 1 }} aria-hidden="true">
        {DENSITY_LEVELS.map((_, i) => (
          <span
            key={i}
            style={{
              flex: 1,
              height: 6,
              borderRadius: 'var(--radius-full)',
              background: i < filled ? 'var(--accent-primary)' : 'var(--gray-100)',
            }}
          />
        ))}
      </div>
      <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-body)', fontWeight: 600, width: 68, flexShrink: 0, textAlign: 'right' }}>
        {levelLabel(level)}
      </span>
    </div>
  );
}

const cardStyle: CSSProperties = {
  border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-md)',
  padding: 18,
  display: 'flex',
  flexDirection: 'column',
  gap: 12,
  background: 'var(--surface-card)',
};

function categoryMix(ids: string[], opportunityById: Map<string, CategorizedOpportunity>): string[] {
  const seen = new Set<string>();
  for (const id of ids) {
    const category = opportunityById.get(id)?.category;
    if (category) seen.add(category);
  }
  return [...seen];
}

function ConceptMapCard({ entry, opportunityById }: { entry: ConceptMapEntryDto; opportunityById: Map<string, CategorizedOpportunity> }) {
  const ideaCount = entry.opportunity_ids.length;
  const mix = categoryMix(entry.opportunity_ids, opportunityById);
  return (
    <div style={cardStyle}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <div style={{ fontSize: 'var(--text-sm)', fontWeight: 600, color: 'var(--text-primary)' }}>{entry.concept}</div>
        <Badge tone="neutral">{`${ideaCount} idea${ideaCount === 1 ? '' : 's'}`}</Badge>
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <DensityTrack label="Corpus" value={entry.corpus_axis} />
        <DensityTrack label="Live" value={entry.live_axis} />
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
        {entry.whitespace && <Badge tone="success">Whitespace</Badge>}
        {mix.length > 1 && mix.map((c) => <CategoryBadge key={c} category={c} />)}
      </div>
    </div>
  );
}

interface SeedingOverviewProps {
  conceptMap: ConceptMapEntryDto[];
  opportunities: CategorizedOpportunity[];
}

export function SeedingOverview({ conceptMap, opportunities }: SeedingOverviewProps) {
  if (conceptMap.length === 0) return null;
  const opportunityById = new Map(opportunities.map((o) => [o.id, o]));
  return (
    <SectionCard>
      <div style={{ fontSize: 12, fontWeight: 600, letterSpacing: '0.02em', textTransform: 'uppercase', color: 'var(--text-muted)', marginBottom: 6 }}>
        Opportunity map
      </div>
      <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 16 }}>
        Where these ideas sit on the prior-art landscape — crowded areas are well-patented, whitespace is open.
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: 'var(--space-5)' }}>
        {conceptMap.map((entry, i) => (
          <ConceptMapCard key={`${entry.concept}-${i}`} entry={entry} opportunityById={opportunityById} />
        ))}
      </div>
    </SectionCard>
  );
}
