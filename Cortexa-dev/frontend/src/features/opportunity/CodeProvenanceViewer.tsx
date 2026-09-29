import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import type { HighlighterCore, ThemedToken } from 'shiki/core';
import { Badge } from '../../shared/ds';
import { extensionToLanguage } from './provenanceHelpers';
import { NoExcerptNote } from './OpportunityDetailComponents';
import type { LineRange, Provenance } from './opportunityTypes';

// Isolated from the rest of the bundle by design (matches PdfProvenanceViewer's
// pdfjs isolation, US124): shiki, its engine, and its per-language grammars are
// only ever imported inside this lazily-loaded module.
const LANGUAGE_LOADERS: Record<string, () => Promise<unknown>> = {
  python: () => import('@shikijs/langs/python'),
  typescript: () => import('@shikijs/langs/typescript'),
  tsx: () => import('@shikijs/langs/tsx'),
  javascript: () => import('@shikijs/langs/javascript'),
  jsx: () => import('@shikijs/langs/jsx'),
  csharp: () => import('@shikijs/langs/csharp'),
  rust: () => import('@shikijs/langs/rust'),
  java: () => import('@shikijs/langs/java'),
  go: () => import('@shikijs/langs/go'),
  c: () => import('@shikijs/langs/c'),
  cpp: () => import('@shikijs/langs/cpp'),
  ruby: () => import('@shikijs/langs/ruby'),
  php: () => import('@shikijs/langs/php'),
  bash: () => import('@shikijs/langs/bash'),
  json: () => import('@shikijs/langs/json'),
  yaml: () => import('@shikijs/langs/yaml'),
  sql: () => import('@shikijs/langs/sql'),
  markdown: () => import('@shikijs/langs/markdown'),
  html: () => import('@shikijs/langs/html'),
  css: () => import('@shikijs/langs/css'),
};

const TOKENIZER_THEME = 'min-light';

let highlighterPromise: Promise<HighlighterCore> | null = null;
const loadedLangs = new Set<string>();

async function getHighlighter(): Promise<HighlighterCore> {
  if (!highlighterPromise) {
    highlighterPromise = (async () => {
      const [{ createHighlighterCore }, { createJavaScriptRegexEngine }, theme] = await Promise.all([
        import('shiki/core'),
        import('shiki/engine/javascript'),
        import('@shikijs/themes/min-light'),
      ]);
      return createHighlighterCore({
        themes: [theme.default],
        langs: [],
        engine: createJavaScriptRegexEngine(),
      });
    })();
  }
  return highlighterPromise;
}

async function ensureLanguageLoaded(highlighter: HighlighterCore, shikiLang: string): Promise<void> {
  if (loadedLangs.has(shikiLang)) return;
  const loader = LANGUAGE_LOADERS[shikiLang];
  if (!loader) throw new Error(`No grammar bundled for language "${shikiLang}"`);
  const grammar = await loader();
  await highlighter.loadLanguage(grammar as never);
  loadedLangs.add(shikiLang);
}

type SyntaxRole = 'keyword' | 'string' | 'number' | 'comment' | 'function' | 'type' | 'punctuation' | 'plain';

const ROLE_PATTERNS: Array<{ role: SyntaxRole; test: RegExp }> = [
  { role: 'comment', test: /^comment\./ },
  { role: 'string', test: /^(string|constant\.character)\./ },
  { role: 'number', test: /^constant\.numeric\./ },
  { role: 'keyword', test: /^(keyword|storage\.(type|modifier)|constant\.language)\b/ },
  { role: 'function', test: /^(entity\.name\.function|support\.function|meta\.function-call)\b/ },
  { role: 'type', test: /^(entity\.name\.(class|type)|support\.type|storage\.type\.|entity\.other\.inherited-class)\b/ },
  { role: 'punctuation', test: /^(punctuation\.|meta\.brace)/ },
];

function classifyToken(token: ThemedToken): SyntaxRole {
  if (token.type === 1) return 'comment';
  if (token.type === 2 || token.type === 3) return 'string';
  const scopeNames = (token.explanation ?? []).flatMap((e) => e.scopes.map((s) => s.scopeName));
  for (const { role, test } of ROLE_PATTERNS) {
    if (scopeNames.some((name) => test.test(name))) return role;
  }
  return 'plain';
}

interface CodeLine {
  lineNumber: number;
  tokens: Array<{ text: string; role: SyntaxRole }>;
}

function toPlainLines(code: string): CodeLine[] {
  return code.split('\n').map((text, idx) => ({ lineNumber: idx, tokens: [{ text, role: 'plain' as const }] }));
}

function tokensToLines(tokenLines: ThemedToken[][]): CodeLine[] {
  return tokenLines.map((line, idx) => ({
    lineNumber: idx,
    tokens: line.map((t) => ({ text: t.content, role: classifyToken(t) })),
  }));
}

type HighlightStatus = 'plain' | 'loading' | 'highlighted';

function useHighlightedLines(code: string, shikiLang: string | null): { lines: CodeLine[]; status: HighlightStatus } {
  const [state, setState] = useState<{ lines: CodeLine[]; status: HighlightStatus }>(() => ({
    lines: toPlainLines(code),
    status: shikiLang ? 'loading' : 'plain',
  }));

  useEffect(() => {
    if (!shikiLang) {
      setState({ lines: toPlainLines(code), status: 'plain' });
      return;
    }
    let cancelled = false;
    setState({ lines: toPlainLines(code), status: 'loading' });
    (async () => {
      try {
        const highlighter = await getHighlighter();
        await ensureLanguageLoaded(highlighter, shikiLang);
        if (cancelled) return;
        const tokenLines = highlighter.codeToTokensBase(code, { lang: shikiLang, theme: TOKENIZER_THEME, includeExplanation: true });
        if (cancelled) return;
        setState({ lines: tokensToLines(tokenLines), status: 'highlighted' });
      } catch {
        if (!cancelled) setState({ lines: toPlainLines(code), status: 'plain' });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [code, shikiLang]);

  return state;
}

const HEADER_STYLE: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 'var(--space-3)',
  padding: 'var(--space-3) var(--space-4)',
  background: 'var(--surface-card-alt)',
  border: '1px solid var(--border-subtle)',
  borderBottom: 'none',
  borderTopLeftRadius: 'var(--radius-md)',
  borderTopRightRadius: 'var(--radius-md)',
};

function CodeFileIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" style={{ flexShrink: 0 }}>
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <path d="M14 2v6h6" />
      <path d="M10 13l-2 2 2 2M14 13l2 2-2 2" />
    </svg>
  );
}

function CodeViewerHeader({ filePath, languageLabel }: { filePath: string; languageLabel: string }) {
  return (
    <div style={HEADER_STYLE}>
      <span
        title={filePath}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 6,
          minWidth: 0,
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          direction: 'rtl',
          textAlign: 'left',
          fontFamily: 'var(--font-mono)',
          fontSize: 'var(--text-xs)',
          color: 'var(--text-body)',
        }}
      >
        <CodeFileIcon />
        <bdi>{filePath}</bdi>
      </span>
      <Badge tone="neutral">{languageLabel}</Badge>
    </div>
  );
}

function DegradeNote({ children }: { children: ReactNode }) {
  return (
    <div style={{ padding: '6px var(--space-4)', fontSize: 'var(--text-xs)', color: 'var(--text-muted)', fontFamily: 'var(--font-body)', background: 'var(--surface-card-alt)', borderLeft: '1px solid var(--border-subtle)', borderRight: '1px solid var(--border-subtle)' }}>
      {children}
    </div>
  );
}

const STAGE_CSS = `
  .code-provenance-stage:focus-visible {
    outline: 2px solid var(--accent-primary);
    outline-offset: 2px;
  }
`;

interface CodeRowProps {
  line: CodeLine;
  displayNumber: number | null;
  isCited: boolean;
  rowRef?: (el: HTMLDivElement | null) => void;
}

function CodeRow({ line, displayNumber, isCited, rowRef }: CodeRowProps) {
  return (
    <div
      ref={rowRef}
      style={{
        display: 'grid',
        gridTemplateColumns: '3em 1fr',
        minWidth: 'max-content',
        background: isCited ? 'var(--accent-primary-subtle)' : 'transparent',
        borderLeft: isCited ? '2px solid var(--accent-primary)' : '2px solid transparent',
      }}
    >
      <span
        aria-hidden="true"
        style={{
          textAlign: 'right',
          paddingRight: 'var(--space-3)',
          borderRight: '1px solid var(--border-subtle)',
          color: isCited ? 'var(--accent-primary)' : 'var(--text-muted)',
          fontWeight: isCited ? 'var(--weight-semibold)' : 'var(--weight-regular)',
          userSelect: 'none',
        }}
      >
        {displayNumber ?? ''}
      </span>
      <span style={{ paddingLeft: 'var(--space-3)', whiteSpace: 'pre' }}>
        {line.tokens.map((tok, idx) => (
          <span key={idx} style={{ color: `var(--syntax-${tok.role})` }}>
            {tok.text}
          </span>
        ))}
        {line.tokens.length === 0 ? ' ' : null}
      </span>
    </div>
  );
}

function isLineCited(lineNumber: number, gutterStart: number, lineRange: LineRange | null): boolean {
  if (!lineRange) return false;
  const absoluteLine = gutterStart + lineNumber;
  return absoluteLine >= lineRange.startLine && absoluteLine <= lineRange.endLine;
}

interface CodeStageProps {
  filePath: string;
  lines: CodeLine[];
  lineRange: LineRange | null;
  hasLineRange: boolean;
}

function CodeStage({ filePath, lines, lineRange, hasLineRange }: CodeStageProps) {
  const stageRef = useRef<HTMLDivElement>(null);
  const firstCitedRowRef = useRef<HTMLDivElement | null>(null);
  const scrolledRef = useRef(false);

  const gutterStart = hasLineRange ? lineRange!.startLine : 1;

  useEffect(() => {
    if (scrolledRef.current) return;
    if (!firstCitedRowRef.current || !stageRef.current) return;
    scrolledRef.current = true;
    const lead = 16;
    stageRef.current.scrollTop = Math.max(0, firstCitedRowRef.current.offsetTop - lead);
  }, [lines]);

  const rangeLabel = hasLineRange
    ? `cited lines ${lineRange!.startLine} to ${lineRange!.endLine}`
    : 'line numbers unavailable';

  let firstCitedAssigned = false;

  return (
    <div
      ref={stageRef}
      className="code-provenance-stage"
      role="region"
      tabIndex={0}
      aria-label={`Source code, ${filePath}, ${rangeLabel}`}
      style={{
        maxHeight: 420,
        overflow: 'auto',
        background: 'var(--surface-sunken)',
        border: '1px solid var(--border-subtle)',
        borderBottomLeftRadius: 'var(--radius-md)',
        borderBottomRightRadius: 'var(--radius-md)',
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--text-sm)',
        lineHeight: 1.6,
        color: 'var(--syntax-plain)',
      }}
    >
      <style>{STAGE_CSS}</style>
      {hasLineRange && (
        <span style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0,0,0,0)' }}>
          {`Cited lines ${lineRange!.startLine} to ${lineRange!.endLine}`}
        </span>
      )}
      {lines.map((line) => {
        const displayNumber = gutterStart + line.lineNumber;
        const cited = isLineCited(line.lineNumber, gutterStart, hasLineRange ? lineRange : null);
        let assignRef: ((el: HTMLDivElement | null) => void) | undefined;
        if (cited && !firstCitedAssigned) {
          firstCitedAssigned = true;
          assignRef = (el) => {
            firstCitedRowRef.current = el;
          };
        }
        return (
          <CodeRow key={line.lineNumber} line={line} displayNumber={displayNumber} isCited={cited} rowRef={assignRef} />
        );
      })}
    </div>
  );
}

function degradeMessage(lineRange: LineRange, renderedLineCount: number): string | null {
  const citedCount = lineRange.endLine - lineRange.startLine + 1;
  if (renderedLineCount >= citedCount) return null;
  const shownEnd = lineRange.startLine + renderedLineCount - 1;
  return `Showing lines ${lineRange.startLine}–${shownEnd} of cited ${lineRange.startLine}–${lineRange.endLine}.`;
}

export interface CodeProvenanceViewerProps {
  provenance: Provenance;
}

export function CodeProvenanceViewer({ provenance }: CodeProvenanceViewerProps) {
  const filePath = provenance.filePath ?? null;
  const excerptText = provenance.excerptText ?? '';
  const hasExcerpt = excerptText.trim().length > 0;
  const lineRange = provenance.lineRange ?? null;
  const detected = useMemo(() => extensionToLanguage(filePath), [filePath]);
  const { lines, status } = useHighlightedLines(hasExcerpt ? excerptText : '', detected?.shikiLang ?? null);

  const languageLabel = status === 'highlighted' ? detected!.displayName : 'Plain text';

  if (!hasExcerpt) {
    return (
      <div>
        {filePath && <CodeViewerHeader filePath={filePath} languageLabel={languageLabel} />}
        <div style={{ marginTop: filePath ? 'var(--space-4)' : 0 }}>
          <NoExcerptNote />
        </div>
      </div>
    );
  }

  const degradeText = lineRange ? degradeMessage(lineRange, lines.length) : null;

  return (
    <div>
      {filePath && <CodeViewerHeader filePath={filePath} languageLabel={languageLabel} />}
      {!lineRange && (
        <DegradeNote>Line numbers unavailable.</DegradeNote>
      )}
      {degradeText && <DegradeNote>{degradeText}</DegradeNote>}
      <CodeStage filePath={filePath ?? 'source'} lines={lines} lineRange={lineRange} hasLineRange={!!lineRange} />
    </div>
  );
}

export default CodeProvenanceViewer;
