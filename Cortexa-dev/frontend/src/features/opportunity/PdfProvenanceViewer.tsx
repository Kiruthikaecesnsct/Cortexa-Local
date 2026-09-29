import { useCallback, useEffect, useMemo, useRef, useState, type RefObject } from 'react';
import { GlobalWorkerOptions, getDocument, type PDFDocumentProxy } from 'pdfjs-dist';
import workerSrc from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import { ErrorState, Skeleton, Spinner } from '../../shared/ds';
import { IconChevronRight, IconMinus } from '../../shared/ds/icons';
import { recommendationLabel } from '../../shared/ds/badges';
import { fetchDocumentContent } from './opportunityRepository';
import { HighlightOverlay } from './HighlightOverlay';
import { verdictColor } from './provenanceHelpers';
import type { OpportunityDetailErrorKind, Provenance } from './opportunityTypes';

GlobalWorkerOptions.workerSrc = workerSrc;

const ZOOM_MIN = 50;
const ZOOM_MAX = 300;
const ZOOM_STEP = 25;
const ZOOM_DEFAULT = 100;
const DEFAULT_STAGE_WIDTH = 760;
const STAGE_MAX_HEIGHT = 640;
const IN_VIEW_ROOT_MARGIN = '200px';

type FetchStatus = 'idle' | 'loading' | 'ready' | 'forbidden' | 'not-found' | 'error';
type Phase = 'not-in-view' | 'loading' | 'ready' | 'forbidden' | 'not-found' | 'error';

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function useInView(ref: RefObject<HTMLElement | null>, rootMargin: string): boolean {
  const [inView, setInView] = useState(() => typeof IntersectionObserver === 'undefined');

  useEffect(() => {
    if (inView) return;
    const el = ref.current;
    if (!el) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setInView(true);
          observer.disconnect();
        }
      },
      { rootMargin }
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, [inView, ref, rootMargin]);

  return inView;
}

function mapErrorKindToStatus(kind: OpportunityDetailErrorKind): FetchStatus {
  if (kind === 'forbidden') return 'forbidden';
  if (kind === 'not_found') return 'not-found';
  return 'error';
}

interface DocumentFetchState {
  status: FetchStatus;
  bytes: ArrayBuffer | null;
  message: string;
  correlationId?: string;
}

function useDocumentBytes(batchId: string, documentId: string, enabled: boolean) {
  const [state, setState] = useState<DocumentFetchState>({ status: 'idle', bytes: null, message: '' });
  const controllerRef = useRef<AbortController | null>(null);

  const load = useCallback(() => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    setState({ status: 'loading', bytes: null, message: '' });
    void fetchDocumentContent(batchId, documentId, controller.signal).then(async (result) => {
      if (controller.signal.aborted) return;
      if (!result.ok) {
        setState({ status: mapErrorKindToStatus(result.error.kind), bytes: null, message: result.error.message, correlationId: result.error.correlationId });
        return;
      }
      const bytes = await result.data.arrayBuffer();
      if (controller.signal.aborted) return;
      setState({ status: 'ready', bytes, message: '' });
    });
  }, [batchId, documentId]);

  useEffect(() => {
    if (enabled) load();
    return () => controllerRef.current?.abort();
  }, [enabled, load]);

  return { ...state, retry: load };
}

function usePdfDocument(bytes: ArrayBuffer | null) {
  const [pdfDoc, setPdfDoc] = useState<PDFDocumentProxy | null>(null);
  const [loadError, setLoadError] = useState(false);

  useEffect(() => {
    if (!bytes) return;
    setLoadError(false);
    setPdfDoc(null);
    const task = getDocument({ data: new Uint8Array(bytes) });
    task.promise.then(
      (doc) => setPdfDoc(doc),
      () => setLoadError(true)
    );
    return () => {
      void task.destroy();
    };
  }, [bytes]);

  return { pdfDoc, loadError };
}

function computeScale(unscaledWidth: number, zoomPct: number, containerWidth: number | undefined): number {
  const width = containerWidth && containerWidth > 0 ? containerWidth : DEFAULT_STAGE_WIDTH;
  const baseScale = width / unscaledWidth;
  return baseScale * (zoomPct / 100);
}

async function renderPageToCanvas(
  pdfDoc: PDFDocumentProxy,
  pageNumber: number,
  zoomPct: number,
  canvasRef: RefObject<HTMLCanvasElement | null>,
  containerWidth: number | undefined
): Promise<{ width: number; height: number } | null> {
  const page = await pdfDoc.getPage(pageNumber);
  const unscaled = page.getViewport({ scale: 1 });
  const scale = computeScale(unscaled.width, zoomPct, containerWidth);
  const viewport = page.getViewport({ scale });
  const canvas = canvasRef.current;
  if (!canvas) return null;
  canvas.width = viewport.width;
  canvas.height = viewport.height;
  const ctx = canvas.getContext('2d');
  if (!ctx) return null;
  await page.render({ canvas, canvasContext: ctx, viewport }).promise;
  return { width: viewport.width, height: viewport.height };
}

function usePageRender(
  pdfDoc: PDFDocumentProxy | null,
  pageNumber: number,
  zoomPct: number,
  canvasRef: RefObject<HTMLCanvasElement | null>,
  containerWidth: number | undefined
) {
  const [renderedSize, setRenderedSize] = useState({ width: 0, height: 0 });
  const [renderError, setRenderError] = useState(false);

  useEffect(() => {
    if (!pdfDoc || pageNumber < 1) return;
    let cancelled = false;
    setRenderError(false);
    renderPageToCanvas(pdfDoc, pageNumber, zoomPct, canvasRef, containerWidth)
      .then((size) => {
        if (!cancelled && size) setRenderedSize(size);
      })
      .catch(() => {
        if (!cancelled) setRenderError(true);
      });
    return () => {
      cancelled = true;
    };
  }, [pdfDoc, pageNumber, zoomPct, canvasRef, containerWidth]);

  return { renderedSize, renderError };
}

function firstHighlightedPage(provenance: Provenance): number {
  const pages = (provenance.highlightRects ?? []).map((r) => r.pageNumber);
  if (pages.length > 0) return Math.min(...pages);
  return provenance.pageNumber ?? 1;
}

function derivePhase(inView: boolean, fetchStatus: FetchStatus, pdfDoc: PDFDocumentProxy | null, loadError: boolean, renderError: boolean): Phase {
  if (!inView) return 'not-in-view';
  if (fetchStatus === 'loading' || fetchStatus === 'idle') return 'loading';
  if (fetchStatus === 'ready') {
    if (loadError || renderError) return 'error';
    return pdfDoc ? 'ready' : 'loading';
  }
  return fetchStatus;
}

const toolbarRowStyle = {
  display: 'flex',
  alignItems: 'center',
  gap: 'var(--space-3)',
  flexWrap: 'wrap' as const,
  padding: '10px 14px',
  background: 'var(--surface-card-alt)',
  border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-md)',
  marginBottom: 'var(--space-3)',
};

const toolbarGroupStyle = { display: 'flex', alignItems: 'center', gap: 'var(--space-2)' };

function toolbarButtonStyle(disabled: boolean) {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: 44,
    minHeight: 44,
    borderRadius: 'var(--radius-md)',
    border: '1px solid var(--border-subtle)',
    background: 'var(--surface-card-alt)',
    color: disabled ? 'var(--text-muted)' : 'var(--text-body)',
    cursor: disabled ? 'default' : 'pointer',
  };
}

function ToolbarIconButton({ label, onClick, disabled, children }: { label: string; onClick: () => void; disabled?: boolean; children: React.ReactNode }) {
  return (
    <button type="button" aria-label={label} aria-disabled={disabled || undefined} disabled={disabled} onClick={disabled ? undefined : onClick} style={toolbarButtonStyle(!!disabled)}>
      {children}
    </button>
  );
}

function PlusIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
      <path d="M12 5v14M5 12h14" strokeLinecap="round" />
    </svg>
  );
}

function ResetIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
      <path d="M3 12a9 9 0 1 0 3-6.7M3 4v5h5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

interface ToolbarProps {
  page: number;
  numPages: number;
  zoomPct: number;
  hasHighlightOnPage: boolean;
  recommendation: string;
  onPrev: () => void;
  onNext: () => void;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onReset: () => void;
}

function ProvenanceToolbar({ page, numPages, zoomPct, hasHighlightOnPage, recommendation, onPrev, onNext, onZoomIn, onZoomOut, onReset }: ToolbarProps) {
  const { fg, bg } = verdictColor(recommendation);
  const label = recommendationLabel(recommendation);

  return (
    <div style={toolbarRowStyle}>
      <div style={toolbarGroupStyle}>
        <ToolbarIconButton label="Previous page" onClick={onPrev} disabled={page <= 1}>
          <span style={{ display: 'inline-flex', transform: 'rotate(180deg)' }}>
            <IconChevronRight size={14} />
          </span>
        </ToolbarIconButton>
        <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-xs)', color: 'var(--text-body)', minWidth: 90, textAlign: 'center' }}>
          Page {page} of {numPages || 1}
          {hasHighlightOnPage ? ' ● highlighted' : ''}
        </span>
        <ToolbarIconButton label="Next page" onClick={onNext} disabled={page >= numPages}>
          <IconChevronRight size={14} />
        </ToolbarIconButton>
      </div>
      <div style={{ width: 1, alignSelf: 'stretch', background: 'var(--border-subtle)' }} aria-hidden="true" />
      <div style={toolbarGroupStyle}>
        <ToolbarIconButton label="Zoom out" onClick={onZoomOut} disabled={zoomPct <= ZOOM_MIN}>
          <IconMinus size={14} />
        </ToolbarIconButton>
        <span aria-live="polite" style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-xs)', color: 'var(--text-body)', minWidth: 44, textAlign: 'center' }}>
          {zoomPct}%
        </span>
        <ToolbarIconButton label="Zoom in" onClick={onZoomIn} disabled={zoomPct >= ZOOM_MAX}>
          <PlusIcon />
        </ToolbarIconButton>
        <ToolbarIconButton label="Reset zoom" onClick={onReset}>
          <ResetIcon />
        </ToolbarIconButton>
      </div>
      <span
        style={{
          marginLeft: 'auto',
          display: 'inline-flex',
          alignItems: 'center',
          gap: 6,
          padding: '4px 12px',
          borderRadius: 'var(--radius-full)',
          background: bg,
          color: fg,
          fontSize: 'var(--text-xs)',
          fontWeight: 600,
        }}
      >
        <span style={{ width: 10, height: 10, borderRadius: 2, background: fg }} aria-hidden="true" />
        Cited region · {label}
      </span>
    </div>
  );
}

export interface PdfProvenanceViewerProps {
  batchId: string;
  documentId: string;
  provenance: Provenance;
  recommendation: string;
  onDocumentNotFound?: () => void;
}

function LoadingStage() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 12, minHeight: 320 }}>
      <Spinner size={28} />
      <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)' }}>Loading source page…</span>
    </div>
  );
}

function EmptyRectsNote() {
  return (
    <div
      role="status"
      style={{
        marginTop: 'var(--space-3)',
        padding: '10px 12px',
        background: 'var(--status-info-bg)',
        borderLeft: '3px solid var(--status-info-fg)',
        borderRadius: 'var(--radius-md)',
        fontSize: 'var(--text-xs)',
        color: 'var(--text-body)',
      }}
    >
      The source page is shown; no exact region was captured for this citation.
    </div>
  );
}

export function PdfProvenanceViewer({ batchId, documentId, provenance, recommendation, onDocumentNotFound }: PdfProvenanceViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const stageRef = useRef<HTMLDivElement>(null);

  const inView = useInView(containerRef, IN_VIEW_ROOT_MARGIN);
  const { status: fetchStatus, bytes, correlationId, retry } = useDocumentBytes(batchId, documentId, inView);
  const { pdfDoc, loadError } = usePdfDocument(bytes);

  const [page, setPage] = useState(1);
  const [zoomPct, setZoomPct] = useState(ZOOM_DEFAULT);
  const initializedRef = useRef(false);
  const retriedRef = useRef(false);

  const numPages = pdfDoc?.numPages ?? 0;
  const containerWidth = containerRef.current?.clientWidth;
  const { renderedSize, renderError } = usePageRender(pdfDoc, page, zoomPct, canvasRef, containerWidth);
  const phase = derivePhase(inView, fetchStatus, pdfDoc, loadError, renderError);

  // The route has no per-candidate key, so switching candidates reuses this same
  // component instance instead of remounting it. Without this reset the stale
  // `page`/init flag from the previous document would survive the prop change and
  // request an out-of-range page from the new, shorter PDF.
  useEffect(() => {
    initializedRef.current = false;
    setPage(1);
    setZoomPct(ZOOM_DEFAULT);
  }, [batchId, documentId]);

  useEffect(() => {
    if (pdfDoc && !initializedRef.current) {
      initializedRef.current = true;
      setPage(clamp(firstHighlightedPage(provenance), 1, Math.max(pdfDoc.numPages, 1)));
    }
  }, [pdfDoc, provenance]);

  useEffect(() => {
    if (phase === 'not-found') onDocumentNotFound?.();
  }, [phase, onDocumentNotFound]);

  useEffect(() => {
    if (phase === 'ready' && retriedRef.current) {
      retriedRef.current = false;
      stageRef.current?.focus();
    }
  }, [phase]);

  const handleRetry = useCallback(() => {
    retriedRef.current = true;
    retry();
  }, [retry]);

  const highlightRects = provenance.highlightRects ?? [];
  const hasAnyRects = highlightRects.length > 0;
  const hasHighlightOnPage = highlightRects.some((r) => r.pageNumber === page);
  const pageDim = provenance.pageDimensions?.find((d) => d.pageNumber === page) ?? null;

  const liveMessage = useMemo(() => {
    if (phase === 'loading') return 'Loading source page…';
    if (phase === 'ready') return `Source page ready, page ${page} of ${numPages}`;
    return '';
  }, [phase, page, numPages]);

  return (
    <div ref={containerRef}>
      {phase === 'not-in-view' && <Skeleton height={STAGE_MAX_HEIGHT} />}
      {phase === 'loading' && <LoadingStage />}
      {phase === 'forbidden' && <ErrorState title="Access denied" message="You don't have access to this document." />}
      {phase === 'error' && <ErrorState message="Couldn't load the source page." correlationId={correlationId} onRetry={handleRetry} />}
      {phase === 'ready' && (
        <>
          <ProvenanceToolbar
            page={page}
            numPages={numPages}
            zoomPct={zoomPct}
            hasHighlightOnPage={hasHighlightOnPage}
            recommendation={recommendation}
            onPrev={() => setPage((p) => clamp(p - 1, 1, numPages))}
            onNext={() => setPage((p) => clamp(p + 1, 1, numPages))}
            onZoomIn={() => setZoomPct((z) => clamp(z + ZOOM_STEP, ZOOM_MIN, ZOOM_MAX))}
            onZoomOut={() => setZoomPct((z) => clamp(z - ZOOM_STEP, ZOOM_MIN, ZOOM_MAX))}
            onReset={() => setZoomPct(ZOOM_DEFAULT)}
          />
          <div
            ref={stageRef}
            role="region"
            aria-label="Source document page"
            tabIndex={0}
            style={{ maxHeight: STAGE_MAX_HEIGHT, overflow: 'auto', background: 'var(--surface-sunken)', border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-md)', padding: 'var(--space-2)' }}
          >
            <div style={{ position: 'relative', width: renderedSize.width || undefined, height: renderedSize.height || undefined }}>
              <canvas ref={canvasRef} />
              {hasAnyRects && (
                <HighlightOverlay
                  rects={highlightRects}
                  pageNumber={page}
                  pageDim={pageDim}
                  renderedWidth={renderedSize.width}
                  renderedHeight={renderedSize.height}
                  recommendation={recommendation}
                />
              )}
            </div>
          </div>
          {!hasAnyRects && <EmptyRectsNote />}
          <div aria-live="polite" role="status" style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0,0,0,0)' }}>
            {liveMessage}
          </div>
        </>
      )}
    </div>
  );
}

export default PdfProvenanceViewer;
