import { forwardRef, lazy, Suspense, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { Badge, Button, Modal, ProgressBar, Skeleton } from '../../shared/ds';
import type { AxisScore, EvidenceHit, EvidenceSource, EvidenceSourceType, EvidenceState, Provenance, ResolvedCitation } from './opportunityTypes';
import { useToast } from '../../shared/ds/Toast';
import { joinReasonsWithAnd } from './opportunityMappers';
import { resolveExcerptView } from './provenanceHelpers';

const LazyPdfProvenanceViewer = lazy(() => import('./PdfProvenanceViewer'));
const LazyCodeProvenanceViewer = lazy(() => import('./CodeProvenanceViewer'));

const EVIDENCE_LABEL: Record<string, string> = {
  patent_api: 'Live Patent API',
  vector_corpus: 'Corpus Vector Search',
  corpus: 'Corpus Vector Search',
  llm_deep_research: 'LLM Deep Research',
  llm_research: 'LLM Deep Research',
};

const SOURCE_KIND_COLOR: Record<string, string> = {
  patent_api: '#3e8bff',
  vector_corpus: '#a46bff',
  llm_deep_research: '#0e9f6e',
};

function getSourceKindDot(sourceType?: string): ReactNode {
  const color = sourceType ? SOURCE_KIND_COLOR[sourceType] : 'var(--gray-300)';
  return <span style={{ width: 6, height: 6, borderRadius: '50%', background: color, flexShrink: 0 }} aria-hidden="true" />;
}

interface CitationChipProps {
  citation: ResolvedCitation;
  small?: boolean;
}

function CitationLabel({ citation }: { citation: ResolvedCitation }) {
  return citation.patentId ?? citation.title ?? citation.ref;
}

function CitationTooltip({ citation }: { citation: ResolvedCitation }) {
  const sourceLabel = citation.sourceType ? EVIDENCE_LABEL[citation.sourceType] ?? citation.sourceType : undefined;
  return sourceLabel ? `${citation.title ?? citation.ref} — ${sourceLabel}` : (citation.title ?? citation.ref);
}

function isLongTitle(c: ResolvedCitation): boolean {
  return !!c.title && !c.patentId && c.title.length > 28;
}

function chipFontFamily(c: ResolvedCitation): string {
  return (!!c.patentId || (!c.title && !!c.ref)) ? 'var(--font-mono)' : 'var(--font-body)';
}

function chipFontSize(c: ResolvedCitation, small: boolean): number {
  const isMonoFont = !!c.patentId || (!c.title && !!c.ref);
  return small ? (isMonoFont ? 10.5 : 11) : (isMonoFont ? 11 : 11);
}

export function CitationChip({ citation, small = false }: CitationChipProps) {
  const label = CitationLabel({ citation });
  const tooltip = CitationTooltip({ citation });
  const fontFamily = chipFontFamily(citation);
  const fontSize = chipFontSize(citation, small);
  const isLong = isLongTitle(citation);
  const similarityBadge = citation.similarity != null ? (
    <Badge tone="neutral">{Math.round(citation.similarity * 100)}%</Badge>
  ) : null;

  const chipStyle: CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 6,
    padding: small ? '2px 7px' : '3px 8px',
    borderRadius: 'var(--radius-sm)',
    background: 'var(--status-info-bg)',
    color: 'var(--status-info-fg)',
    fontFamily,
    fontSize,
    fontWeight: 500,
    border: '1px solid transparent',
    transition: 'background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast)',
    ...(isLong && {
      maxWidth: '34ch',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap',
    }),
  };

  if (citation.url) {
    return (
      <a
        href={citation.url}
        target="_blank"
        rel="noopener noreferrer"
        aria-label={`Open ${label} (${citation.sourceType ?? 'citation'}, similarity ${citation.similarity != null ? Math.round(citation.similarity * 100) : 'unknown'}%, opens in new tab)`}
        style={{
          textDecoration: 'none',
          color: 'var(--status-info-fg)',
          display: 'inline-flex',
          alignItems: 'center',
          gap: 6,
          outline: 'none',
        }}
        onFocus={(e) => {
          e.currentTarget.style.outline = '2px solid var(--accent-primary)';
          e.currentTarget.style.outlineOffset = '2px';
          e.currentTarget.style.borderRadius = 'var(--radius-sm)';
        }}
        onBlur={(e) => {
          e.currentTarget.style.outline = 'none';
        }}
      >
        <span style={chipStyle} title={tooltip}>
          {getSourceKindDot(citation.sourceType)}
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{label}</span>
        </span>
        <span aria-hidden="true" style={{ fontSize: 11, fontWeight: 600 }}>↗</span>
        {similarityBadge}
      </a>
    );
  }

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <span style={chipStyle} title={tooltip}>
        {getSourceKindDot(citation.sourceType)}
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{label}</span>
      </span>
      {similarityBadge}
    </span>
  );
}

interface AxisScoreRowProps {
  axis: AxisScore;
  isLast: boolean;
}

function axisTitle(axis: string): string {
  return axis.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

export function AxisScoreRow({ axis, isLast }: AxisScoreRowProps) {
  const resolvedCount = axis.citations.filter(c => c.title || c.url || c.patentId).length;
  const hasResolved = resolvedCount > 0;
  return (
    <div style={{ padding: '16px 0', borderBottom: isLast ? 'none' : '1px solid var(--border-subtle)' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
        <div style={{ fontWeight: 600, color: 'var(--text-primary)', fontSize: 14 }}>{axisTitle(axis.axis)}</div>
        <div style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--accent-primary)' }}>{Math.round(axis.score)} / 100</div>
      </div>
      <div style={{ marginBottom: 10 }}>
        <ProgressBar pct={Math.min(100, Math.max(0, axis.score))} height={5} />
      </div>
      {axis.reasoning && <div style={{ fontSize: 13, color: 'var(--text-body)', lineHeight: 1.6, marginBottom: 10 }}>{axis.reasoning}</div>}
      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
        <span style={{ fontSize: 11, color: 'var(--text-muted)', marginRight: 2 }}>Evidence</span>
        {hasResolved ? (
          axis.citations.map((c, idx) => <CitationChip key={`${c.ref}-${idx}`} citation={c} small={false} />)
        ) : (
          <span style={{ color: 'var(--text-body)', fontStyle: 'italic', fontSize: 12 }}>
            Not captured <span style={{ color: 'var(--text-muted)', fontSize: 11 }}>— scored on the disclosure alone</span>
          </span>
        )}
      </div>
    </div>
  );
}

interface EvidenceSourceCardProps {
  source: EvidenceSource;
}

// Scoped layout for the 3-up Evidence Sources row. minmax(0,1fr) forces three equal
// tracks that shrink instead of blowing out on long content; the fixed body height
// keeps every card the same size. Below 900px the row stacks and relaxes the height.
export const EVIDENCE_GRID_CSS = `
  .evidence-sources-grid {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 16px;
    align-items: stretch;
  }
  .evidence-sources-grid .evi-card-body {
    height: 168px;
  }
  @media (max-width: 900px) {
    .evidence-sources-grid {
      grid-template-columns: 1fr;
    }
    .evidence-sources-grid .evi-card-body {
      height: auto;
      max-height: 220px;
    }
  }
`;

type EvidenceDotState = 'available' | 'unavailable' | 'not_captured' | 'blocked' | 'failed';

function StateDot({ state }: { state: EvidenceDotState }) {
  const bg =
    state === 'available' ? 'var(--status-success-fg)' :
    state === 'unavailable' ? 'var(--status-warning-fg)' :
    state === 'failed' ? 'var(--status-warning-fg)' :
    state === 'blocked' ? 'var(--status-info-fg)' :
    'var(--gray-300)';
  return <span style={{ width: 8, height: 8, borderRadius: '50%', background: bg, display: 'inline-block', flexShrink: 0 }} aria-hidden="true" />;
}

function ShieldSlashIcon() {
  return (
    <svg
      width="12"
      height="12"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      aria-hidden="true"
      style={{ flexShrink: 0 }}
    >
      <path d="M12 2 4 5v6c0 5 3.5 9 8 11 1.7-.76 3.16-1.8 4.33-3.06M20 11V5l-4.2-1.68" />
      <path d="M2 2l20 20" />
    </svg>
  );
}

type EvidencePresentationKey = EvidenceState | 'not_captured';

interface EvidencePresentation {
  cardBorderStyle: 'solid' | 'dashed';
  cardBackground: string;
  leftBorderColor: string;
  leftBorderStyle: 'solid' | 'dashed';
  statePillBg: string;
  statePillFg: string;
  stateLabel: string;
  dotState: EvidenceDotState;
  showShieldIcon: boolean;
  progressBar: 'confidence' | 'full' | 'empty';
  showCitations: boolean;
  metaText: (pct: number) => string;
  summaryText: (source: EvidenceSource) => string;
}

const EVIDENCE_PRESENTATION: Record<EvidencePresentationKey, EvidencePresentation> = {
  content_filtered: {
    cardBorderStyle: 'solid',
    cardBackground: 'var(--surface-card-alt)',
    leftBorderColor: 'var(--status-info-fg)',
    leftBorderStyle: 'solid',
    statePillBg: 'var(--status-info-bg)',
    statePillFg: 'var(--status-info-fg)',
    stateLabel: 'Blocked',
    dotState: 'blocked',
    showShieldIcon: true,
    progressBar: 'empty',
    showCitations: false,
    metaText: () => 'Blocked by provider policy',
    summaryText: () => "This source ran, but the model provider's content filter refused the request. It's excluded from this score — not missing, and not a zero result.",
  },
  source_error: {
    cardBorderStyle: 'solid',
    cardBackground: 'var(--surface-card-alt)',
    leftBorderColor: 'var(--status-warning-fg)',
    leftBorderStyle: 'solid',
    statePillBg: 'var(--status-warning-bg)',
    statePillFg: 'var(--status-warning-fg)',
    stateLabel: 'Failed',
    dotState: 'failed',
    showShieldIcon: false,
    progressBar: 'empty',
    showCitations: false,
    metaText: () => 'Source failed',
    summaryText: (source) => source.summary,
  },
  source_timeout: {
    cardBorderStyle: 'solid',
    cardBackground: 'var(--surface-card-alt)',
    leftBorderColor: 'var(--status-warning-fg)',
    leftBorderStyle: 'solid',
    statePillBg: 'var(--status-warning-bg)',
    statePillFg: 'var(--status-warning-fg)',
    stateLabel: 'Timed out',
    dotState: 'failed',
    showShieldIcon: false,
    progressBar: 'empty',
    showCitations: false,
    metaText: () => 'Source timed out',
    summaryText: (source) => source.summary,
  },
  not_captured: {
    cardBorderStyle: 'dashed',
    cardBackground: 'var(--surface-sunken)',
    leftBorderColor: 'var(--status-warning-fg)',
    leftBorderStyle: 'dashed',
    statePillBg: 'var(--status-warning-bg)',
    statePillFg: 'var(--status-warning-fg)',
    stateLabel: 'Not captured',
    dotState: 'not_captured',
    showShieldIcon: false,
    progressBar: 'full',
    showCitations: false,
    metaText: () => 'Not captured',
    summaryText: () => 'We have no record that this source ran for this candidate.',
  },
  unavailable: {
    cardBorderStyle: 'solid',
    cardBackground: 'var(--surface-card-alt)',
    leftBorderColor: 'var(--status-warning-fg)',
    leftBorderStyle: 'solid',
    statePillBg: 'var(--status-warning-bg)',
    statePillFg: 'var(--status-warning-fg)',
    stateLabel: 'Unavailable',
    dotState: 'unavailable',
    showShieldIcon: false,
    progressBar: 'full',
    showCitations: false,
    metaText: () => 'Ran, returned 0 matches',
    summaryText: () => 'The source ran and genuinely returned nothing. This is a real result — not a failure.',
  },
  active_cited: {
    cardBorderStyle: 'solid',
    cardBackground: 'var(--surface-card-alt)',
    leftBorderColor: 'var(--status-success-fg)',
    leftBorderStyle: 'solid',
    statePillBg: 'var(--status-success-bg)',
    statePillFg: 'var(--status-success-fg)',
    stateLabel: 'Available',
    dotState: 'available',
    showShieldIcon: false,
    progressBar: 'confidence',
    showCitations: true,
    metaText: (pct) => `${pct}% confidence`,
    summaryText: (source) => source.summary,
  },
  active_uncited: {
    cardBorderStyle: 'solid',
    cardBackground: 'var(--surface-card-alt)',
    leftBorderColor: 'var(--border-strong)',
    leftBorderStyle: 'solid',
    statePillBg: 'var(--gray-100)',
    statePillFg: 'var(--text-muted)',
    stateLabel: 'Available',
    dotState: 'available',
    showShieldIcon: false,
    progressBar: 'empty',
    showCitations: false,
    metaText: () => 'Contributed to scoring',
    summaryText: (source) => source.summary,
  },
};

function resolveEvidencePresentationKey(source: EvidenceSource): EvidencePresentationKey {
  if (source.state !== 'unavailable') return source.state;
  return source.available ? 'unavailable' : 'not_captured';
}

function EvidenceStatePill({ presentation }: { presentation: EvidencePresentation }) {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        padding: '4px 10px',
        borderRadius: 'var(--radius-full)',
        background: presentation.statePillBg,
        color: presentation.statePillFg,
        fontWeight: 600,
        fontSize: 11,
        whiteSpace: 'nowrap',
      }}
    >
      {presentation.showShieldIcon && <ShieldSlashIcon />}
      <StateDot state={presentation.dotState} />
      {presentation.stateLabel}
    </span>
  );
}

function EvidenceProgressBar({ variant, pct, ariaLabel }: { variant: EvidencePresentation['progressBar']; pct: number; ariaLabel?: string }) {
  if (variant === 'confidence') return <ProgressBar pct={pct} height={5} ariaLabel={ariaLabel} />;
  return (
    <div style={{ height: 5, borderRadius: 'var(--radius-full)', background: 'var(--gray-100)', overflow: 'hidden' }} aria-hidden="true">
      <div style={{ width: variant === 'full' ? '100%' : '0%', height: '100%', background: 'var(--gray-300)', borderRadius: 'var(--radius-full)' }} />
    </div>
  );
}

function EvidenceCitations({ source, presentation }: { source: EvidenceSource; presentation: EvidencePresentation }) {
  if (!presentation.showCitations || source.citations.length === 0) return null;
  return (
    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
      {source.citations.map((c, idx) => <CitationChip key={`${c.ref}-${idx}`} citation={c} small />)}
    </div>
  );
}

// Preview shows this many list items inside the fixed-height card body; the rest
// live in the "view all" modal. Kept small so every card resolves to one silhouette.
// The body's fixed height (168px = 3 × 40px rows + fade room) lives in EVIDENCE_GRID_CSS.
const EVIDENCE_PREVIEW_LIMIT = 3;

function evidencePreview<T>(items: T[]): { visible: T[]; hasMore: boolean } {
  return { visible: items.slice(0, EVIDENCE_PREVIEW_LIMIT), hasMore: items.length > EVIDENCE_PREVIEW_LIMIT };
}

// Accent-text button pinned to the card bottom that opens the full-content modal.
const ViewAllButton = forwardRef<HTMLButtonElement, { label: string; onClick: () => void }>(
  function ViewAllButton({ label, onClick }, ref) {
    return (
      <button
        ref={ref}
        type="button"
        aria-haspopup="dialog"
        onClick={onClick}
        style={{
          marginTop: 'auto',
          alignSelf: 'flex-start',
          background: 'none',
          border: 'none',
          color: 'var(--accent-primary)',
          font: 'inherit',
          fontSize: 12,
          fontWeight: 600,
          cursor: 'pointer',
          padding: '6px 0',
          minHeight: 40,
          outline: 'none',
        }}
        onFocus={(e) => {
          e.currentTarget.style.outline = '2px solid var(--accent-primary)';
          e.currentTarget.style.outlineOffset = '2px';
          e.currentTarget.style.borderRadius = 'var(--radius-sm)';
        }}
        onBlur={(e) => {
          e.currentTarget.style.outline = 'none';
        }}
      >
        {label}
      </button>
    );
  }
);

function evidenceHitRelevancePct(similarity?: number): number | null {
  return similarity != null ? Math.round(similarity * 100) : null;
}

function evidenceHitRowKey(hit: EvidenceHit, idx: number): string {
  return `${hit.patentId ?? hit.url ?? hit.title}-${idx}`;
}

function EvidenceHitRowDot({ sourceType }: { sourceType: EvidenceSourceType }) {
  const color = SOURCE_KIND_COLOR[sourceType] ?? 'var(--gray-300)';
  return <span style={{ width: 8, height: 8, borderRadius: '50%', background: color, flexShrink: 0 }} aria-hidden="true" />;
}

function PatentHitLink({ hit, pct, wrap }: { hit: EvidenceHit; pct: number | null; wrap?: boolean }) {
  const jurisdictionLabel = hit.jurisdiction ?? 'patent';
  const patentIdPart = hit.patentId ? `, ${hit.patentId}` : '';
  const label = `Open ${hit.title}${patentIdPart} (${jurisdictionLabel} patent, relevance ${pct ?? 0}%, opens in new tab)`;
  const titleStyle: CSSProperties = wrap
    ? { whiteSpace: 'normal', wordBreak: 'break-word' }
    : { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' };
  return (
    <a
      href={hit.url}
      target="_blank"
      rel="noopener noreferrer"
      aria-label={label}
      style={{
        flex: 1,
        minWidth: 0,
        display: 'inline-flex',
        alignItems: wrap ? 'flex-start' : 'center',
        gap: 6,
        color: 'var(--text-primary)',
        fontSize: 13.5,
        fontWeight: 500,
        textDecoration: 'none',
        outline: 'none',
      }}
      onFocus={(e) => {
        e.currentTarget.style.outline = '2px solid var(--accent-primary)';
        e.currentTarget.style.outlineOffset = '2px';
        e.currentTarget.style.borderRadius = 'var(--radius-sm)';
      }}
      onBlur={(e) => {
        e.currentTarget.style.outline = 'none';
      }}
    >
      <span style={titleStyle}>{hit.title}</span>
      <span aria-hidden="true" style={{ fontWeight: 600 }}>↗</span>
    </a>
  );
}

function EvidenceHitRow({ hit, sourceType, isLast, wrap }: { hit: EvidenceHit; sourceType: EvidenceSourceType; isLast: boolean; wrap?: boolean }) {
  const isPatent = sourceType === 'patent_api';
  const pct = evidenceHitRelevancePct(hit.similarity);
  const rowStyle: CSSProperties = {
    display: 'flex',
    alignItems: wrap ? 'flex-start' : 'center',
    gap: 10,
    padding: '10px 0',
    borderBottom: isLast ? 'none' : '1px solid var(--border-subtle)',
    minHeight: 40,
  };
  const relevanceBadge = pct != null ? <Badge tone="neutral">{pct}%</Badge> : null;
  const juris = isPatent && hit.jurisdiction ? (
    <span
      style={{
        fontFamily: 'var(--font-mono)',
        fontSize: 10,
        fontWeight: 600,
        letterSpacing: '0.04em',
        color: 'var(--text-muted)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-sm)',
        padding: '1px 5px',
        flexShrink: 0,
        textTransform: 'uppercase',
      }}
    >
      {hit.jurisdiction}
    </span>
  ) : null;

  return (
    <div style={rowStyle}>
      <EvidenceHitRowDot sourceType={sourceType} />
      {juris}
      {isPatent && hit.url ? (
        <PatentHitLink hit={hit} pct={pct} wrap={wrap} />
      ) : (
        <span
          style={{
            flex: 1,
            minWidth: 0,
            color: 'var(--text-primary)',
            fontSize: 13.5,
            fontWeight: 500,
            ...(wrap
              ? { whiteSpace: 'normal', wordBreak: 'break-word' }
              : { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }),
          }}
        >
          {hit.title}
        </span>
      )}
      {relevanceBadge}
    </div>
  );
}

// Preview: first N hit rows, single-line ellipsis, inside the fixed-height body.
function EvidenceHitsPreview({ hits, sourceType }: { hits: EvidenceHit[]; sourceType: EvidenceSourceType }) {
  const { visible } = evidencePreview(hits);
  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      {visible.map((hit, idx) => (
        <EvidenceHitRow key={evidenceHitRowKey(hit, idx)} hit={hit} sourceType={sourceType} isLast={idx === visible.length - 1} />
      ))}
    </div>
  );
}

// Full list for the modal: every hit, wrapped titles, scrolls inside the dialog.
function EvidenceHitsFull({ hits, sourceType }: { hits: EvidenceHit[]; sourceType: EvidenceSourceType }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      {hits.map((hit, idx) => (
        <EvidenceHitRow key={evidenceHitRowKey(hit, idx)} hit={hit} sourceType={sourceType} isLast={idx === hits.length - 1} wrap />
      ))}
    </div>
  );
}

function ReasoningBullet({ text, clamp }: { text: string; clamp?: boolean }) {
  const clampStyle: CSSProperties = clamp
    ? { display: '-webkit-box', WebkitLineClamp: 3, WebkitBoxOrient: 'vertical', overflow: 'hidden' }
    : {};
  return (
    <li style={{ display: 'flex', gap: 8, fontSize: 12.5, color: 'var(--text-body)', lineHeight: 1.5 }}>
      <span
        aria-hidden="true"
        style={{ width: 6, height: 6, borderRadius: '50%', background: SOURCE_KIND_COLOR.llm_deep_research, marginTop: 6, flexShrink: 0 }}
      />
      <span style={clampStyle}>{text}</span>
    </li>
  );
}

// Preview: one finding clamped to 3 lines inside the fixed-height body.
function ReasoningPreview({ reasoning }: { reasoning: string[] }) {
  const first = reasoning[0];
  if (first == null) return null;
  return (
    <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: 8 }}>
      <ReasoningBullet text={first} clamp />
    </ul>
  );
}

// Full findings for the modal: every bullet, no clamp, scrolls inside the dialog.
function ReasoningFull({ reasoning }: { reasoning: string[] }) {
  return (
    <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: 14 }}>
      {reasoning.map((text, idx) => (
        <ReasoningBullet key={`reason-${idx}`} text={text} />
      ))}
    </ul>
  );
}

function InfoCircleIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" style={{ flexShrink: 0 }}>
      <circle cx="12" cy="12" r="10" />
      <path d="M12 8v5" />
      <path d="M12 16h.01" />
    </svg>
  );
}

function EvidenceDegradedChip({ source }: { source: EvidenceSource }) {
  if (source.sourceType !== 'patent_api' || !source.degraded) return null;
  const degradedSources = source.degradedSources ?? [];
  if (degradedSources.length === 0) return null;
  const list = joinReasonsWithAnd(degradedSources);
  return (
    <span
      role="note"
      aria-label={`Partial patent coverage: ${list} unavailable; the remaining patent sources are included in these results.`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        fontSize: 11,
        fontWeight: 500,
        color: 'var(--status-info-fg)',
        background: 'var(--status-info-bg)',
        borderRadius: 'var(--radius-sm)',
        padding: '4px 8px',
        alignSelf: 'flex-start',
      }}
    >
      <InfoCircleIcon />
      Partial coverage — {list} unavailable
    </span>
  );
}

// A single long finding still overflows a 3-line clamp beyond this length, so the
// preview earns a "view full analysis" affordance even with just one finding.
const REASONING_LONG_CHARS = 160;

interface EvidenceContent {
  preview: ReactNode;
  full: ReactNode | null;
  hasMore: boolean;
  buttonLabel: string;
  modalTitle: string;
  modalSubtitle: string;
  isSummary: boolean;
}

function reasoningContent(source: EvidenceSource, pct: number, label: string): EvidenceContent {
  const reasoning = source.reasoning!;
  const hasMore = reasoning.length > 1 || (reasoning[0]?.length ?? 0) > REASONING_LONG_CHARS;
  return {
    preview: <ReasoningPreview reasoning={reasoning} />,
    full: <ReasoningFull reasoning={reasoning} />,
    hasMore,
    buttonLabel: 'View full analysis',
    modalTitle: `${label} — full analysis`,
    modalSubtitle: `${pct}% confidence · ${reasoning.length} reasoning ${reasoning.length === 1 ? 'finding' : 'findings'}`,
    isSummary: false,
  };
}

function hitsContent(source: EvidenceSource, pct: number, label: string): EvidenceContent {
  const hits = source.hits!;
  const noun = hits.length === 1 ? 'match' : 'matches';
  return {
    preview: <EvidenceHitsPreview hits={hits} sourceType={source.sourceType} />,
    full: <EvidenceHitsFull hits={hits} sourceType={source.sourceType} />,
    hasMore: hits.length > EVIDENCE_PREVIEW_LIMIT,
    buttonLabel: `View all ${hits.length} hits`,
    modalTitle: `${label} — full results`,
    modalSubtitle: `${pct}% confidence · ${hits.length} ${noun}`,
    isSummary: false,
  };
}

function summaryContent(source: EvidenceSource, presentation: EvidencePresentation): EvidenceContent {
  return {
    preview: (
      <div style={{ fontSize: 12.5, color: 'var(--text-body)', lineHeight: 1.5 }}>{presentation.summaryText(source)}</div>
    ),
    full: null,
    hasMore: false,
    buttonLabel: '',
    modalTitle: '',
    modalSubtitle: '',
    isSummary: true,
  };
}

function resolveEvidenceContent(source: EvidenceSource, presentation: EvidencePresentation, pct: number, label: string): EvidenceContent {
  if (source.sourceType === 'llm_deep_research' && (source.reasoning?.length ?? 0) > 0) {
    return reasoningContent(source, pct, label);
  }
  if ((source.hits?.length ?? 0) > 0) {
    return hitsContent(source, pct, label);
  }
  if (presentation.showCitations && source.citations.length > 0) {
    return { ...summaryContent(source, presentation), preview: <EvidenceCitations source={source} presentation={presentation} /> };
  }
  return summaryContent(source, presentation);
}

// Fixed-height region so every card resolves to the same silhouette. Summary/failure
// states center their content; list/reasoning states top-align and fade when clipped.
function EvidenceBodyRegion({ content, fadeColor }: { content: EvidenceContent; fadeColor: string }) {
  return (
    <div
      className="evi-card-body"
      style={{
        position: 'relative',
        overflow: 'hidden',
        display: 'flex',
        flexDirection: 'column',
        justifyContent: content.isSummary ? 'center' : 'flex-start',
        gap: content.isSummary ? 10 : 0,
      }}
    >
      {content.preview}
      {content.hasMore && (
        <div
          aria-hidden="true"
          style={{
            position: 'absolute',
            left: 0,
            right: 0,
            bottom: 0,
            height: 40,
            background: `linear-gradient(to bottom, transparent, ${fadeColor})`,
            pointerEvents: 'none',
          }}
        />
      )}
    </div>
  );
}

function EvidenceDetailModal({ open, onClose, content }: { open: boolean; onClose: () => void; content: EvidenceContent }) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={content.modalTitle}
      subtitle={content.modalSubtitle}
      width={560}
      footer={<Button variant="secondary" onClick={onClose}>Close</Button>}
    >
      {content.full}
    </Modal>
  );
}

export function EvidenceSourceCard({ source }: EvidenceSourceCardProps) {
  const sourceLabel = EVIDENCE_LABEL[source.sourceType] ?? source.sourceType;
  const pct = Math.round((source.confidence ?? 0) * 100);
  const p = EVIDENCE_PRESENTATION[resolveEvidencePresentationKey(source)];
  const confidenceAriaLabel = p.progressBar === 'confidence' ? `Confidence ${pct}%` : undefined;
  const content = resolveEvidenceContent(source, p, pct, sourceLabel);
  const [modalOpen, setModalOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const closeModal = () => {
    setModalOpen(false);
    triggerRef.current?.focus();
  };

  return (
    <div
      role="group"
      aria-label={`${sourceLabel} evidence`}
      style={{
        border: '1px solid var(--border-subtle)',
        borderStyle: p.cardBorderStyle,
        borderLeft: `3px ${p.leftBorderStyle} ${p.leftBorderColor}`,
        borderRadius: 'var(--radius-md)',
        padding: 18,
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
        height: '100%',
        minWidth: 0,
        background: p.cardBackground,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <div style={{ fontWeight: 600, fontSize: 13, color: 'var(--text-primary)' }}>{sourceLabel}</div>
        <EvidenceStatePill presentation={p} />
      </div>
      <EvidenceProgressBar variant={p.progressBar} pct={pct} ariaLabel={confidenceAriaLabel} />
      <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{p.metaText(pct)}</div>
      <EvidenceDegradedChip source={source} />
      <EvidenceBodyRegion content={content} fadeColor={p.cardBackground} />
      {content.hasMore && content.full && (
        <ViewAllButton ref={triggerRef} label={content.buttonLabel} onClick={() => setModalOpen(true)} />
      )}
      {content.full && <EvidenceDetailModal open={modalOpen} onClose={closeModal} content={content} />}
    </div>
  );
}

interface ClaimDraftPanelProps {
  claimDraft: string;
}

export function ClaimDraftPanel({ claimDraft }: ClaimDraftPanelProps) {
  if (!claimDraft || claimDraft.trim() === '') return null;
  return (
    <div style={{ marginTop: 20, paddingTop: 20, borderTop: '1px solid var(--border-subtle)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
        <span style={{ fontSize: 12, fontWeight: 600, letterSpacing: '0.02em', textTransform: 'uppercase', color: 'var(--text-muted)' }}>Claim Draft</span>
        <Badge tone="info">Grounded draft</Badge>
      </div>
      <div
        style={{
          fontFamily: 'var(--font-mono)',
          fontSize: 13,
          color: 'var(--text-body)',
          background: 'var(--surface-sunken)',
          borderRadius: 'var(--radius-md)',
          padding: 16,
          lineHeight: 1.7,
        }}
      >
        {claimDraft}
      </div>
    </div>
  );
}

interface ProvenancePanelProps {
  provenance?: Provenance;
  recommendation?: string;
  batchId?: string;
}

function formatNumber(n: number): string {
  return n.toLocaleString('en-US');
}

const DISCLOSURE_CSS = `
  .provenance-technical-details summary {
    display: flex;
    align-items: center;
    gap: 8px;
    cursor: pointer;
    list-style: none;
    font-size: 12px;
    font-weight: 600;
    letter-spacing: 0.02em;
    text-transform: uppercase;
    color: var(--text-muted);
    min-height: 44px;
    padding: 6px 0;
    user-select: none;
  }
  .provenance-technical-details summary::-webkit-details-marker {
    display: none;
  }
  .provenance-technical-details summary svg {
    transition: transform var(--duration-fast) var(--ease-standard);
  }
  .provenance-technical-details[open] summary svg {
    transform: rotate(90deg);
  }
  .provenance-technical-details summary:focus-visible {
    outline: 2px solid var(--accent-primary);
    outline-offset: 2px;
  }
`;

export function TechnicalDisclosure({ summaryLabel, children }: { summaryLabel: string; children: ReactNode }) {
  return (
    <details className="provenance-technical-details" style={{ marginTop: 16, borderTop: '1px solid var(--border-subtle)', paddingTop: 16 }}>
      <style>{DISCLOSURE_CSS}</style>
      <summary>
        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
          <path d="M9 6l6 6-6 6" />
        </svg>
        {summaryLabel}
      </summary>
      {children}
    </details>
  );
}

export function CopyChip({ displayText, fullText, title, ariaLabel, iconOnly }: { displayText: string; fullText: string; title: string; ariaLabel: string; iconOnly?: boolean }) {
  const { show } = useToast();
  const handleCopy = () => {
    void navigator.clipboard?.writeText(fullText).then(() => {
      show({ variant: 'success', title: 'Copied to clipboard' });
    });
  };
  return (
    <button
      type="button"
      onClick={handleCopy}
      title={title}
      aria-label={ariaLabel}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        fontFamily: 'var(--font-mono)',
        fontSize: 11,
        padding: iconOnly ? '3px 6px' : '3px 8px',
        borderRadius: 'var(--radius-sm)',
        background: 'var(--status-info-bg)',
        color: 'var(--status-info-fg)',
        border: 'none',
        cursor: 'pointer',
      }}
    >
      {!iconOnly && displayText}
      <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
        <rect x="9" y="9" width="11" height="11" rx="2" />
        <path d="M5 15V5a2 2 0 0 1 2-2h10" />
      </svg>
    </button>
  );
}

interface ProvFieldProps {
  label: string;
  value: unknown;
  sourceKind?: string;
  appliesTo?: string[];
  render?: (val: NonNullable<unknown>) => ReactNode;
  mono?: boolean;
}

function normalizeSourceKind(kind?: string): string {
  return kind ? kind.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase()) : '';
}

function ProvField({ label, value, sourceKind, appliesTo, render, mono = true }: ProvFieldProps) {
  const normalizedKind = normalizeSourceKind(sourceKind);
  const appliesToSource = !appliesTo || appliesTo.includes(normalizedKind);
  const hasValue = value != null && value !== '';
  const state = hasValue ? 'value' : !appliesToSource ? 'not_applicable' : 'unavailable';

  return (
    <>
      <dt style={{ fontSize: 12.5, color: 'var(--text-muted)', alignSelf: 'center' }}>{label}</dt>
      <dd style={{ margin: 0, fontSize: 12.5, color: 'var(--text-body)', alignSelf: 'center', display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        {state === 'value' ? (
          render ? render(value as NonNullable<unknown>) : (
            <span style={{ fontFamily: mono ? 'var(--font-mono)' : 'var(--font-body)', fontWeight: mono ? 600 : 400, color: 'var(--text-primary)' }}>
              {String(value)}
            </span>
          )
        ) : state === 'not_applicable' ? (
          <>
            <span style={{ color: 'var(--text-body)', fontStyle: 'italic' }}>Not captured</span>
            <span style={{ color: 'var(--text-muted)', fontSize: 11 }}>— not applicable for {normalizedKind.toLowerCase()}</span>
          </>
        ) : (
          <span style={{ color: 'var(--text-body)', fontStyle: 'italic' }}>Not captured</span>
        )}
      </dd>
    </>
  );
}

function ProvenanceTechnicalDetails({ provenance }: { provenance: Provenance }) {
  const hasSpan = provenance.spanStart != null && provenance.spanEnd != null;
  if (!provenance.sourceDocumentId && !hasSpan && provenance.chunkIndex == null && !provenance.chunkId) return null;

  return (
    <TechnicalDisclosure summaryLabel="Technical details">
      <dl style={{ display: 'grid', gridTemplateColumns: '150px 1fr', gap: '12px 20px', margin: 0, marginTop: 12 }}>
        <ProvField
          label="Document ID"
          value={provenance.sourceDocumentId}
          render={(v) => (
            <CopyChip
              displayText={`${String(v).slice(0, 8)}…`}
              fullText={String(v)}
              title={String(v)}
              ariaLabel={`Document ID ${String(v).slice(0, 8)}, click to copy full ID`}
            />
          )}
        />
        <ProvField
          label="Chunk ID"
          value={provenance.chunkId}
          render={(v) => (
            <CopyChip
              displayText={`${String(v).slice(0, 8)}…`}
              fullText={String(v)}
              title={String(v)}
              ariaLabel={`Chunk ID ${String(v).slice(0, 8)}, click to copy full ID`}
            />
          )}
        />
        <ProvField
          label="Character span"
          value={provenance.spanStart != null && provenance.spanEnd != null ? { start: provenance.spanStart, end: provenance.spanEnd } : null}
          render={(v) => {
            const span = v as { start: number; end: number };
            return (
              <>
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--text-primary)' }}>
                  characters {formatNumber(span.start)}–{formatNumber(span.end)}
                </span>
                <CopyChip
                  displayText=""
                  fullText={`chars:${span.start}-${span.end}`}
                  title="Copy character span"
                  ariaLabel={`Copy character span ${span.start} to ${span.end}`}
                  iconOnly
                />
              </>
            );
          }}
        />
        <ProvField
          label="Chunk"
          value={provenance.chunkIndex}
          render={(v) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--text-primary)' }}>Chunk {String(v)}</span>}
        />
      </dl>
    </TechnicalDisclosure>
  );
}

function ProvenanceLocation({ provenance }: { provenance: Provenance }) {
  const sourceKindNormalized = normalizeSourceKind(provenance.sourceKind);

  return (
    <div style={{ marginBottom: 20 }}>
      <div style={{ fontSize: 12, fontWeight: 600, letterSpacing: '0.02em', textTransform: 'uppercase', color: 'var(--text-muted)', marginBottom: 12 }}>Location</div>
      <dl style={{ display: 'grid', gridTemplateColumns: '150px 1fr', gap: '12px 20px', margin: 0 }}>
        <ProvField label="Source kind" value={sourceKindNormalized} />
        {provenance.sourceFilename ? <ProvField label="File" value={provenance.sourceFilename} /> : null}
        <ProvField label="Page" value={provenance.pageNumber} sourceKind={provenance.sourceKind} appliesTo={['Paper', 'Pdf']} render={(v) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--text-primary)' }}>Page {String(v)}</span>} />
        <ProvField label="Section" value={provenance.sectionHint} sourceKind={provenance.sourceKind} appliesTo={['Paper', 'Pdf']} />
      </dl>
      <ProvenanceTechnicalDetails provenance={provenance} />
    </div>
  );
}

export function ExcerptBlockquote({ text, cite, mono = false }: { text: string; cite?: string; mono?: boolean }) {
  return (
    <figure style={{ margin: 0 }}>
      <blockquote
        cite={cite}
        style={{
          margin: 0,
          background: 'var(--surface-sunken)',
          borderLeft: '3px solid var(--accent-primary)',
          borderRadius: 'var(--radius-md)',
          padding: '16px 18px',
          fontSize: 13.5,
          color: 'var(--text-body)',
          lineHeight: 1.7,
          fontFamily: mono ? 'var(--font-mono)' : 'var(--font-body)',
          whiteSpace: mono ? 'pre-wrap' : 'normal',
        }}
      >
        {text}
      </blockquote>
    </figure>
  );
}

export function NoExcerptNote() {
  return (
    <div
      style={{
        background: 'var(--surface-sunken)',
        border: '1px dashed var(--border-strong)',
        borderRadius: 'var(--radius-md)',
        padding: '16px 18px',
        fontSize: 12.5,
        color: 'var(--text-body)',
        fontStyle: 'italic',
      }}
    >
      No source excerpt was captured for this candidate.
    </div>
  );
}

const SECTION_LABEL_STYLE: CSSProperties = { fontSize: 12, fontWeight: 600, letterSpacing: '0.02em', textTransform: 'uppercase', color: 'var(--text-muted)', marginBottom: 12 };

function ProvenanceExcerpt({ provenance }: { provenance: Provenance }) {
  const { text, mono } = resolveExcerptView(provenance);
  return (
    <div style={{ marginBottom: 20, paddingTop: 20, borderTop: '1px solid var(--border-subtle)' }}>
      <div style={SECTION_LABEL_STYLE}>Excerpt</div>
      {text ? (
        <ExcerptBlockquote text={text} cite={provenance.hitUrl} mono={mono} />
      ) : (
        <NoExcerptNote />
      )}
    </div>
  );
}

function ProvenanceActions({ provenance }: { provenance: Provenance }) {
  const deepLinkLabel = provenance.pageNumber != null && provenance.pageNumber > 0
    ? `View this excerpt in the source document, page ${provenance.pageNumber} (opens in a new tab)`
    : 'View this excerpt in the source (opens in a new tab)';

  return (
    <div style={{ paddingTop: 16, borderTop: '1px solid var(--border-subtle)' }}>
      <div style={{ fontSize: 12, fontWeight: 600, letterSpacing: '0.02em', textTransform: 'uppercase', color: 'var(--text-muted)', marginBottom: 12 }}>Actions</div>
      {provenance.hitUrl ? (
        <a
          href={provenance.hitUrl}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={deepLinkLabel}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 8,
            fontFamily: 'var(--font-body)',
            fontWeight: 500,
            fontSize: 13,
            padding: '8px 14px',
            borderRadius: 'var(--radius-md)',
            border: '1px solid var(--accent-primary)',
            color: 'var(--accent-primary)',
            background: 'var(--surface-card)',
            textDecoration: 'none',
            cursor: 'pointer',
          }}
        >
          View in source <span aria-hidden="true">↗</span>
        </a>
      ) : (
        <span
          style={{
            fontFamily: 'var(--font-body)',
            fontSize: 13,
            color: 'var(--text-muted)',
            fontStyle: 'italic',
            cursor: 'default',
            display: 'inline-flex',
            alignItems: 'center',
          }}
        >
          No source link available for this source type.
        </span>
      )}
    </div>
  );
}

const FALLBACK_BANNER_STYLE: CSSProperties = {
  padding: '10px 12px',
  background: 'var(--status-info-bg)',
  borderLeft: '3px solid var(--status-info-fg)',
  borderRadius: 'var(--radius-md)',
};

function ProvenanceFallback({ provenance }: { provenance: Provenance }) {
  const excerpt = provenance.cleanExcerpt ?? provenance.excerptText;

  return (
    <div>
      <div role="status" aria-live="polite" style={FALLBACK_BANNER_STYLE}>
        <div style={{ fontWeight: 600, fontSize: 13, color: 'var(--text-body)' }}>Source preview isn&apos;t available for this batch.</div>
        <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>This batch was processed before document preview was captured.</div>
      </div>
      <div style={{ marginTop: 20 }}>
        <div style={SECTION_LABEL_STYLE}>Excerpt</div>
        {excerpt ? <ExcerptBlockquote text={excerpt} cite={provenance.hitUrl} mono={provenance.sourceKind === 'Code'} /> : <NoExcerptNote />}
      </div>
      <ProvenanceTechnicalDetails provenance={provenance} />
    </div>
  );
}

function ProvenancePdfSection({ batchId, provenance, recommendation }: { batchId: string; provenance: Provenance; recommendation: string }) {
  const [documentUnavailable, setDocumentUnavailable] = useState(false);

  if (documentUnavailable) return <ProvenanceFallback provenance={provenance} />;

  return (
    <div>
      <Suspense fallback={<Skeleton height={520} />}>
        <LazyPdfProvenanceViewer
          batchId={batchId}
          documentId={provenance.sourceDocumentId}
          provenance={provenance}
          recommendation={recommendation}
          onDocumentNotFound={() => setDocumentUnavailable(true)}
        />
      </Suspense>
      <ProvenanceTechnicalDetails provenance={provenance} />
    </div>
  );
}

function resolvePreviewKind(provenance: Provenance, recommendation?: string, batchId?: string): 'pdf' | 'code' | 'none' | 'legacy' {
  if (provenance.previewKind === 'pdf' && recommendation && batchId) return 'pdf';
  if (provenance.previewKind === 'code') return 'code';
  if (provenance.previewKind === 'none') return 'none';
  return 'legacy';
}

function ProvenanceCodeSection({ provenance }: { provenance: Provenance }) {
  return (
    <div>
      <Suspense fallback={<Skeleton height={420} />}>
        <LazyCodeProvenanceViewer provenance={provenance} />
      </Suspense>
      <ProvenanceTechnicalDetails provenance={provenance} />
    </div>
  );
}

export function ProvenancePanel({ provenance, recommendation, batchId }: ProvenancePanelProps) {
  if (!provenance) return null;

  const mode = resolvePreviewKind(provenance, recommendation, batchId);
  if (mode === 'pdf') return <ProvenancePdfSection batchId={batchId!} provenance={provenance} recommendation={recommendation!} />;
  if (mode === 'code') return <ProvenanceCodeSection provenance={provenance} />;
  if (mode === 'none') return <ProvenanceFallback provenance={provenance} />;

  return (
    <>
      <ProvenanceLocation provenance={provenance} />
      <ProvenanceExcerpt provenance={provenance} />
      <ProvenanceActions provenance={provenance} />
    </>
  );
}
