import { describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

vi.mock('../PdfProvenanceViewer', () => ({
  default: ({ onDocumentNotFound }: { onDocumentNotFound?: () => void }) => (
    <div data-testid="mock-pdf-viewer">
      <button type="button" onClick={() => onDocumentNotFound?.()}>
        simulate-not-found
      </button>
    </div>
  ),
}));

vi.mock('../CodeProvenanceViewer', () => ({
  default: ({ provenance }: { provenance: { filePath?: string | null } }) => (
    <div data-testid="mock-code-viewer">{provenance.filePath}</div>
  ),
}));

import {
  CitationChip,
  AxisScoreRow,
  EvidenceSourceCard,
  ClaimDraftPanel,
  ProvenancePanel,
} from '../OpportunityDetailComponents';
import type { AxisScore, EvidenceSource, Provenance, ResolvedCitation } from '../opportunityTypes';
import { ToastProvider } from '../../../shared/ds/Toast';
import type { ReactNode } from 'react';

function WithToast({ children }: { children: ReactNode }) {
  return <ToastProvider>{children}</ToastProvider>;
}

describe('CitationChip', () => {
  it('renders resolved citation as clickable link with source-kind dot and external-link icon', () => {
    const citation: ResolvedCitation = {
      ref: 'ref-1',
      title: 'Adaptive Neural Compression',
      url: 'https://patents.google.com/patent/US123456',
      patentId: 'US-123456-B2',
      similarity: 0.87,
      sourceType: 'patent_api',
    };

    render(<CitationChip citation={citation} />);

    const link = screen.getByRole('link', { name: /Open US-123456-B2/i });
    expect(link).toBeTruthy();
    expect(link.getAttribute('href')).toBe('https://patents.google.com/patent/US123456');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    expect(screen.getByText('US-123456-B2')).toBeTruthy();
    expect(screen.getByText('↗')).toBeTruthy();
    expect(screen.getByText('87%')).toBeTruthy();
  });

  it('renders unresolved citation as inert span with no link icon', () => {
    const citation: ResolvedCitation = {
      ref: 'ref-unresolved',
    };

    render(<CitationChip citation={citation} />);

    const linkQuery = screen.queryByRole('link');
    expect(linkQuery).toBeNull();
    expect(screen.getByText('ref-unresolved')).toBeTruthy();
    expect(screen.queryByText('↗')).toBeNull();
  });

  it('truncates long title to 34ch with ellipsis', () => {
    const citation: ResolvedCitation = {
      ref: 'ref-long',
      title: 'A Very Long Title That Should Be Truncated Because It Exceeds The 28 Character Threshold And We Want To Show Ellipsis',
      url: 'https://example.com',
      sourceType: 'vector_corpus',
    };

    const { container } = render(<CitationChip citation={citation} />);

    const chipSpan = container.querySelector('span[style*="max-width"]');
    expect(chipSpan).toBeTruthy();
    expect(chipSpan?.getAttribute('style')).toContain('max-width: 34ch');
    expect(chipSpan?.getAttribute('style')).toContain('text-overflow: ellipsis');
  });

  it('renders small size with correct fontSize and padding', () => {
    const citation: ResolvedCitation = {
      ref: 'ref-small',
      patentId: 'US-999999-A1',
      url: 'https://example.com',
    };

    const { container } = render(<CitationChip citation={citation} small />);

    const chipSpan = container.querySelector('span[style*="padding"]');
    expect(chipSpan?.getAttribute('style')).toContain('padding: 2px 7px');
  });

  it('renders normal size with correct padding', () => {
    const citation: ResolvedCitation = {
      ref: 'ref-normal',
      patentId: 'US-888888-B2',
      url: 'https://example.com',
    };

    const { container } = render(<CitationChip citation={citation} small={false} />);

    const chipSpan = container.querySelector('span[style*="padding"]');
    expect(chipSpan?.getAttribute('style')).toContain('padding: 3px 8px');
  });

  it('renders similarity badge when similarity is present', () => {
    const citation: ResolvedCitation = {
      ref: 'ref-sim',
      title: 'Test Patent',
      url: 'https://example.com',
      similarity: 0.92,
    };

    render(<CitationChip citation={citation} />);
    expect(screen.getByText('92%')).toBeTruthy();
  });

  it('does not render similarity badge when similarity is null', () => {
    const citation: ResolvedCitation = {
      ref: 'ref-no-sim',
      title: 'Test Patent',
      url: 'https://example.com',
      similarity: undefined,
    };

    render(<CitationChip citation={citation} />);
    expect(screen.queryByText(/%/)).toBeNull();
  });
});

describe('AxisScoreRow', () => {
  it('renders axis title transformed from snake_case with score and progress bar', () => {
    const axis: AxisScore = {
      axis: 'non_obviousness',
      score: 78.3,
      citations: [],
    };

    render(<AxisScoreRow axis={axis} isLast={false} />);

    expect(screen.getByText('Non Obviousness')).toBeTruthy();
    expect(screen.getByText('78 / 100')).toBeTruthy();
  });

  it('renders CitationChips when axis has resolved citations', () => {
    const axis: AxisScore = {
      axis: 'novelty',
      score: 85,
      citations: [
        { ref: 'ref-1', title: 'Patent A', url: 'https://example.com/a', patentId: 'US-111-A' },
        { ref: 'ref-2', title: 'Patent B', url: 'https://example.com/b', patentId: 'US-222-B' },
      ],
    };

    render(<AxisScoreRow axis={axis} isLast={false} />);

    expect(screen.getByText('US-111-A')).toBeTruthy();
    expect(screen.getByText('US-222-B')).toBeTruthy();
    expect(screen.getByText('Evidence')).toBeTruthy();
  });

  it('renders honest empty state when axis has zero resolved citations', () => {
    const axis: AxisScore = {
      axis: 'utility',
      score: 65,
      citations: [{ ref: 'ref-unresolved' }, { ref: 'ref-another-unresolved' }],
    };

    render(<AxisScoreRow axis={axis} isLast={false} />);

    expect(screen.getByText('Not captured')).toBeTruthy();
    expect(screen.getByText('— scored on the disclosure alone')).toBeTruthy();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('omits bottom border when isLast is true', () => {
    const axis: AxisScore = { axis: 'enablement', score: 70, citations: [] };

    const { container } = render(<AxisScoreRow axis={axis} isLast />);

    const axisRow = container.firstChild as HTMLElement;
    expect(axisRow.style.borderBottom).not.toBeTruthy();
  });

  it('renders bottom border when isLast is false', () => {
    const axis: AxisScore = { axis: 'claim_clarity', score: 80, citations: [] };

    const { container } = render(<AxisScoreRow axis={axis} isLast={false} />);

    const axisRow = container.firstChild as HTMLElement;
    expect(axisRow.getAttribute('style')).toContain('border-bottom: 1px solid var(--border-subtle)');
  });

  it('renders reasoning when present', () => {
    const axis: AxisScore = {
      axis: 'novelty',
      score: 90,
      reasoning: 'No prior art found in the corpus.',
      citations: [],
    };

    render(<AxisScoreRow axis={axis} isLast={false} />);
    expect(screen.getByText('No prior art found in the corpus.')).toBeTruthy();
  });

  it('clamps progress bar to 0-100 range', () => {
    const axisLow: AxisScore = { axis: 'novelty', score: -5, citations: [] };
    const { rerender } = render(<AxisScoreRow axis={axisLow} isLast={false} />);
    expect(screen.getByText('-5 / 100')).toBeTruthy();

    const axisHigh: AxisScore = { axis: 'novelty', score: 105, citations: [] };
    rerender(<AxisScoreRow axis={axisHigh} isLast={false} />);
    expect(screen.getByText('105 / 100')).toBeTruthy();
  });
});

describe('EvidenceSourceCard', () => {
  it('renders Available state with green border, success pill, progress bar, confidence, and legacy citation chips (no evidence_sources on report)', () => {
    const source: EvidenceSource = {
      sourceType: 'patent_api',
      available: true,
      state: 'active_cited',
      confidence: 0.84,
      summary: 'Found 3 highly relevant patents.',
      citations: [
        { ref: 'ref-1', title: 'Patent X', url: 'https://example.com/x', patentId: 'US-123' },
      ],
    };

    const { container } = render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('Live Patent API')).toBeTruthy();
    expect(screen.getByText('Available')).toBeTruthy();
    expect(screen.getByText('84% confidence')).toBeTruthy();
    expect(screen.getByText('US-123')).toBeTruthy();
    expect(screen.queryByText('Found 3 highly relevant patents.')).toBeNull();

    const card = container.firstChild as HTMLElement;
    expect(card.getAttribute('style')).toContain('border-left: 3px solid var(--status-success-fg)');
  });

  it('renders real patent hits with external Google-Patents-style links instead of the generic uncited line (BUG183)', () => {
    const source: EvidenceSource = {
      sourceType: 'patent_api',
      available: true,
      state: 'active_cited',
      confidence: 0.81,
      summary: '',
      citations: [],
      hits: [
        { title: 'Adaptive neural cache prefetch controller', patentId: 'US10891234B2', url: 'https://patents.google.com/patent/US10891234B2/en', similarity: 0.91, jurisdiction: 'US' },
        { title: 'Method for speculative memory access scheduling', patentId: 'EP3745271A1', url: 'https://patents.google.com/patent/EP3745271A1/en', similarity: 0.84, jurisdiction: 'EP' },
      ],
    };

    render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('81% confidence')).toBeTruthy();
    expect(screen.queryByText('Active — corroborating hit not among cited refs.')).toBeNull();
    const link = screen.getByRole('link', { name: /Open Adaptive neural cache prefetch controller, US10891234B2 \(US patent, relevance 91%, opens in new tab\)/i });
    expect(link.getAttribute('href')).toBe('https://patents.google.com/patent/US10891234B2/en');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    expect(screen.getByText('91%')).toBeTruthy();
    expect(screen.getByText('84%')).toBeTruthy();
    expect(screen.getByText('US')).toBeTruthy();
    expect(screen.getByText('EP')).toBeTruthy();
  });

  it('renders corpus hits as plain text rows with no external link and no jurisdiction tag', () => {
    const source: EvidenceSource = {
      sourceType: 'vector_corpus',
      available: true,
      state: 'active_cited',
      confidence: 0.73,
      summary: '',
      citations: [],
      hits: [
        { title: 'Cache-coherent prefetch in heterogeneous SoCs', similarity: 0.88 },
      ],
    };

    render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('Cache-coherent prefetch in heterogeneous SoCs')).toBeTruthy();
    expect(screen.getByText('88%')).toBeTruthy();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('renders LLM reasoning bullets instead of the generic uncited line (BUG183)', () => {
    const source: EvidenceSource = {
      sourceType: 'llm_deep_research',
      available: true,
      state: 'active_cited',
      confidence: 0.69,
      summary: '',
      citations: [],
      reasoning: [
        'The closest prior art prefetches on static heuristics; the candidate is a non-obvious departure.',
        'Claimed feedback loop is not disclosed by any retrieved reference — supports novelty.',
      ],
    };

    const { container } = render(<EvidenceSourceCard source={source} />);

    // Preview shows the first finding; the rest live in the "view full analysis" modal.
    expect(screen.getByText(/The closest prior art prefetches/)).toBeTruthy();
    expect(container.querySelector('ul')).toBeTruthy();
    expect(screen.queryByText('Active — corroborating hit not among cited refs.')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'View full analysis' }));
    expect(screen.getByText(/Claimed feedback loop/)).toBeTruthy();
  });

  it('previews 3 hits and opens the full list in a dialog via an accessible View all button', () => {
    const source: EvidenceSource = {
      sourceType: 'patent_api',
      available: true,
      state: 'active_cited',
      confidence: 0.8,
      summary: '',
      citations: [],
      hits: [
        { title: 'Hit 1', url: 'https://example.com/1', similarity: 0.9, jurisdiction: 'US' },
        { title: 'Hit 2', url: 'https://example.com/2', similarity: 0.8, jurisdiction: 'US' },
        { title: 'Hit 3', url: 'https://example.com/3', similarity: 0.7, jurisdiction: 'US' },
        { title: 'Hit 4', url: 'https://example.com/4', similarity: 0.6, jurisdiction: 'US' },
        { title: 'Hit 5', url: 'https://example.com/5', similarity: 0.5, jurisdiction: 'US' },
      ],
    };

    render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('Hit 1')).toBeTruthy();
    expect(screen.getByText('Hit 3')).toBeTruthy();
    expect(screen.queryByText('Hit 4')).toBeNull();

    const trigger = screen.getByRole('button', { name: 'View all 5 hits' });
    expect(trigger.getAttribute('aria-haspopup')).toBe('dialog');

    fireEvent.click(trigger);

    expect(screen.getByRole('dialog')).toBeTruthy();
    expect(screen.getByText('Hit 4')).toBeTruthy();
    expect(screen.getByText('Hit 5')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Close' })).toBeTruthy();
  });

  it('renders a partial-coverage info chip on the patent card when degraded, without downgrading the state pill', () => {
    const source: EvidenceSource = {
      sourceType: 'patent_api',
      available: true,
      state: 'active_cited',
      confidence: 0.8,
      summary: '',
      citations: [],
      hits: [{ title: 'US Hit', url: 'https://example.com/us', similarity: 0.8, jurisdiction: 'US' }],
      degraded: true,
      degradedSources: ['Lens'],
    };

    render(<EvidenceSourceCard source={source} />);

    const note = screen.getByRole('note');
    expect(note.textContent).toContain('Partial coverage');
    expect(note.textContent).toContain('Lens');
    expect(note.getAttribute('aria-label')).toContain('Lens unavailable');
    expect(screen.getByText('Available')).toBeTruthy();
  });

  it('does not render a degraded chip for the corpus or LLM cards even when degraded is set', () => {
    const source: EvidenceSource = {
      sourceType: 'vector_corpus',
      available: true,
      state: 'active_cited',
      confidence: 0.8,
      summary: '',
      citations: [],
      hits: [{ title: 'Corpus Hit', similarity: 0.8 }],
      degraded: true,
      degradedSources: ['Lens'],
    };

    render(<EvidenceSourceCard source={source} />);

    expect(screen.queryByRole('note')).toBeNull();
  });

  it('adds an aria-label with the confidence percentage to the progress bar', () => {
    const source: EvidenceSource = {
      sourceType: 'patent_api',
      available: true,
      state: 'active_cited',
      confidence: 0.81,
      summary: '',
      citations: [],
      hits: [{ title: 'US Hit', url: 'https://example.com/us', similarity: 0.81, jurisdiction: 'US' }],
    };

    render(<EvidenceSourceCard source={source} />);

    expect(screen.getByRole('progressbar', { name: 'Confidence 81%' })).toBeTruthy();
  });

  it('renders the fixed-height body region and shows no "view all" affordance when exactly 3 hits fit the preview', () => {
    const source: EvidenceSource = {
      sourceType: 'vector_corpus',
      available: true,
      state: 'active_cited',
      confidence: 0.66,
      summary: '',
      citations: [],
      hits: [
        { title: 'Corpus Hit 1', similarity: 0.7 },
        { title: 'Corpus Hit 2', similarity: 0.66 },
        { title: 'Corpus Hit 3', similarity: 0.65 },
      ],
    };

    const { container } = render(<EvidenceSourceCard source={source} />);

    // Fixed-height body region present (equal-size guarantee) but no truncation button.
    expect(container.querySelector('.evi-card-body')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /View all/ })).toBeNull();
    expect(screen.getByText('Corpus Hit 3')).toBeTruthy();
  });

  it('offers no "view all" affordance and no dialog for a timed-out summary-only source', () => {
    const source: EvidenceSource = {
      sourceType: 'patent_api',
      available: true,
      state: 'source_timeout',
      confidence: 0,
      summary: 'This source ran but timed out before returning results.',
      citations: [],
    };

    render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('Timed out')).toBeTruthy();
    expect(screen.getByText('This source ran but timed out before returning results.')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /View/ })).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('reserves the generic uncited line for a source with genuinely no hits or reasoning, showing "Contributed to scoring" instead of a fake percentage', () => {
    const source: EvidenceSource = {
      sourceType: 'patent_api',
      available: true,
      state: 'active_uncited',
      confidence: 0,
      summary: 'Active — corroborating hit not among cited refs.',
      citations: [],
    };

    render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('Active — corroborating hit not among cited refs.')).toBeTruthy();
    expect(screen.getByText('Contributed to scoring')).toBeTruthy();
    expect(screen.queryByText(/% confidence/)).toBeNull();
  });

  it('renders Unavailable state with warning border, warning pill, gray filled bar, meta text, and honest summary', () => {
    const source: EvidenceSource = {
      sourceType: 'vector_corpus',
      available: true,
      state: 'unavailable',
      confidence: 0,
      summary: '',
      citations: [],
    };

    const { container } = render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('Corpus Vector Search')).toBeTruthy();
    expect(screen.getByText('Unavailable')).toBeTruthy();
    expect(screen.getByText('Ran, returned 0 matches')).toBeTruthy();
    expect(
      screen.getByText('The source ran and genuinely returned nothing. This is a real result — not a failure.')
    ).toBeTruthy();

    const card = container.firstChild as HTMLElement;
    expect(card.getAttribute('style')).toContain('border-left: 3px solid var(--status-warning-fg)');
    expect(card.getAttribute('style')).toContain('border-style: solid');
  });

  it('renders Not captured state with dashed border, neutral pill, empty bar, meta text, and honest summary', () => {
    const source: EvidenceSource = {
      sourceType: 'llm_deep_research',
      available: false,
      state: 'unavailable',
      confidence: 0,
      summary: '',
      citations: [],
    };

    const { container } = render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('LLM Deep Research')).toBeTruthy();
    const notCapturedElements = screen.getAllByText('Not captured');
    expect(notCapturedElements.length).toBeGreaterThan(0);
    expect(screen.getByText('We have no record that this source ran for this candidate.')).toBeTruthy();

    const card = container.firstChild as HTMLElement;
    expect(card.getAttribute('style')).toContain('border-style: dashed');
  });

  it('renders Blocked state with info border, info pill, shield-slash icon, neutral 0% bar, and provider-block copy (BUG169)', () => {
    const source: EvidenceSource = {
      sourceType: 'llm_deep_research',
      available: true,
      state: 'content_filtered',
      confidence: 0,
      summary: '',
      citations: [],
    };

    const { container } = render(<EvidenceSourceCard source={source} />);

    expect(screen.getByText('LLM Deep Research')).toBeTruthy();
    expect(screen.getByText('Blocked')).toBeTruthy();
    expect(screen.getByText('Blocked by provider policy')).toBeTruthy();
    expect(
      screen.getByText(
        "This source ran, but the model provider's content filter refused the request. It's excluded from this score — not missing, and not a zero result."
      )
    ).toBeTruthy();

    const card = container.firstChild as HTMLElement;
    expect(card.getAttribute('style')).toContain('border-left: 3px solid var(--status-info-fg)');
    expect(card.getAttribute('style')).toContain('background: var(--surface-card-alt)');
    expect(card.getAttribute('style')).toContain('border-style: solid');
    expect(container.querySelector('svg[aria-hidden="true"]')).toBeTruthy();
  });

  it('does not regress the existing Unavailable (ran-0) and Not captured states when Blocked state is present in the codebase', () => {
    const ranZero: EvidenceSource = {
      sourceType: 'vector_corpus',
      available: true,
      state: 'unavailable',
      confidence: 0,
      summary: '',
      citations: [],
    };
    const { container: ranZeroContainer } = render(<EvidenceSourceCard source={ranZero} />);
    expect(screen.getByText('Unavailable')).toBeTruthy();
    expect(screen.getByText('Ran, returned 0 matches')).toBeTruthy();
    expect((ranZeroContainer.firstChild as HTMLElement).getAttribute('style')).toContain('border-left: 3px solid var(--status-warning-fg)');

    const notCaptured: EvidenceSource = {
      sourceType: 'patent_api',
      available: false,
      state: 'unavailable',
      confidence: 0,
      summary: '',
      citations: [],
    };
    const { container: notCapturedContainer } = render(<EvidenceSourceCard source={notCaptured} />);
    expect(screen.getAllByText('Not captured').length).toBeGreaterThan(0);
    expect((notCapturedContainer.firstChild as HTMLElement).getAttribute('style')).toContain('border-style: dashed');
  });
});

describe('ClaimDraftPanel', () => {
  it('renders panel with claim text in mono font when claimDraft is populated', () => {
    const claimDraft = 'A method comprising: step one; step two; step three.';

    render(<ClaimDraftPanel claimDraft={claimDraft} />);

    expect(screen.getByText('Claim Draft')).toBeTruthy();
    expect(screen.getByText('Grounded draft')).toBeTruthy();
    expect(screen.getByText(claimDraft)).toBeTruthy();
  });

  it('returns null when claimDraft is empty string', () => {
    const { container } = render(<ClaimDraftPanel claimDraft="" />);
    expect(container.firstChild).toBeNull();
  });

  it('returns null when claimDraft is null', () => {
    const { container } = render(<ClaimDraftPanel claimDraft={null as unknown as string} />);
    expect(container.firstChild).toBeNull();
  });

  it('returns null when claimDraft is whitespace only', () => {
    const { container } = render(<ClaimDraftPanel claimDraft="   " />);
    expect(container.firstChild).toBeNull();
  });
});

describe('ProvenancePanel', () => {
  it('renders Not captured for null field values', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-123',
      sourceFilename: null,
      pageNumber: null,
      spanStart: null,
      spanEnd: null,
      excerptText: null,
      chunkIndex: undefined,
      sectionHint: undefined,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    const notCapturedElements = screen.getAllByText('Not captured');
    expect(notCapturedElements.length).toBeGreaterThan(0);
  });

  it('renders Not captured — not applicable for sourceKind when appliesTo excludes the kind', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-code',
      sourceKind: 'Code',
      pageNumber: 42,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    const notCapturedElements = screen.getAllByText('Not captured');
    expect(notCapturedElements.length).toBeGreaterThan(0);
    expect(screen.getByText('— not applicable for code')).toBeTruthy();
  });

  it('renders formatted character span with copy chip when both spanStart and spanEnd present', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-span',
      spanStart: 1234,
      spanEnd: 5678,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText(/characters 1,234–5,678/)).toBeTruthy();
    const copyButton = screen.getByRole('button', { name: /Copy character span 1234 to 5678/i });
    expect(copyButton).toBeTruthy();
  });

  it('renders Not captured for character span when either spanStart or spanEnd is missing', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-partial-span',
      spanStart: 100,
      spanEnd: null,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('Character span')).toBeTruthy();
    const notCapturedElements = screen.getAllByText('Not captured');
    expect(notCapturedElements.length).toBeGreaterThan(0);
  });

  it('renders blockquote when excerptText is populated', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-excerpt',
      excerptText: 'This is the source excerpt from the document.',
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('This is the source excerpt from the document.')).toBeTruthy();
    expect(screen.queryByText('No source excerpt was captured')).toBeNull();
  });

  it('renders dashed empty box when excerptText is null', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-no-excerpt',
      excerptText: null,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('No source excerpt was captured for this candidate.')).toBeTruthy();
  });

  it('renders deep-link as clickable link when hitUrl present', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-link',
      hitUrl: 'https://example.com/document#section',
      pageNumber: 5,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    const link = screen.getByRole('link', { name: /View this excerpt in the source document, page 5/i });
    expect(link).toBeTruthy();
    expect(link.getAttribute('href')).toBe('https://example.com/document#section');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('renders honest unavailable text when hitUrl is null, with no disabled affordance', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-no-link',
      hitUrl: undefined,
    };

    const { container } = render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('No source link available for this source type.')).toBeTruthy();
    expect(container.querySelector('span[aria-disabled="true"]')).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('renders sourceKind normalized', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-kind',
      sourceKind: 'research_paper',
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('Research Paper')).toBeTruthy();
  });

  it('renders page number field when sourceKind is Paper', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-paper',
      sourceKind: 'Paper',
      pageNumber: 12,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('Page 12')).toBeTruthy();
  });

  it('renders chunk index field when present', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-chunk',
      chunkIndex: 7,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('Chunk 7')).toBeTruthy();
  });

  it('renders document ID with truncated copy chip', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-very-long-document-id-123456789',
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    const copyButton = screen.getByRole('button', { name: /Document ID doc-very, click to copy full ID/i });
    expect(copyButton).toBeTruthy();
  });

  it('does not render a File row when sourceFilename is absent, even with a sourceDocumentId GUID', () => {
    const provenance: Provenance = {
      sourceDocumentId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
      sourceFilename: null,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.queryByText('File')).toBeNull();
    expect(screen.queryByText('3fa85f64-5717-4562-b3fc-2c963f66afa6')).toBeNull();
  });

  it('renders a File row with the real filename when sourceFilename is present', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-with-name',
      sourceFilename: 'thesis-chapter-3.pdf',
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('File')).toBeTruthy();
    expect(screen.getByText('thesis-chapter-3.pdf')).toBeTruthy();
  });

  it('keeps Document ID and Character span out of the primary Location rows and inside the Technical details disclosure', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-details-test',
      spanStart: 10,
      spanEnd: 20,
    };

    const { container } = render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    const details = container.querySelector('details');
    expect(details).toBeTruthy();
    expect(screen.getByText('Technical details')).toBeTruthy();

    const primaryDl = container.querySelectorAll('dl')[0];
    expect(primaryDl?.textContent).not.toContain('Document ID');
    expect(primaryDl?.textContent).not.toContain('Character span');

    expect(details?.textContent).toContain('Document ID');
    expect(details?.textContent).toContain('Character span');
  });

  it('omits the Technical details disclosure when there is no sourceDocumentId and no valid span', () => {
    const provenance: Provenance = {
      sourceDocumentId: '',
      spanStart: null,
      spanEnd: null,
    };

    const { container } = render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(container.querySelector('details')).toBeNull();
  });

  it('still renders Section, Page, Chunk, and Excerpt content', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-full',
      sourceKind: 'Paper',
      pageNumber: 3,
      sectionHint: 'Methods',
      chunkIndex: 2,
      excerptText: 'The excerpt body text.',
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('Page 3')).toBeTruthy();
    expect(screen.getByText('Methods')).toBeTruthy();
    expect(screen.getByText('Chunk 2')).toBeTruthy();
    expect(screen.getByText('The excerpt body text.')).toBeTruthy();
  });
});

describe('ProvenancePanel — previewKind branching (US124)', () => {
  it('renders the legacy Location/Excerpt/Actions body when previewKind is undefined', () => {
    const provenance: Provenance = { sourceDocumentId: 'doc-legacy', sourceKind: 'Paper', pageNumber: 4 };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} recommendation="pursue" batchId="batch-1" />
      </WithToast>
    );

    expect(screen.getByText('Location')).toBeTruthy();
    expect(screen.queryByText("Source preview isn't available for this batch.")).toBeNull();
  });

  it('renders the CodeProvenanceViewer for previewKind "code" (US125)', async () => {
    const provenance: Provenance = { sourceDocumentId: 'doc-code', previewKind: 'code', sourceKind: 'Code', filePath: 'src/main.py' };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} recommendation="pursue" batchId="batch-1" />
      </WithToast>
    );

    expect(await screen.findByTestId('mock-code-viewer')).toBeTruthy();
    expect(screen.queryByText('Location')).toBeNull();
    expect(screen.getByText('Technical details')).toBeTruthy();
  });

  it('renders the clean fallback for previewKind "none" with cleanExcerpt', () => {
    const provenance: Provenance = {
      sourceDocumentId: 'doc-none',
      previewKind: 'none',
      cleanExcerpt: 'A cleaned readable excerpt.',
      spanStart: 10,
      spanEnd: 20,
      chunkIndex: 3,
    };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} recommendation="pursue" batchId="batch-1" />
      </WithToast>
    );

    expect(screen.getByText("Source preview isn't available for this batch.")).toBeTruthy();
    expect(screen.getByText('This batch was processed before document preview was captured.')).toBeTruthy();
    expect(screen.getByText('A cleaned readable excerpt.')).toBeTruthy();
    expect(screen.queryByText('Location')).toBeNull();
    expect(screen.getByText('Technical details')).toBeTruthy();
  });

  it('renders the fallback dashed empty-excerpt note when neither cleanExcerpt nor excerptText is present', () => {
    const provenance: Provenance = { sourceDocumentId: 'doc-none-empty', previewKind: 'none' };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} recommendation="pursue" batchId="batch-1" />
      </WithToast>
    );

    expect(screen.getByText('No source excerpt was captured for this candidate.')).toBeTruthy();
  });

  it('falls back to the legacy body for previewKind "pdf" when batchId or recommendation is missing', () => {
    const provenance: Provenance = { sourceDocumentId: 'doc-pdf-no-ctx', previewKind: 'pdf' };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} />
      </WithToast>
    );

    expect(screen.getByText('Location')).toBeTruthy();
    expect(screen.queryByTestId('mock-pdf-viewer')).toBeNull();
  });

  it('mounts the lazy PDF viewer for previewKind "pdf" with batchId and recommendation, and falls through to the fallback on 404', async () => {
    const provenance: Provenance = { sourceDocumentId: 'doc-pdf', previewKind: 'pdf', cleanExcerpt: 'Fallback excerpt.' };

    render(
      <WithToast>
        <ProvenancePanel provenance={provenance} recommendation="pursue" batchId="batch-1" />
      </WithToast>
    );

    const viewer = await screen.findByTestId('mock-pdf-viewer');
    expect(viewer).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'simulate-not-found' }));

    await waitFor(() => expect(screen.getByText("Source preview isn't available for this batch.")).toBeTruthy());
    expect(screen.getByText('Fallback excerpt.')).toBeTruthy();
  });
});
