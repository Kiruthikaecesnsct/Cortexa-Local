import type { HighlightRect, PageDimension, Provenance, Recommendation } from './opportunityTypes';

export interface VerdictColor {
  fg: string;
  bg: string;
}

const SUCCESS_COLOR: VerdictColor = { fg: 'var(--status-success-fg)', bg: 'var(--status-success-bg)' };
const WARNING_COLOR: VerdictColor = { fg: 'var(--status-warning-fg)', bg: 'var(--status-warning-bg)' };
const DANGER_COLOR: VerdictColor = { fg: 'var(--status-danger-fg)', bg: 'var(--status-danger-bg)' };
const NEUTRAL_COLOR: VerdictColor = { fg: 'var(--text-body)', bg: 'var(--gray-100)' };

const VERDICT_COLOR_BY_RECOMMENDATION: Record<string, VerdictColor> = {
  pursue: SUCCESS_COLOR,
  investigate: WARNING_COLOR,
  review: WARNING_COLOR,
  hold: WARNING_COLOR,
  abandon: DANGER_COLOR,
  reject: DANGER_COLOR,
};

export function verdictColor(recommendation: Recommendation | string): VerdictColor {
  return VERDICT_COLOR_BY_RECOMMENDATION[recommendation.toLowerCase()] ?? NEUTRAL_COLOR;
}

export interface NormalizedRect {
  pageNumber: number;
  x0: number;
  x1: number;
  top: number;
  bottom: number;
}

function clamp01(value: number): number {
  if (value < 0) return 0;
  if (value > 1) return 1;
  return value;
}

// The ingestion service (US122 word_mapper.py + pdf_parser.py) already divides
// every word rect by its page width/height and clamps to 0..1 before it ever
// reaches the wire — highlight_rects arrive pre-normalized. This function stays
// defensive: if a rect ever arrives with a coordinate outside 0..1 (e.g. a future
// source emitting absolute PDF points), it falls back to dividing by the matching
// page's dimensions before clamping.
export function normalizeRect(rect: HighlightRect, pageDim?: PageDimension | null): NormalizedRect {
  const isAbsolute = rect.x1 > 1 || rect.bottom > 1;
  if (!isAbsolute || !pageDim || pageDim.width <= 0 || pageDim.height <= 0) {
    return {
      pageNumber: rect.pageNumber,
      x0: clamp01(rect.x0),
      x1: clamp01(rect.x1),
      top: clamp01(rect.top),
      bottom: clamp01(rect.bottom),
    };
  }
  return {
    pageNumber: rect.pageNumber,
    x0: clamp01(rect.x0 / pageDim.width),
    x1: clamp01(rect.x1 / pageDim.width),
    top: clamp01(rect.top / pageDim.height),
    bottom: clamp01(rect.bottom / pageDim.height),
  };
}

export interface DetectedLanguage {
  shikiLang: string;
  displayName: string;
}

// Coverage per US125 handoff §3 — the common languages present in ingested
// repos. Extensions map to a Shiki grammar id + a human display name for the
// header badge. Unknown extensions return null so the viewer falls back to
// the plain-text state honestly rather than guessing.
const EXTENSION_LANGUAGE: Record<string, DetectedLanguage> = {
  py: { shikiLang: 'python', displayName: 'Python' },
  ts: { shikiLang: 'typescript', displayName: 'TypeScript' },
  tsx: { shikiLang: 'tsx', displayName: 'TypeScript' },
  js: { shikiLang: 'javascript', displayName: 'JavaScript' },
  mjs: { shikiLang: 'javascript', displayName: 'JavaScript' },
  cjs: { shikiLang: 'javascript', displayName: 'JavaScript' },
  jsx: { shikiLang: 'jsx', displayName: 'JavaScript' },
  cs: { shikiLang: 'csharp', displayName: 'C#' },
  rs: { shikiLang: 'rust', displayName: 'Rust' },
  java: { shikiLang: 'java', displayName: 'Java' },
  go: { shikiLang: 'go', displayName: 'Go' },
  c: { shikiLang: 'c', displayName: 'C' },
  h: { shikiLang: 'c', displayName: 'C' },
  cpp: { shikiLang: 'cpp', displayName: 'C++' },
  cc: { shikiLang: 'cpp', displayName: 'C++' },
  cxx: { shikiLang: 'cpp', displayName: 'C++' },
  hpp: { shikiLang: 'cpp', displayName: 'C++' },
  hh: { shikiLang: 'cpp', displayName: 'C++' },
  hxx: { shikiLang: 'cpp', displayName: 'C++' },
  rb: { shikiLang: 'ruby', displayName: 'Ruby' },
  php: { shikiLang: 'php', displayName: 'PHP' },
  sh: { shikiLang: 'bash', displayName: 'Shell' },
  bash: { shikiLang: 'bash', displayName: 'Shell' },
  zsh: { shikiLang: 'bash', displayName: 'Shell' },
  json: { shikiLang: 'json', displayName: 'JSON' },
  yaml: { shikiLang: 'yaml', displayName: 'YAML' },
  yml: { shikiLang: 'yaml', displayName: 'YAML' },
  sql: { shikiLang: 'sql', displayName: 'SQL' },
  md: { shikiLang: 'markdown', displayName: 'Markdown' },
  markdown: { shikiLang: 'markdown', displayName: 'Markdown' },
  html: { shikiLang: 'html', displayName: 'HTML' },
  htm: { shikiLang: 'html', displayName: 'HTML' },
  css: { shikiLang: 'css', displayName: 'CSS' },
};

export function extensionToLanguage(filePath?: string | null): DetectedLanguage | null {
  if (!filePath) return null;
  const match = /\.([a-zA-Z0-9]+)$/.exec(filePath);
  if (!match) return null;
  const ext = (match[1] ?? '').toLowerCase();
  return EXTENSION_LANGUAGE[ext] ?? null;
}

export interface ExcerptView {
  text: string;
  mono: boolean;
}

// US125 excerpt readability fix — prose consumes the whitespace-cleaned field
// (falling back to raw only for legacy batches that lack it) and wraps as
// normal text; code keeps the raw excerpt so indentation/line breaks survive,
// and is never handed cleanExcerpt (the server collapses newlines in it).
export function resolveExcerptView(provenance: Pick<Provenance, 'sourceKind' | 'excerptText' | 'cleanExcerpt'>): ExcerptView {
  const isCode = provenance.sourceKind === 'Code';
  if (isCode) {
    return { text: provenance.excerptText ?? '', mono: true };
  }
  return { text: provenance.cleanExcerpt ?? provenance.excerptText ?? '', mono: false };
}
