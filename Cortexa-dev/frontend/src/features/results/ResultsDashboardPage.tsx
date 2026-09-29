import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader, SectionCard } from '../../shared/layout/PageHeader';
import { Button, Tabs, StatCard, EmptyState, ErrorState, Skeleton, Spinner, IconChevronRight } from '../../shared/ds';
import { MaturityBadge, RecommendationBadge, SeedingModeBadge } from '../../shared/ds/badges';
import { useToast } from '../../shared/ds/Toast';
import { useSession } from '../../core/auth/useSession';
import { useResultsData } from './useResultsData';
import { toRankedCandidates, toCategorizedOpportunities } from './resultsMappers';
import { SeedingOverview } from './SeedingOverview';
import { setResults } from '../opportunity/resultsStore';
import { useExport } from '../export';
import type { RankedCandidate, CategorizedOpportunity } from './resultsTypes';

const FILTERS = ['All', 'Mature', 'Emerging', 'Score > 80'];

function applyFilter(cands: RankedCandidate[], filter: string): RankedCandidate[] {
  switch (filter) {
    case 'Mature':
      return cands.filter((c) => c.maturity === 'mature');
    case 'Emerging':
      return cands.filter((c) => c.maturity === 'emerging');
    case 'Score > 80':
      return cands.filter((c) => c.patentability_score > 80);
    default:
      return cands;
  }
}

function mean(values: number[]): number {
  return values.length ? Math.round(values.reduce((a, v) => a + v, 0) / values.length) : 0;
}

function seedingEmptyContent(seedingOnly: boolean, explanation: string | undefined): { title: string; message: string } {
  if (!seedingOnly) {
    return {
      title: 'No patentable candidates found',
      message: "This batch completed but didn't surface any invention signals above threshold.",
    };
  }
  return {
    title: 'No new opportunities were proposed.',
    message: explanation && explanation.trim() !== ''
      ? explanation
      : 'Seeding ran but found no patentable ideas beyond what your document already contains.',
  };
}

type RenderPhase = 'error' | 'pending' | 'loading' | 'empty' | 'results';

function selectRenderPhase(hasError: boolean, terminal: boolean, isLoading: boolean, hasResults: boolean): RenderPhase {
  if (hasError) return 'error';
  if (!terminal) return 'pending';
  if (isLoading) return 'loading';
  if (!hasResults) return 'empty';
  return 'results';
}

function HarvestStatCards({ candidates }: { candidates: RankedCandidate[] }) {
  const avgScore = mean(candidates.map((c) => c.patentability_score));
  const mature = candidates.filter((c) => c.maturity === 'mature').length;
  const emerging = candidates.filter((c) => c.maturity === 'emerging').length;
  return (
    <>
      <StatCard label="Candidates Found" value={String(candidates.length)} sublabel="this batch" />
      <StatCard label="Avg Patentability" value={`${avgScore}/100`} sublabel="across all candidates" />
      <StatCard label="Mature Signals" value={String(mature).padStart(2, '0')} sublabel="ready for drafting" />
      <StatCard label="Emerging Signals" value={String(emerging).padStart(2, '0')} sublabel="need more engineering" />
    </>
  );
}

function SeedingStatCards({ opportunities }: { opportunities: CategorizedOpportunity[] }) {
  const avgConfidence = mean(opportunities.map((o) => o.confidence_score));
  const whitespace = opportunities.filter((o) => o.category === 'whitespace').length;
  const adjacent = opportunities.filter((o) => o.category === 'adjacent').length;
  return (
    <>
      <StatCard label="Opportunities Found" value={String(opportunities.length)} sublabel="this batch" />
      <StatCard label="Avg Confidence" value={`${avgConfidence}/100`} sublabel="across all opportunities" />
      <StatCard label="Whitespace" value={String(whitespace).padStart(2, '0')} sublabel="open patent space" />
      <StatCard label="Adjacent" value={String(adjacent).padStart(2, '0')} sublabel="extend existing IP" />
    </>
  );
}

interface HarvestingListProps {
  candidates: RankedCandidate[];
  filter: string;
  onFilterChange: (f: string) => void;
  onOpen: (id: string) => void;
}

function HarvestingList({ candidates, filter, onFilterChange, onOpen }: HarvestingListProps) {
  const filtered = applyFilter(candidates, filter);
  return (
    <>
      <div style={{ fontSize: 13, color: 'var(--text-muted)', marginTop: 12 }}>
        Found {candidates.length} candidate{candidates.length === 1 ? '' : 's'}.
      </div>
      <div style={{ marginTop: 8 }}>
        <Tabs tabs={FILTERS} active={filter} onChange={onFilterChange} />
      </div>
      <div style={{ marginTop: 8 }}>
        {filtered.length === 0 ? (
          <div style={{ padding: '32px 0', textAlign: 'center', color: 'var(--text-muted)' }}>No candidates match this filter.</div>
        ) : (
          filtered.map((c, i) => (
            <div
              key={c.id}
              onClick={() => onOpen(c.id)}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: '18px 0',
                borderBottom: i === filtered.length - 1 ? 'none' : '1px solid var(--border-subtle)',
                cursor: 'pointer',
                gap: 12,
              }}
            >
              <div style={{ minWidth: 0 }}>
                <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginBottom: 6 }}>{c.title}</div>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                  <MaturityBadge maturity={c.maturity} />
                </div>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexShrink: 0 }}>
                <RecommendationBadge recommendation={c.recommendation} />
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent-primary)' }}>
                  {Math.round(c.patentability_score)}
                </span>
                <span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}>
                  <IconChevronRight size={16} />
                </span>
              </div>
            </div>
          ))
        )}
      </div>
    </>
  );
}

function SeedingList({ opportunities, onOpen }: { opportunities: CategorizedOpportunity[]; onOpen: (id: string) => void }) {
  return (
    <div style={{ marginTop: 16, display: 'flex', flexDirection: 'column', gap: 14 }}>
      {opportunities.map((o) => (
        <div
          key={o.id}
          onClick={() => onOpen(o.id)}
          style={{ border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-md)', padding: 18, cursor: 'pointer' }}
        >
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
            <div style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{o.title}</div>
            <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent-primary)' }}>
              {Math.round(o.confidence_score)}%
            </span>
          </div>
          <div style={{ fontSize: 13, color: 'var(--text-body)', marginBottom: 8 }}>{o.description}</div>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{o.roadmap_alignment}</div>
        </div>
      ))}
    </div>
  );
}

export function ResultsDashboardPage() {
  const { batchId } = useParams<{ batchId: string }>();
  const navigate = useNavigate();
  const { show } = useToast();
  const { hasPermission } = useSession();
  const canExport = hasPermission('reports:export');
  const { exportReport } = useExport();

  const state = useResultsData(batchId ?? '');
  const [engineTab, setEngineTab] = useState('Harvesting');
  const [filter, setFilter] = useState('All');

  const candidates = useMemo<RankedCandidate[]>(
    () => (state.harvesting ? toRankedCandidates(state.harvesting.candidates, state.harvesting.verdicts) : []),
    [state.harvesting]
  );
  const opportunities = useMemo<CategorizedOpportunity[]>(
    () => (state.seeding ? toCategorizedOpportunities(state.seeding.opportunities, state.seeding.source_flags) : []),
    [state.seeding]
  );

  // Feed the shared store so Opportunity Detail can resolve by id.
  useEffect(() => {
    if (batchId && (candidates.length || opportunities.length)) {
      setResults(batchId, candidates, opportunities);
    }
  }, [batchId, candidates, opportunities]);

  const bs = state.batchStatus;
  const wantsHarvesting = bs?.wants_harvesting ?? candidates.length > 0;
  const wantsSeeding = bs?.wants_seeding ?? opportunities.length > 0;
  const terminal = bs ? ['Completed', 'Failed', 'PartiallyFailed', 'Cancelled', 'NoCandidates'].includes(bs.status) : false;

  function runExport(format: 'pdf' | 'json') {
    if (!batchId || (!state.harvesting && !state.seeding)) return;
    const data = {
      harvesting: state.harvesting ?? { id: '', batch_id: batchId, candidates: [], verdicts: [], summary: '', created_at: '' },
      seeding: state.seeding ?? { id: '', batch_id: batchId, opportunities: [], created_at: '' },
    };
    void exportReport(data, batchId, format).then(() =>
      show({ variant: 'success', title: `Report exported as ${format.toUpperCase()}.` })
    );
  }

  const openDetail = (id: string) => navigate(`/batches/${batchId}/results/${id}`);
  const showHarvest = wantsHarvesting && (engineTab === 'Harvesting' || !wantsSeeding);
  const hasResults = candidates.length > 0 || opportunities.length > 0;
  const dualEngine = wantsHarvesting && wantsSeeding;
  const phase = selectRenderPhase(state.error !== null, terminal, state.isLoading, hasResults);
  const conceptMap = state.seeding?.concept_map ?? [];
  const showSeedingOverview = !showHarvest && conceptMap.length > 0;
  const seedingOnly = wantsSeeding && !wantsHarvesting;
  const emptyContent = seedingEmptyContent(seedingOnly, state.seeding?.explanation);

  return (
    <AppShell>
      <PageHeader
        title="Invention Results"
        subtitle={bs?.batch_name ?? batchId}
        actions={
          canExport && hasResults ? (
            <div style={{ display: 'flex', gap: 8 }}>
              <Button variant="secondary" size="sm" onClick={() => runExport('pdf')} data-testid="export-pdf-button">
                Export PDF
              </Button>
              <Button variant="secondary" size="sm" onClick={() => runExport('json')} data-testid="export-json-button">
                Export JSON
              </Button>
            </div>
          ) : undefined
        }
      />

      {phase === 'error' && state.error && (
        <ErrorState title="This batch failed — no results were generated." message={state.error.message} correlationId={state.error.correlationId} onRetry={() => navigate(0)} />
      )}

      {phase === 'pending' && (
        <SectionCard>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, color: 'var(--text-muted)', fontSize: 14 }}>
            <Spinner size={16} /> Waiting for the batch to finish — results appear here automatically.
          </div>
        </SectionCard>
      )}

      {phase === 'loading' && (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 16 }}>
            {Array.from({ length: 4 }).map((_, i) => (
              <StatCard key={i} label="" value="" loading />
            ))}
          </div>
          <Skeleton height={240} radius="var(--radius-lg)" />
        </>
      )}

      {phase === 'empty' && (
        <EmptyState
          title={emptyContent.title}
          message={emptyContent.message}
          action={hasPermission('jobs:submit') ? { label: 'Start a New Analysis', onClick: () => navigate('/batches/new') } : undefined}
        />
      )}

      {phase === 'results' && (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 16 }} data-testid="results-stat-cards">
            {showHarvest ? <HarvestStatCards candidates={candidates} /> : <SeedingStatCards opportunities={opportunities} />}
          </div>

          {showSeedingOverview && <SeedingOverview conceptMap={conceptMap} opportunities={opportunities} />}

          <SectionCard>
            {(dualEngine || !showHarvest) && (
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
                {dualEngine ? <Tabs tabs={['Harvesting', 'Seeding']} active={engineTab} onChange={setEngineTab} /> : <span />}
                {!showHarvest && <SeedingModeBadge mode={state.seeding?.seeding_mode ?? 'legacy'} />}
              </div>
            )}

            <div data-testid={showHarvest ? 'harvesting-results' : 'seeding-results'}>
              {showHarvest ? (
                <HarvestingList candidates={candidates} filter={filter} onFilterChange={setFilter} onOpen={openDetail} />
              ) : (
                <SeedingList opportunities={opportunities} onOpen={openDetail} />
              )}
            </div>
          </SectionCard>
        </>
      )}
    </AppShell>
  );
}
