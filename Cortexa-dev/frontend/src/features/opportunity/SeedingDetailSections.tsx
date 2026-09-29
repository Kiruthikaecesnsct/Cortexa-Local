import type { CSSProperties } from 'react';
import { Badge } from '../../shared/ds';
import { CopyChip, ExcerptBlockquote, TechnicalDisclosure } from './OpportunityDetailComponents';
import type { GroundedExcerpt, GroundedIn, OpportunityDetail, PriorArtItem } from './opportunityTypes';

const cardStyle: CSSProperties = {
  background: 'var(--surface-card)',
  border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-lg)',
  boxShadow: 'var(--shadow-xs)',
  padding: 28,
  marginBottom: 20,
};

const sectionTitle: CSSProperties = {
  fontWeight: 700,
  fontSize: 18,
  color: 'var(--text-primary)',
  marginBottom: 6,
};

const subtitleStyle: CSSProperties = {
  fontSize: 13,
  color: 'var(--text-muted)',
  marginBottom: 16,
};

const eyebrowStyle: CSSProperties = {
  fontSize: 12,
  fontWeight: 600,
  letterSpacing: '0.02em',
  textTransform: 'uppercase',
  color: 'var(--text-muted)',
  marginBottom: 8,
};

const proseStyle: CSSProperties = {
  fontSize: 'var(--text-base)',
  color: 'var(--text-body)',
  lineHeight: 1.6,
};

const dashedEmptyStyle: CSSProperties = {
  background: 'var(--surface-sunken)',
  border: '1px dashed var(--border-strong)',
  borderRadius: 'var(--radius-md)',
  padding: '16px 18px',
  fontSize: 12.5,
  color: 'var(--text-body)',
  fontStyle: 'italic',
};

const mutedItalic: CSSProperties = { fontSize: 13, color: 'var(--text-muted)', fontStyle: 'italic' };

/* ---- A1 — Novelty delta callout ---- */
export function NoveltyDeltaCard({ noveltyDelta }: { noveltyDelta?: string }) {
  if (!noveltyDelta || noveltyDelta.trim() === '') {
    return (
      <div style={cardStyle}>
        <div style={eyebrowStyle}>What&apos;s new beyond your document</div>
        <div style={mutedItalic}>No novelty delta was recorded for this opportunity.</div>
      </div>
    );
  }
  return (
    <div
      role="note"
      aria-label="Novelty delta"
      style={{
        ...cardStyle,
        borderLeft: '3px solid var(--accent-primary)',
        background: 'var(--status-info-bg)',
      }}
    >
      <div style={eyebrowStyle}>What&apos;s new beyond your document</div>
      <div style={proseStyle}>{noveltyDelta}</div>
    </div>
  );
}

/* ---- A2 — Built from your document ---- */
function ExcerptItem({ excerpt, index }: { excerpt: GroundedExcerpt; index: number }) {
  const heading = excerpt.sectionLabel && excerpt.sectionLabel.trim() !== ''
    ? excerpt.sectionLabel
    : `Source passage ${index + 1}`;
  return (
    <div style={{ marginBottom: 16 }}>
      <div style={{ fontSize: 'var(--text-sm)', fontWeight: 600, color: 'var(--text-primary)', marginBottom: 8 }}>{heading}</div>
      <ExcerptBlockquote text={excerpt.text} />
    </div>
  );
}

function ChunkIdDisclosure({ chunkIds }: { chunkIds: string[] }) {
  if (chunkIds.length === 0) return null;
  return (
    <TechnicalDisclosure summaryLabel="Source chunk IDs">
      <dl style={{ display: 'grid', gridTemplateColumns: '150px 1fr', gap: '12px 20px', margin: 0, marginTop: 12 }}>
        {chunkIds.map((id, i) => (
          <div key={`${id}-${i}`} style={{ display: 'contents' }}>
            <dt style={{ fontSize: 12.5, color: 'var(--text-muted)', alignSelf: 'center' }}>Chunk {i + 1}</dt>
            <dd style={{ margin: 0, alignSelf: 'center' }}>
              <CopyChip
                displayText={`${id.slice(0, 12)}${id.length > 12 ? '…' : ''}`}
                fullText={id}
                title={id}
                ariaLabel={`Chunk ID ${id.slice(0, 12)}, click to copy full ID`}
              />
            </dd>
          </div>
        ))}
      </dl>
    </TechnicalDisclosure>
  );
}

export function BuiltFromDocumentCard({ groundedIn }: { groundedIn?: GroundedIn }) {
  const excerpts = groundedIn?.excerpts ?? [];
  const chunkIds = groundedIn?.chunkIds ?? [];
  return (
    <div style={cardStyle}>
      <div style={sectionTitle}>Built from your document</div>
      <div style={subtitleStyle}>The passages in your asset this idea was generated from.</div>
      {excerpts.length === 0 ? (
        <div style={dashedEmptyStyle}>No source passages were recorded for this opportunity.</div>
      ) : (
        <>
          {excerpts.map((e, i) => <ExcerptItem key={`${e.chunkId}-${i}`} excerpt={e} index={i} />)}
          <ChunkIdDisclosure chunkIds={chunkIds} />
        </>
      )}
    </div>
  );
}

/* ---- A3 — Prior-art proximity ---- */
const SOURCE_KIND_DOT: Record<'live' | 'corpus', string> = {
  live: '#3e8bff',
  corpus: '#a46bff',
};

function relevancePct(score: number): number {
  return Math.round(score * 100);
}

export function PriorArtRow({ item }: { item: PriorArtItem }) {
  const dotColor = item.isCorpus ? SOURCE_KIND_DOT.corpus : SOURCE_KIND_DOT.live;
  const label = item.reference || item.title;
  const pct = relevancePct(item.relevanceScore);
  const dot = <span style={{ width: 8, height: 8, borderRadius: '50%', background: dotColor, flexShrink: 0 }} aria-hidden="true" />;
  const relevanceBadge = <Badge tone="neutral">{pct}%</Badge>;

  const rowStyle: CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 10,
    padding: '12px 0',
    borderBottom: '1px solid var(--border-subtle)',
  };

  if (!item.isCorpus && item.url) {
    return (
      <div style={rowStyle}>
        {dot}
        <a
          href={item.url}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={`Open ${label} (live patent match, relevance ${pct}%, opens in new tab)`}
          style={{
            flex: 1,
            minWidth: 0,
            display: 'inline-flex',
            alignItems: 'center',
            gap: 6,
            minHeight: 44,
            color: 'var(--text-primary)',
            fontSize: 13.5,
            fontWeight: 500,
            textDecoration: 'none',
            outline: 'none',
          }}
          onFocus={(e) => {
            e.currentTarget.style.outline = '2px solid var(--accent-primary)';
            e.currentTarget.style.outlineOffset = '2px';
          }}
          onBlur={(e) => {
            e.currentTarget.style.outline = 'none';
          }}
        >
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{item.title || item.reference}</span>
          <span aria-hidden="true" style={{ fontWeight: 600 }}>↗</span>
        </a>
        {relevanceBadge}
      </div>
    );
  }

  return (
    <div style={rowStyle}>
      {dot}
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 13.5, color: 'var(--text-primary)', fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {item.title || item.reference}
        </div>
        <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', fontStyle: 'italic' }}>corpus match — no external link</span>
      </div>
      {relevanceBadge}
    </div>
  );
}

function priorArtEmptyMessage(landscapeCorpusOnly?: boolean): string {
  if (landscapeCorpusOnly) {
    return 'Prior-art landscape was corpus-only for this batch — no live patent matches were retrieved.';
  }
  return 'No close prior art was found for this concept.';
}

export function PriorArtProximityCard({ items, landscapeCorpusOnly }: { items?: PriorArtItem[]; landscapeCorpusOnly?: boolean }) {
  const list = items ?? [];
  return (
    <div style={cardStyle}>
      <div style={sectionTitle}>Closest prior art</div>
      <div style={subtitleStyle}>The nearest existing patents and corpus documents this idea steers around.</div>
      {list.length === 0 ? (
        <div style={mutedItalic}>{priorArtEmptyMessage(landscapeCorpusOnly)}</div>
      ) : (
        <div>
          {list.map((item, i) => <PriorArtRow key={`${item.reference}-${i}`} item={item} />)}
        </div>
      )}
    </div>
  );
}

/* ---- A5 — Roadmap alignment ---- */
export function RoadmapAlignmentCard({ roadmapAlignment }: { roadmapAlignment?: string }) {
  const hasRoadmap = !!roadmapAlignment && roadmapAlignment.trim() !== '';
  return (
    <div style={cardStyle}>
      <div style={eyebrowStyle}>Roadmap alignment</div>
      {hasRoadmap ? (
        <div style={proseStyle}>{roadmapAlignment}</div>
      ) : (
        <div style={mutedItalic}>No roadmap was provided for this batch, so alignment wasn&apos;t assessed.</div>
      )}
    </div>
  );
}

export function SeedingDetailSections({ detail }: { detail: OpportunityDetail }) {
  return (
    <>
      <NoveltyDeltaCard noveltyDelta={detail.noveltyDelta} />
      <BuiltFromDocumentCard groundedIn={detail.groundedIn} />
      <PriorArtProximityCard items={detail.priorArtProximity} landscapeCorpusOnly={detail.landscapeCorpusOnly} />
    </>
  );
}
