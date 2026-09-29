import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { getDocument } from 'pdfjs-dist';
import { PdfProvenanceViewer } from '../PdfProvenanceViewer';
import type { Provenance } from '../opportunityTypes';

const mockPage = {
  getViewport: ({ scale }: { scale: number }) => ({ width: 100 * scale, height: 140 * scale }),
  render: () => ({ promise: Promise.resolve() }),
};

const mockPdfDoc = {
  numPages: 3,
  getPage: vi.fn().mockResolvedValue(mockPage),
};

vi.mock('pdfjs-dist/build/pdf.worker.min.mjs?url', () => ({ default: 'mock-worker-url' }));

vi.mock('pdfjs-dist', () => ({
  GlobalWorkerOptions: {},
  getDocument: vi.fn(() => ({
    promise: Promise.resolve(mockPdfDoc),
    destroy: vi.fn(),
  })),
}));

const fetchDocumentContentMock = vi.fn();
vi.mock('../opportunityRepository', () => ({
  fetchDocumentContent: (...args: unknown[]) => fetchDocumentContentMock(...args),
}));

function makeBlob(): Blob {
  return { arrayBuffer: () => Promise.resolve(new ArrayBuffer(8)) } as unknown as Blob;
}

function makeMockPdfDoc(numPages: number) {
  return {
    numPages,
    getPage: vi.fn().mockResolvedValue(mockPage),
  };
}

const baseProvenance: Provenance = {
  sourceDocumentId: 'doc-1',
  previewKind: 'pdf',
  pageNumber: 2,
  highlightRects: [{ pageNumber: 2, x0: 0.1, x1: 0.4, top: 0.2, bottom: 0.3 }],
  pageDimensions: [{ pageNumber: 2, width: 612, height: 792 }],
};

beforeEach(() => {
  fetchDocumentContentMock.mockReset();
});

describe('PdfProvenanceViewer', () => {
  it('shows the loading spinner while the document is being fetched', async () => {
    fetchDocumentContentMock.mockReturnValue(new Promise(() => {}));
    render(<PdfProvenanceViewer batchId="b1" documentId="doc-1" provenance={baseProvenance} recommendation="pursue" />);
    expect(await screen.findByText('Loading source page…')).toBeTruthy();
  });

  it('renders the toolbar and verdict legend once the PDF loads, opened on the first highlighted page', async () => {
    fetchDocumentContentMock.mockResolvedValue({ ok: true, data: makeBlob() });
    render(<PdfProvenanceViewer batchId="b1" documentId="doc-1" provenance={baseProvenance} recommendation="pursue" />);
    expect(await screen.findByText(/Page 2 of 3/)).toBeTruthy();
    expect(screen.getByLabelText('Previous page')).toBeTruthy();
    expect(screen.getByLabelText('Next page')).toBeTruthy();
    expect(screen.getByLabelText('Zoom in')).toBeTruthy();
    expect(screen.getByLabelText('Zoom out')).toBeTruthy();
    expect(screen.getByLabelText('Reset zoom')).toBeTruthy();
    expect(screen.getByText(/Cited region · Pursue/)).toBeTruthy();
  });

  it('shows an access-denied message with no retry button on a 403', async () => {
    fetchDocumentContentMock.mockResolvedValue({ ok: false, error: { kind: 'forbidden', message: 'nope' } });
    render(<PdfProvenanceViewer batchId="b1" documentId="doc-1" provenance={baseProvenance} recommendation="pursue" />);
    expect(await screen.findByText("You don't have access to this document.")).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
  });

  it('calls onDocumentNotFound on a 404 so the caller can fall through to the fallback', async () => {
    fetchDocumentContentMock.mockResolvedValue({ ok: false, error: { kind: 'not_found', message: 'nope' } });
    const onDocumentNotFound = vi.fn();
    render(
      <PdfProvenanceViewer batchId="b1" documentId="doc-1" provenance={baseProvenance} recommendation="pursue" onDocumentNotFound={onDocumentNotFound} />
    );
    await waitFor(() => expect(onDocumentNotFound).toHaveBeenCalled());
  });

  it('shows a network error message with a working retry button', async () => {
    fetchDocumentContentMock.mockResolvedValueOnce({ ok: false, error: { kind: 'network', message: 'nope' } });
    fetchDocumentContentMock.mockResolvedValueOnce({ ok: true, data: makeBlob() });
    render(<PdfProvenanceViewer batchId="b1" documentId="doc-1" provenance={baseProvenance} recommendation="pursue" />);
    expect(await screen.findByText("Couldn't load the source page.")).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText(/Page 2 of 3/)).toBeTruthy();
  });

  it('shows the empty-rects info line when no highlight rects were captured', async () => {
    fetchDocumentContentMock.mockResolvedValue({ ok: true, data: makeBlob() });
    const provenance: Provenance = { ...baseProvenance, highlightRects: [] };
    render(<PdfProvenanceViewer batchId="b1" documentId="doc-1" provenance={provenance} recommendation="pursue" />);
    expect(await screen.findByText(/no exact region was captured for this citation/)).toBeTruthy();
  });

  it('resolves the abandon verdict to the danger legend label', async () => {
    fetchDocumentContentMock.mockResolvedValue({ ok: true, data: makeBlob() });
    render(<PdfProvenanceViewer batchId="b1" documentId="doc-1" provenance={baseProvenance} recommendation="abandon" />);
    expect(await screen.findByText(/Cited region · Abandon/)).toBeTruthy();
  });

  it('resets the stale page when documentId changes without unmounting (router has no per-candidate key)', async () => {
    const docA = makeMockPdfDoc(3);
    const docB = makeMockPdfDoc(1);
    vi.mocked(getDocument)
      .mockReturnValueOnce({ promise: Promise.resolve(docA), destroy: vi.fn() } as never)
      .mockReturnValueOnce({ promise: Promise.resolve(docB), destroy: vi.fn() } as never);
    fetchDocumentContentMock.mockResolvedValue({ ok: true, data: makeBlob() });

    const provenanceA: Provenance = {
      sourceDocumentId: 'doc-a',
      previewKind: 'pdf',
      pageNumber: 2,
      highlightRects: [{ pageNumber: 2, x0: 0.1, x1: 0.4, top: 0.2, bottom: 0.3 }],
    };
    const provenanceB: Provenance = { sourceDocumentId: 'doc-b', previewKind: 'pdf', pageNumber: 1, highlightRects: [] };

    const { rerender } = render(
      <PdfProvenanceViewer batchId="b1" documentId="doc-a" provenance={provenanceA} recommendation="pursue" />
    );
    expect(await screen.findByText(/Page 2 of 3/)).toBeTruthy();

    fireEvent.click(screen.getByLabelText('Next page'));
    expect(await screen.findByText(/Page 3 of 3/)).toBeTruthy();

    rerender(<PdfProvenanceViewer batchId="b1" documentId="doc-b" provenance={provenanceB} recommendation="pursue" />);

    expect(await screen.findByText(/Page 1 of 1/)).toBeTruthy();
    expect(docB.getPage).toHaveBeenCalledWith(1);
    expect(docB.getPage).not.toHaveBeenCalledWith(3);
  });
});
