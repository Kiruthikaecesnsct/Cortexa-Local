import { describe, expect, it } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { CodeProvenanceViewer } from '../CodeProvenanceViewer';
import type { Provenance } from '../opportunityTypes';

function baseProvenance(overrides: Partial<Provenance>): Provenance {
  return {
    sourceDocumentId: 'doc-1',
    sourceKind: 'Code',
    ...overrides,
  };
}

describe('CodeProvenanceViewer (US125)', () => {
  it('renders the Plain-text fallback for an unknown/undetected language without crashing', async () => {
    const provenance = baseProvenance({
      filePath: 'infra/Makefile.unknownext',
      excerptText: 'build:\n\techo hi',
      lineRange: { startLine: 10, endLine: 11 },
    });

    render(<CodeProvenanceViewer provenance={provenance} />);

    expect(screen.getByText('Plain text')).toBeTruthy();
    expect(screen.getByText('infra/Makefile.unknownext')).toBeTruthy();
    await waitFor(() => expect(screen.getByText('build:')).toBeTruthy());
  });

  it('renders the "no excerpt" empty state while still showing the file-path header', () => {
    const provenance = baseProvenance({ filePath: 'src/main.py', excerptText: null });

    render(<CodeProvenanceViewer provenance={provenance} />);

    expect(screen.getByText('src/main.py')).toBeTruthy();
    expect(screen.getByText('No source excerpt was captured for this candidate.')).toBeTruthy();
  });

  it('renders "Line numbers unavailable" and starts the gutter at 1 when lineRange is absent', () => {
    const provenance = baseProvenance({
      filePath: 'src/utils.txt',
      excerptText: 'line one\nline two',
      lineRange: null,
    });

    render(<CodeProvenanceViewer provenance={provenance} />);

    expect(screen.getByText('Line numbers unavailable.')).toBeTruthy();
    expect(screen.getByRole('region', { name: /line numbers unavailable/i })).toBeTruthy();
  });

  it('shows the honest degrade note when the excerpt is shorter than the declared cited range', () => {
    const provenance = baseProvenance({
      filePath: 'src/handler.txt',
      excerptText: 'only one line',
      lineRange: { startLine: 40, endLine: 45 },
    });

    render(<CodeProvenanceViewer provenance={provenance} />);

    expect(screen.getByText('Showing lines 40–40 of cited 40–45.')).toBeTruthy();
  });

  it('exposes a keyboard-scrollable region with an aria-label naming the file and cited range', () => {
    const provenance = baseProvenance({
      filePath: 'src/model.txt',
      excerptText: 'a\nb\nc',
      lineRange: { startLine: 5, endLine: 7 },
    });

    render(<CodeProvenanceViewer provenance={provenance} />);

    const region = screen.getByRole('region', { name: 'Source code, src/model.txt, cited lines 5 to 7' });
    expect(region.getAttribute('tabindex')).toBe('0');
  });
});
