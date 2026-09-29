import { useNavigate, useParams } from 'react-router-dom';
import { AppShell } from '../../shared/layout/AppShell';
import { Button, ScoreGauge, Skeleton, EmptyState, ErrorState } from '../../shared/ds';
import { RecommendationBadge, CategoryBadge } from '../../shared/ds/badges';
import { useToast } from '../../shared/ds/Toast';
import { useSession } from '../../core/auth/useSession';
import { useOpportunityDetail } from './useOpportunityDetail';
import { useExport } from '../export';
import { AxisScoreRow, EvidenceSourceCard, ClaimDraftPanel, ProvenancePanel, EVIDENCE_GRID_CSS } from './OpportunityDetailComponents';
import { SeedingDetailSections, RoadmapAlignmentCard } from './SeedingDetailSections';

export function OpportunityDetailPage() {
  const { batchId, candidateId } = useParams<{ batchId: string; candidateId: string }>();
  const navigate = useNavigate();
  const { show } = useToast();
  const { hasPermission } = useSession();
  const canExport = hasPermission('reports:export');
  const { exportOpportunity } = useExport();
  const { detail, isLoading, error, notInResults } = useOpportunityDetail(batchId ?? '', candidateId ?? '');

  const back = () => navigate(`/batches/${batchId}/results`);

  function doExport(format: 'pdf' | 'json') {
    if (!detail || !batchId) return;
    void exportOpportunity(detail, batchId, format).then(() =>
      show({ variant: 'success', title: `Verdict exported as ${format.toUpperCase()}.` })
    );
  }

  return (
    <AppShell>
      <a href="#" onClick={(e) => { e.preventDefault(); back(); }} style={{ fontSize: 13, fontWeight: 600, display: 'inline-block', marginBottom: 16 }}>
        ← Back to Results
      </a>

      {isLoading && !detail ? (
        <Skeleton height={320} radius="var(--radius-lg)" />
      ) : notInResults ? (
        <EmptyState title="Candidate not found" message="This candidate isn't part of the current results set." action={{ label: 'Back to Results', onClick: back }} />
      ) : error ? (
        <ErrorState message={error.message} correlationId={error.correlationId} onRetry={back} />
      ) : detail ? (
        <>
          <div style={cardStyle} data-testid="opportunity-detail-header">
            <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 20, flexWrap: 'wrap' }}>
              <div style={{ flex: 1, minWidth: 280 }}>
                <div style={{ fontFamily: 'var(--font-display)', fontSize: 22, color: 'var(--text-primary)', marginBottom: 10 }}>{detail.title}</div>
                <div style={{ fontSize: 14, color: 'var(--text-body)', lineHeight: 1.6 }}>{detail.abstract}</div>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8, flexShrink: 0 }}>
                <ScoreGauge pct={detail.overallScore} />
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', justifyContent: 'center' }}>
                  <RecommendationBadge recommendation={detail.recommendation} />
                  <CategoryBadge category={detail.category} />
                </div>
              </div>
            </div>

            {detail.source !== 'seeding' && <ClaimDraftPanel claimDraft={detail.claimDraft} />}

            {canExport && (
              <div style={{ marginTop: 16, display: 'flex', gap: 8 }}>
                <Button variant="secondary" size="sm" onClick={() => doExport('pdf')} data-testid="detail-export-pdf-button">
                  Export PDF
                </Button>
                <Button variant="secondary" size="sm" onClick={() => doExport('json')} data-testid="detail-export-json-button">
                  Export JSON
                </Button>
              </div>
            )}
          </div>

          {detail.source === 'seeding' && <SeedingDetailSections detail={detail} />}

          {detail.axisScores.length > 0 && (
            <div style={cardStyle}>
              <div style={sectionTitle}>Axis Scores</div>
              <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 16 }}>
                Each patentability axis lists the exact evidence that justifies its score.
              </div>
              {detail.axisScores.map((a, i) => <AxisScoreRow key={a.axis} axis={a} isLast={i === detail.axisScores.length - 1} />)}
            </div>
          )}

          <div style={cardStyle} data-testid="evidence-sources-section">
            <div style={sectionTitle}>Evidence Sources</div>
            <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 16 }}>
              Every score is backed by three independent sources. Each card reflects what actually happened for this candidate.
            </div>
            {detail.degradeNote && (
              <div
                role="status"
                aria-live="polite"
                style={{
                  padding: '10px 12px',
                  background: 'var(--status-info-bg)',
                  borderLeft: '3px solid var(--status-info-fg)',
                  borderRadius: 'var(--radius-md)',
                  fontSize: 12.5,
                  color: 'var(--text-body)',
                  marginBottom: 16,
                }}
              >
                {detail.degradeNote}
              </div>
            )}
            <style>{EVIDENCE_GRID_CSS}</style>
            <div className="evidence-sources-grid">
              {detail.evidenceSources.map((e) => <EvidenceSourceCard key={e.sourceType} source={e} />)}
            </div>
          </div>

          {detail.source === 'seeding' ? (
            <>
              {detail.claimDraft.trim() !== '' && (
                <div style={cardStyle}>
                  <ClaimDraftPanel claimDraft={detail.claimDraft} />
                </div>
              )}
              <RoadmapAlignmentCard roadmapAlignment={detail.roadmapAlignment} />
            </>
          ) : (
            detail.provenance && (
              <div style={cardStyle}>
                <div style={sectionTitle}>Provenance</div>
                <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 16 }}>
                  Trace this candidate back to its exact spot in the source document.
                </div>
                <ProvenancePanel provenance={detail.provenance} recommendation={detail.recommendation} batchId={batchId} />
              </div>
            )
          )}
        </>
      ) : null}
    </AppShell>
  );
}

const cardStyle: React.CSSProperties = {
  background: 'var(--surface-card)',
  border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-lg)',
  boxShadow: 'var(--shadow-xs)',
  padding: 28,
  marginBottom: 20,
};

const sectionTitle: React.CSSProperties = {
  fontWeight: 700,
  fontSize: 18,
  color: 'var(--text-primary)',
  marginBottom: 6,
};
