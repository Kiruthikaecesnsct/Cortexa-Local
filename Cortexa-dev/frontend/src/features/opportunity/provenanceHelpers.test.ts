import { describe, it, expect } from 'vitest';
import { extensionToLanguage, normalizeRect, resolveExcerptView, verdictColor } from './provenanceHelpers';

type FieldState = 'value' | 'not_applicable' | 'unavailable';

function provFieldState(value: unknown, appliesToSource: boolean): FieldState {
  if (value != null && value !== '') return 'value';
  if (!appliesToSource) return 'not_applicable';
  return 'unavailable';
}

describe('provFieldState — BUG167 honest "Not captured" logic', () => {
  it('returns "value" when value is present', () => {
    expect(provFieldState('Paper', true)).toBe('value');
    expect(provFieldState(7, true)).toBe('value');
    expect(provFieldState(0, true)).toBe('value');
    expect(provFieldState(false, true)).toBe('value');
  });

  it('returns "not_applicable" when value absent and does not apply to source', () => {
    expect(provFieldState(null, false)).toBe('not_applicable');
    expect(provFieldState(undefined, false)).toBe('not_applicable');
    expect(provFieldState('', false)).toBe('not_applicable');
  });

  it('returns "unavailable" when value absent but should apply to source', () => {
    expect(provFieldState(null, true)).toBe('unavailable');
    expect(provFieldState(undefined, true)).toBe('unavailable');
    expect(provFieldState('', true)).toBe('unavailable');
  });

  it('never fabricates a value — null stays null', () => {
    const pageNumber = null;
    const state = provFieldState(pageNumber, true);
    expect(state).toBe('unavailable');
    expect(pageNumber).toBeNull();
  });

  it('treats zero as a valid value, never as missing', () => {
    expect(provFieldState(0, true)).toBe('value');
  });

  it('distinguishes page not-applicable (Code) from page unavailable (Paper with missing page)', () => {
    const pageForCode = null;
    const pageForPaper = null;
    const codeApplies = false;
    const paperApplies = true;

    expect(provFieldState(pageForCode, codeApplies)).toBe('not_applicable');
    expect(provFieldState(pageForPaper, paperApplies)).toBe('unavailable');
  });
});

describe('verdictColor', () => {
  it('resolves pursue to the success token pair', () => {
    expect(verdictColor('pursue')).toEqual({ fg: 'var(--status-success-fg)', bg: 'var(--status-success-bg)' });
  });

  it('resolves investigate/review/hold to the warning token pair', () => {
    expect(verdictColor('investigate')).toEqual({ fg: 'var(--status-warning-fg)', bg: 'var(--status-warning-bg)' });
    expect(verdictColor('review')).toEqual({ fg: 'var(--status-warning-fg)', bg: 'var(--status-warning-bg)' });
    expect(verdictColor('hold')).toEqual({ fg: 'var(--status-warning-fg)', bg: 'var(--status-warning-bg)' });
  });

  it('resolves abandon to the danger token pair', () => {
    expect(verdictColor('abandon')).toEqual({ fg: 'var(--status-danger-fg)', bg: 'var(--status-danger-bg)' });
  });

  it('falls back to neutral for an unrecognized recommendation', () => {
    expect(verdictColor('unknown')).toEqual({ fg: 'var(--text-body)', bg: 'var(--gray-100)' });
  });
});

describe('normalizeRect — highlight_rects arrive already normalized 0..1', () => {
  it('clamps an already-normalized rect and passes it through unchanged', () => {
    const rect = { pageNumber: 1, x0: 0.1, x1: 0.5, top: 0.2, bottom: 0.4 };
    expect(normalizeRect(rect)).toEqual({ pageNumber: 1, x0: 0.1, x1: 0.5, top: 0.2, bottom: 0.4 });
  });

  it('clamps out-of-range normalized values into 0..1 without page dimensions', () => {
    const rect = { pageNumber: 1, x0: -0.2, x1: 1.4, top: 0, bottom: 1 };
    expect(normalizeRect(rect)).toEqual({ pageNumber: 1, x0: 0, x1: 1, top: 0, bottom: 1 });
  });

  it('falls back to dividing by page dimensions when a rect arrives in absolute PDF points', () => {
    const rect = { pageNumber: 1, x0: 61.2, x1: 306, top: 100, bottom: 150 };
    const pageDim = { pageNumber: 1, width: 612, height: 792 };
    const result = normalizeRect(rect, pageDim);
    expect(result.x0).toBeCloseTo(0.1);
    expect(result.x1).toBeCloseTo(0.5);
    expect(result.top).toBeCloseTo(100 / 792);
    expect(result.bottom).toBeCloseTo(150 / 792);
  });
});

describe('extensionToLanguage — US125 code viewer language detection', () => {
  it('detects common ingested-repo languages from the file extension', () => {
    expect(extensionToLanguage('services/ingestion/main.py')).toEqual({ shikiLang: 'python', displayName: 'Python' });
    expect(extensionToLanguage('frontend/src/App.tsx')).toEqual({ shikiLang: 'tsx', displayName: 'TypeScript' });
    expect(extensionToLanguage('src/index.ts')).toEqual({ shikiLang: 'typescript', displayName: 'TypeScript' });
    expect(extensionToLanguage('src/Program.cs')).toEqual({ shikiLang: 'csharp', displayName: 'C#' });
    expect(extensionToLanguage('src/lib.rs')).toEqual({ shikiLang: 'rust', displayName: 'Rust' });
    expect(extensionToLanguage('Main.java')).toEqual({ shikiLang: 'java', displayName: 'Java' });
    expect(extensionToLanguage('cmd/server/main.go')).toEqual({ shikiLang: 'go', displayName: 'Go' });
    expect(extensionToLanguage('deploy/setup.sh')).toEqual({ shikiLang: 'bash', displayName: 'Shell' });
  });

  it('is case-insensitive on the extension', () => {
    expect(extensionToLanguage('README.PY')).toEqual({ shikiLang: 'python', displayName: 'Python' });
  });

  it('returns null for an unknown or missing extension, without throwing', () => {
    expect(extensionToLanguage('Makefile')).toBeNull();
    expect(extensionToLanguage('data.unknownext')).toBeNull();
    expect(extensionToLanguage(null)).toBeNull();
    expect(extensionToLanguage(undefined)).toBeNull();
    expect(extensionToLanguage('')).toBeNull();
  });
});

describe('resolveExcerptView — US125 excerpt readability fix', () => {
  it('prose consumes cleanExcerpt, wraps normally, never mono', () => {
    const view = resolveExcerptView({ sourceKind: 'Paper', excerptText: 'raw\ntext', cleanExcerpt: 'clean readable text' });
    expect(view).toEqual({ text: 'clean readable text', mono: false });
  });

  it('prose falls back to excerptText when cleanExcerpt is absent (legacy batches)', () => {
    const view = resolveExcerptView({ sourceKind: 'Paper', excerptText: 'raw fallback text', cleanExcerpt: null });
    expect(view).toEqual({ text: 'raw fallback text', mono: false });
  });

  it('code always uses the raw excerptText and is mono, even when cleanExcerpt is present', () => {
    const view = resolveExcerptView({ sourceKind: 'Code', excerptText: 'def f():\n    return 1', cleanExcerpt: 'def f(): return 1' });
    expect(view).toEqual({ text: 'def f():\n    return 1', mono: true });
  });

  it('returns an empty string when no excerpt field is present at all', () => {
    const view = resolveExcerptView({ sourceKind: 'Paper', excerptText: null, cleanExcerpt: null });
    expect(view).toEqual({ text: '', mono: false });
  });
});
