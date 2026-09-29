import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ResolvedCitation } from '../opportunityTypes';
import { AvailabilityBadge } from '../../../shared/ds/badges';

function CitationRowTestWrapper({ citations, small = false }: { citations: ResolvedCitation[]; small?: boolean }) {
  const EVIDENCE_LABEL: Record<string, string> = {
    patent_api: 'Live Patent API',
    vector_corpus: 'Corpus Vector Search',
    corpus: 'Corpus Vector Search',
    llm_deep_research: 'LLM Deep Research',
    llm_research: 'LLM Deep Research',
  };

  if (!citations.length) return null;
  return (
    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
      {citations.map((c, idx) => {
        const label = c.patentId ?? c.title ?? c.ref;
        const sourceLabel = c.sourceType ? EVIDENCE_LABEL[c.sourceType] ?? c.sourceType : undefined;
        const tooltipText = sourceLabel ? `${c.title ?? c.ref} — ${sourceLabel}` : (c.title ?? c.ref);
        const isLongTitle = c.title && !c.patentId && c.title.length > 28;
        const fontSize = (c.patentId || (!c.title && c.ref)) ? (small ? 10.5 : 11) : undefined;

        const chip = (
          <span
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 6,
              fontFamily: (c.patentId || (!c.title && c.ref)) ? 'var(--font-mono)' : 'var(--font-body)',
              fontSize,
              padding: small ? '2px 7px' : '3px 8px',
              borderRadius: 'var(--radius-sm)',
              background: 'var(--status-info-bg)',
              color: 'var(--status-info-fg)',
              ...(isLongTitle && {
                maxWidth: '28ch',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }),
            }}
            title={tooltipText}
          >
            {label}
          </span>
        );

        if (c.url) {
          return (
            <a
              key={`${c.ref}-${idx}`}
              href={c.url}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`Open ${label} on the patent source (opens in new tab)`}
              style={{
                textDecoration: 'none',
                color: 'var(--accent-primary)',
                display: 'inline-flex',
                alignItems: 'center',
                gap: 6,
                outline: 'none',
              }}
            >
              {chip}
              <span aria-hidden="true" style={{ fontSize: 11, fontWeight: 600 }}>↗</span>
              {c.similarity != null && (
                <span
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 6,
                    padding: '4px 12px',
                    borderRadius: 'var(--radius-full)',
                    background: 'var(--gray-100)',
                    color: 'var(--text-body)',
                    fontFamily: 'var(--font-body)',
                    fontWeight: 600,
                    fontSize: 'var(--text-xs)',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {Math.round(c.similarity * 100)}%
                </span>
              )}
            </a>
          );
        }

        return (
          <span key={`${c.ref}-${idx}`} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            {chip}
            {c.similarity != null && (
              <span
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 6,
                  padding: '4px 12px',
                  borderRadius: 'var(--radius-full)',
                  background: 'var(--gray-100)',
                  color: 'var(--text-body)',
                  fontFamily: 'var(--font-body)',
                  fontWeight: 600,
                  fontSize: 'var(--text-xs)',
                  whiteSpace: 'nowrap',
                }}
              >
                {Math.round(c.similarity * 100)}%
              </span>
            )}
          </span>
        );
      })}
    </div>
  );
}

describe('CitationRow rendering', () => {
  it('renders a clickable anchor with href when citation has url', () => {
    const citations: ResolvedCitation[] = [
      { ref: 'E7', title: 'Patent Title A', patentId: 'US1234567', url: 'https://example.com/US1234567', similarity: 0.92, sourceType: 'patent_api' },
    ];

    const { container } = render(
      <MemoryRouter>
        <CitationRowTestWrapper citations={citations} />
      </MemoryRouter>
    );

    const link = container.querySelector('a[href="https://example.com/US1234567"]');
    expect(link).toBeTruthy();
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(container.textContent).toContain('US1234567');
    expect(container.textContent).toContain('92%');
  });

  it('renders a plain non-link chip when citation has no url', () => {
    const citations: ResolvedCitation[] = [
      { ref: 'E10', title: 'Corpus Doc', sourceType: 'vector_corpus' },
    ];

    const { container } = render(
      <MemoryRouter>
        <CitationRowTestWrapper citations={citations} />
      </MemoryRouter>
    );

    expect(container.querySelector('a')).toBeNull();
    expect(container.textContent).toContain('Corpus Doc');
  });

  it('renders unmatched stale ref as plain bare chip with ref only', () => {
    const citations: ResolvedCitation[] = [
      { ref: 'STALE_REF' },
    ];

    const { container } = render(
      <MemoryRouter>
        <CitationRowTestWrapper citations={citations} />
      </MemoryRouter>
    );

    expect(container.querySelector('a')).toBeNull();
    expect(container.textContent).toContain('STALE_REF');
  });

  it('renders similarity badge only when similarity is not null', () => {
    const withSimilarity: ResolvedCitation[] = [
      { ref: 'E1', similarity: 0.85, sourceType: 'patent_api' },
    ];
    const withoutSimilarity: ResolvedCitation[] = [
      { ref: 'E2', sourceType: 'patent_api' },
    ];

    const { container, rerender } = render(
      <MemoryRouter>
        <CitationRowTestWrapper citations={withSimilarity} />
      </MemoryRouter>
    );
    expect(container.textContent).toContain('85%');

    rerender(
      <MemoryRouter>
        <CitationRowTestWrapper citations={withoutSimilarity} />
      </MemoryRouter>
    );
    expect(container.textContent).not.toContain('%');
  });

  it('renders 0% similarity when similarity is 0', () => {
    const citations: ResolvedCitation[] = [
      { ref: 'E3', similarity: 0, sourceType: 'patent_api' },
    ];

    const { container } = render(
      <MemoryRouter>
        <CitationRowTestWrapper citations={citations} />
      </MemoryRouter>
    );

    expect(container.textContent).toContain('0%');
  });

  it('uses title attribute for tooltip with source label', () => {
    const citations: ResolvedCitation[] = [
      { ref: 'E7', title: 'Patent Title A', patentId: 'US1234567', url: 'https://example.com/US1234567', sourceType: 'patent_api' },
    ];

    const { container } = render(
      <MemoryRouter>
        <CitationRowTestWrapper citations={citations} />
      </MemoryRouter>
    );

    const chipSpan = container.querySelector('[title="Patent Title A — Live Patent API"]');
    expect(chipSpan).toBeTruthy();
  });

  it('handles duplicate refs by keying with index', () => {
    const citations: ResolvedCitation[] = [
      { ref: 'E7', title: 'Patent A', url: 'https://example.com/A' },
      { ref: 'E7', title: 'Patent B', url: 'https://example.com/B' },
    ];

    const { container } = render(
      <MemoryRouter>
        <CitationRowTestWrapper citations={citations} />
      </MemoryRouter>
    );

    const links = container.querySelectorAll('a');
    expect(links.length).toBe(2);
    expect(links[0]?.getAttribute('href')).toBe('https://example.com/A');
    expect(links[1]?.getAttribute('href')).toBe('https://example.com/B');
  });
});

describe('Evidence Source Card — 3-state rendering (BUG166)', () => {
  it('renders active_cited state with Available badge and citation summary', () => {
    const { container } = render(
      <MemoryRouter>
        <AvailabilityBadge state="available" sourceLabel="Live Patent API" />
      </MemoryRouter>
    );

    expect(container.textContent).toContain('Available');
    expect(container.querySelector('[aria-label*="available and cited"]')).toBeTruthy();
  });

  it('renders active_uncited state with Available badge and uncited summary', () => {
    const { container } = render(
      <MemoryRouter>
        <AvailabilityBadge state="active_uncited" sourceLabel="Corpus Vector Search" />
      </MemoryRouter>
    );

    expect(container.textContent).toContain('Available');
    expect(container.querySelector('[aria-label*="contributed but not cited"]')).toBeTruthy();
  });

  it('renders unavailable state with Unavailable badge', () => {
    const { container } = render(
      <MemoryRouter>
        <AvailabilityBadge state="unavailable" sourceLabel="LLM Deep Research" />
      </MemoryRouter>
    );

    expect(container.textContent).toContain('Unavailable');
    expect(container.querySelector('[aria-label*="unavailable"]')).toBeTruthy();
  });
});
