import type { CSSProperties } from 'react';
import { recommendationLabel } from '../../shared/ds/badges';
import type { HighlightRect, PageDimension } from './opportunityTypes';
import { normalizeRect, verdictColor } from './provenanceHelpers';

type VerdictBorderStyle = 'solid' | 'dashed' | 'dotted';

const BORDER_STYLE_BY_RECOMMENDATION: Record<string, VerdictBorderStyle> = {
  pursue: 'solid',
  investigate: 'dashed',
  review: 'dashed',
  hold: 'dashed',
  abandon: 'dotted',
  reject: 'dotted',
};

function verdictBorderStyle(recommendation: string): VerdictBorderStyle {
  return BORDER_STYLE_BY_RECOMMENDATION[recommendation.toLowerCase()] ?? 'solid';
}

const visuallyHiddenStyle: CSSProperties = {
  position: 'absolute',
  width: 1,
  height: 1,
  padding: 0,
  margin: -1,
  overflow: 'hidden',
  clip: 'rect(0, 0, 0, 0)',
  whiteSpace: 'nowrap',
  border: 0,
};

interface RectBoxProps {
  rect: HighlightRect;
  pageDim?: PageDimension | null;
  renderedWidth: number;
  renderedHeight: number;
  fg: string;
  bg: string;
  borderStyle: VerdictBorderStyle;
  showLabel: boolean;
  label: string;
}

function RectBox({ rect, pageDim, renderedWidth, renderedHeight, fg, bg, borderStyle, showLabel, label }: RectBoxProps) {
  const norm = normalizeRect(rect, pageDim);
  const left = norm.x0 * renderedWidth;
  const top = norm.top * renderedHeight;
  const width = (norm.x1 - norm.x0) * renderedWidth;
  const height = (norm.bottom - norm.top) * renderedHeight;

  return (
    <div
      aria-hidden="true"
      style={{
        position: 'absolute',
        left,
        top,
        width,
        height,
        background: bg,
        border: `2px ${borderStyle} ${fg}`,
        borderRadius: 2,
      }}
    >
      {showLabel && (
        <span
          style={{
            position: 'absolute',
            top: -20,
            left: 0,
            fontSize: 11,
            fontWeight: 700,
            color: fg,
            background: 'var(--surface-card)',
            padding: '1px 6px',
            borderRadius: 'var(--radius-sm)',
            whiteSpace: 'nowrap',
          }}
        >
          {label}
        </span>
      )}
    </div>
  );
}

export interface HighlightOverlayProps {
  rects: HighlightRect[];
  pageNumber: number;
  pageDim?: PageDimension | null;
  renderedWidth: number;
  renderedHeight: number;
  recommendation: string;
}

export function HighlightOverlay({ rects, pageNumber, pageDim, renderedWidth, renderedHeight, recommendation }: HighlightOverlayProps) {
  const pageRects = rects.filter((r) => r.pageNumber === pageNumber);
  if (pageRects.length === 0 || renderedWidth <= 0 || renderedHeight <= 0) return null;

  const { fg, bg } = verdictColor(recommendation);
  const borderStyle = verdictBorderStyle(recommendation);
  const label = recommendationLabel(recommendation);

  return (
    <div style={{ position: 'absolute', inset: 0, pointerEvents: 'none' }}>
      <span role="img" aria-label={`Cited region on page ${pageNumber}, verdict: ${recommendation.toLowerCase()}`} style={visuallyHiddenStyle} />
      {pageRects.map((rect, idx) => (
        <RectBox
          key={`${rect.pageNumber}-${rect.x0}-${rect.top}-${idx}`}
          rect={rect}
          pageDim={pageDim}
          renderedWidth={renderedWidth}
          renderedHeight={renderedHeight}
          fg={fg}
          bg={bg}
          borderStyle={borderStyle}
          showLabel={idx === 0}
          label={label}
        />
      ))}
    </div>
  );
}
