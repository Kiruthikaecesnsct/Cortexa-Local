import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ResultsDashboardPage } from '../ResultsDashboardPage';
import * as useResultsDataModule from '../useResultsData';
import * as useSessionModule from '../../../core/auth/useSession';
import * as exportModule from '../../export';
import type { ResultsState } from '../useResultsData';
import type { HarvestingResultDto, SeedingResultDto } from '../../../core/api/types';
import type { JobStatusDto } from '../../jobs/jobTypes';

vi.mock('../useResultsData');
vi.mock('../../../core/auth/useSession');
vi.mock('../../export');
vi.mock('../../../shared/ds/Toast', () => ({ useToast: () => ({ show: vi.fn() }) }));
vi.mock('../../../shared/layout/AppShell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

const navigateMock = vi.fn();
vi.mock('react-router-dom', () => ({
  useNavigate: () => navigateMock,
  useParams: () => ({ batchId: 'batch-1' }),
}));

const mockedUseResultsData = vi.mocked(useResultsDataModule.useResultsData);
const mockedUseSession = vi.mocked(useSessionModule.useSession);
const mockedUseExport = vi.mocked(exportModule.useExport);
const exportReportMock = vi.fn().mockResolvedValue(undefined);

function seedingResult(count: number): SeedingResultDto {
  return {
    id: 'seed-1',
    batch_id: 'batch-1',
    created_at: '2026-07-09T00:00:00Z',
    opportunities: Array.from({ length: count }, (_, i) => ({
      id: `opp-${i}`,
      title: `Opportunity ${i}`,
      description: `Description ${i}`,
      confidence_score: 60 + i,
      roadmap_alignment: i === 0 ? 'whitespace expansion' : 'adjacent extension',
    })),
  };
}

function harvestingResult(count: number): HarvestingResultDto {
  return {
    id: 'harv-1',
    batch_id: 'batch-1',
    summary: '',
    created_at: '2026-07-09T00:00:00Z',
    candidates: Array.from({ length: count }, (_, i) => ({
      id: `cand-${i}`,
      title: `Candidate ${i}`,
      abstract: 'abstract',
      claim_draft: '',
      novelty_hypothesis: '',
      source_asset_id: 'asset-1',
      batch_id: 'batch-1',
      created_at: '2026-07-09T00:00:00Z',
      weighted_score: 85,
      maturity: 'Mature',
      rank: i + 1,
    })),
    verdicts: [],
  };
}

function status(overrides: Partial<JobStatusDto>): JobStatusDto {
  return {
    batch_id: 'batch-1',
    batch_name: 'e2e-batch',
    status: 'Completed',
    documents: [],
    ...overrides,
  };
}

function stubState(overrides: Partial<ResultsState>): void {
  mockedUseResultsData.mockReturnValue({
    harvesting: null,
    seeding: null,
    batchStatus: null,
    isLoading: false,
    error: null,
    ...overrides,
  });
}

describe('ResultsDashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedUseSession.mockReturnValue({
      user: null,
      hasPermission: () => true,
      hasAnyRole: () => false,
      isSuperAdmin: false,
      isAuthenticated: true,
    });
    mockedUseExport.mockReturnValue({
      isExporting: false,
      exportReport: exportReportMock,
      exportOpportunity: vi.fn(),
    });
  });

  it('seeding-only batch renders one card per opportunity, not the harvesting empty state', () => {
    stubState({
      seeding: seedingResult(3),
      batchStatus: status({ wants_harvesting: false, wants_seeding: true }),
    });

    render(<ResultsDashboardPage />);

    expect(screen.getByText('Opportunity 0')).toBeTruthy();
    expect(screen.getByText('Opportunity 1')).toBeTruthy();
    expect(screen.getByText('Opportunity 2')).toBeTruthy();
    expect(screen.queryByText('No candidates match this filter.')).toBeNull();
    expect(screen.queryByText(/Found 0 candidate/)).toBeNull();
  });

  it('seeding-only batch shows opportunity-oriented stat cards', () => {
    stubState({
      seeding: seedingResult(3),
      batchStatus: status({ wants_harvesting: false, wants_seeding: true }),
    });

    render(<ResultsDashboardPage />);

    expect(screen.getByText('Opportunities Found')).toBeTruthy();
    expect(screen.getByText('Avg Confidence')).toBeTruthy();
    expect(screen.queryByText('Candidates Found')).toBeNull();
    expect(screen.queryByText('Avg Patentability')).toBeNull();
    // Category counts derive from roadmap_alignment: 1 whitespace, 2 adjacent.
    expect(screen.getByText('Whitespace')).toBeTruthy();
    expect(screen.getByText('01')).toBeTruthy();
    expect(screen.getByText('02')).toBeTruthy();
  });

  it('seeding-only batch enables export and passes a harvesting fallback', () => {
    stubState({
      seeding: seedingResult(2),
      batchStatus: status({ wants_harvesting: false, wants_seeding: true }),
    });

    render(<ResultsDashboardPage />);

    const exportBtn = screen.getByText('Export JSON');
    fireEvent.click(exportBtn);

    expect(exportReportMock).toHaveBeenCalledTimes(1);
    const [data, batchId, format] = exportReportMock.mock.calls[0] ?? [];
    expect(batchId).toBe('batch-1');
    expect(format).toBe('json');
    expect(data?.harvesting.candidates).toEqual([]);
    expect(data?.seeding.opportunities.length).toBe(2);
  });

  it('harvesting-only batch still renders the harvesting branch unchanged', () => {
    stubState({
      harvesting: harvestingResult(2),
      batchStatus: status({ wants_harvesting: true, wants_seeding: false }),
    });

    render(<ResultsDashboardPage />);

    expect(screen.getByText('Candidates Found')).toBeTruthy();
    expect(screen.getByText('Avg Patentability')).toBeTruthy();
    expect(screen.getByText(/Found 2 candidates\./)).toBeTruthy();
    expect(screen.getByText('Candidate 0')).toBeTruthy();
    expect(screen.queryByText('Opportunities Found')).toBeNull();
  });

  it('renders the concept map overview above the seeding list when concept_map is present', () => {
    const seeding = seedingResult(2);
    seeding.concept_map = [
      { concept: 'Cache Eviction', density: 0.8, corpus_axis: 0.9, live_axis: 0.2, opportunity_ids: ['opp-0'], whitespace: true },
    ];
    stubState({
      seeding,
      batchStatus: status({ wants_harvesting: false, wants_seeding: true }),
    });

    render(<ResultsDashboardPage />);

    expect(screen.getByText('Opportunity map')).toBeTruthy();
    expect(screen.getByText('Cache Eviction')).toBeTruthy();
    expect(screen.getByLabelText('Corpus landscape: crowded')).toBeTruthy();
    expect(screen.getByLabelText('Live landscape: sparse')).toBeTruthy();
  });

  it('omits the concept map overview when concept_map is absent', () => {
    stubState({
      seeding: seedingResult(2),
      batchStatus: status({ wants_harvesting: false, wants_seeding: true }),
    });

    render(<ResultsDashboardPage />);

    expect(screen.queryByText('Opportunity map')).toBeNull();
    expect(screen.getByText('Opportunity 0')).toBeTruthy();
  });

  it('shows a seeding-specific empty state using the server explanation', () => {
    const seeding = seedingResult(0);
    seeding.is_empty = true;
    seeding.explanation = 'The document already patents every idea we could find.';
    stubState({
      seeding,
      batchStatus: status({ wants_harvesting: false, wants_seeding: true }),
    });

    render(<ResultsDashboardPage />);

    expect(screen.getByText('No new opportunities were proposed.')).toBeTruthy();
    expect(screen.getByText('The document already patents every idea we could find.')).toBeTruthy();
  });

  it('dual-engine batch defaults to the harvesting view and offers the engine switch', () => {
    stubState({
      harvesting: harvestingResult(2),
      seeding: seedingResult(2),
      batchStatus: status({ wants_harvesting: true, wants_seeding: true }),
    });

    render(<ResultsDashboardPage />);

    expect(screen.getByText('Candidates Found')).toBeTruthy();
    expect(screen.getByText('Seeding')).toBeTruthy();
    expect(screen.getByText(/Found 2 candidates\./)).toBeTruthy();
  });
});
