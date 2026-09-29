import { Document, Page, Text, View, StyleSheet } from '@react-pdf/renderer';
import type { AxisScore, OpportunityDetail, AxisName, ResolvedCitation } from '../../opportunity/opportunityTypes';
import {
  truncate,
  humanizeAxis,
  humanizeSourceType,
  humanizeRecommendation,
  recommendationColor,
} from './pdfHelpers';
import { PDF_COLORS, pdfBaseStyles } from './pdfStyles';
import { PdfPageFooter } from './PdfPageFooter';

const AXIS_ORDER: AxisName[] = [
  'novelty',
  'non_obviousness',
  'utility',
  'enablement',
  'claim_clarity',
];

const styles = StyleSheet.create({
  h1: {
    fontFamily: 'Helvetica-Bold',
    fontSize: 22,
    color: PDF_COLORS.textPrimary,
    marginBottom: 8,
  },
  largeScore: {
    fontFamily: 'Helvetica-Bold',
    fontSize: 28,
    color: PDF_COLORS.textPrimary,
  },
  hairlineAfterH2: {
    borderBottomWidth: 0.5,
    borderBottomColor: PDF_COLORS.divider,
    borderBottomStyle: 'solid',
    marginBottom: 10,
    marginTop: 4,
  },
  sectionGap: {
    marginTop: 20,
  },
  blockGap: {
    marginTop: 12,
  },
  scoreRow: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    marginBottom: 4,
    gap: 12,
  },
  recBlock: {
    flexDirection: 'column',
    justifyContent: 'flex-end',
    marginBottom: 4,
  },
  axisHeaderRow: {
    flexDirection: 'row',
    marginBottom: 4,
  },
  axisRow: {
    flexDirection: 'row',
    paddingVertical: 4,
  },
  axisName: {
    width: '28%',
    fontFamily: 'Helvetica-Bold',
    fontSize: 10,
    color: PDF_COLORS.textPrimary,
  },
  axisScore: {
    width: '12%',
    fontFamily: 'Helvetica',
    fontSize: 10,
    color: PDF_COLORS.textPrimary,
    textAlign: 'right',
  },
  axisReasoning: {
    width: '60%',
    paddingLeft: 8,
  },
  excerptBlock: {
    borderLeftWidth: 2,
    borderLeftColor: PDF_COLORS.brandAccent,
    borderLeftStyle: 'solid',
    paddingLeft: 12,
    marginTop: 8,
  },
});

interface OpportunityPdfDocumentProps {
  detail: OpportunityDetail;
  batchId: string;
  exportDate: string;
}

function CandidateHeader({
  detail,
  batchId,
  exportDate,
}: {
  detail: OpportunityDetail;
  batchId: string;
  exportDate: string;
}) {
  return (
    <View>
      <Text style={pdfBaseStyles.coverWordmark}>Cortexa</Text>
      <Text style={styles.h1}>{detail.title}</Text>
      <View style={styles.scoreRow}>
        <Text style={styles.largeScore}>{`${detail.overallScore}/100`}</Text>
        <View style={styles.recBlock}>
          <Text style={pdfBaseStyles.caption}>Recommendation</Text>
          <Text
            style={{
              fontFamily: 'Helvetica-Bold',
              fontSize: 11,
              color: recommendationColor(detail.recommendation),
            }}
          >
            {humanizeRecommendation(detail.recommendation)}
          </Text>
        </View>
      </View>
      <Text style={pdfBaseStyles.caption}>
        {`Candidate ID: ${detail.candidateId}  •  Batch: ${batchId}  •  Exported: ${exportDate}`}
      </Text>
      <View style={pdfBaseStyles.coverHairline} />
    </View>
  );
}

function AbstractAndClaim({ detail }: { detail: OpportunityDetail }) {
  return (
    <View>
      <Text style={pdfBaseStyles.h3}>Abstract</Text>
      <Text style={{ ...pdfBaseStyles.body, marginTop: 4 }}>{detail.abstract}</Text>
      {detail.claimDraft && (
        <>
          <View style={styles.blockGap} />
          <Text style={pdfBaseStyles.h3}>Draft Claim</Text>
          <Text style={{ ...pdfBaseStyles.body, marginTop: 4 }}>{detail.claimDraft}</Text>
        </>
      )}
    </View>
  );
}

function AxesSection({ detail }: { detail: OpportunityDetail }) {
  const orderedAxes = AXIS_ORDER.map((name) =>
    detail.axisScores.find((a) => a.axis === name),
  ).filter(Boolean) as OpportunityDetail['axisScores'];

  return (
    <View style={styles.sectionGap}>
      <Text style={pdfBaseStyles.h2}>Patentability Axes</Text>
      <View style={styles.hairlineAfterH2} />
      <View style={styles.axisHeaderRow}>
        <Text style={{ ...pdfBaseStyles.caption, fontFamily: 'Helvetica-Bold', width: '28%' }}>
          Axis
        </Text>
        <Text
          style={{
            ...pdfBaseStyles.caption,
            fontFamily: 'Helvetica-Bold',
            width: '12%',
            textAlign: 'right',
          }}
        >
          Score
        </Text>
        <Text
          style={{ ...pdfBaseStyles.caption, fontFamily: 'Helvetica-Bold', width: '60%', paddingLeft: 8 }}
        >
          Reasoning
        </Text>
      </View>
      <View style={pdfBaseStyles.hairline} />
      {orderedAxes.map((axis) => (
        <View wrap={false} key={axis.axis} style={styles.axisRow}>
          <Text style={styles.axisName}>{humanizeAxis(axis.axis)}</Text>
          <Text style={styles.axisScore}>{`${axis.score}/100`}</Text>
          <View style={styles.axisReasoning}>
            <Text style={pdfBaseStyles.body}>{truncate(axis.reasoning ?? '', 200)}</Text>
            {axis.citations.length > 0 && (
              <Text style={{ ...pdfBaseStyles.caption, marginTop: 2 }}>
                {axis.citations.join('  ·  ')}
              </Text>
            )}
          </View>
        </View>
      ))}
    </View>
  );
}

function EvidenceSection({ detail }: { detail: OpportunityDetail }) {
  return (
    <View style={styles.sectionGap}>
      <Text style={pdfBaseStyles.h2}>Evidence Triangulation</Text>
      <View style={styles.hairlineAfterH2} />
      {detail.evidenceSources.map((src, i) => (
        <View wrap={false} key={src.sourceType} style={pdfBaseStyles.rowContainer}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
            <Text style={pdfBaseStyles.h3}>{humanizeSourceType(src.sourceType)}</Text>
            <Text
              style={{
                fontFamily: 'Helvetica-Bold',
                fontSize: 8,
                color: src.available ? PDF_COLORS.positive : PDF_COLORS.muted,
              }}
            >
              {src.available ? 'Available' : 'Not available'}
            </Text>
            <Text style={pdfBaseStyles.caption}>{`${Math.round(src.confidence * 100)}/100`}</Text>
          </View>
          <Text style={{ ...pdfBaseStyles.body, marginTop: 4 }}>{truncate(src.summary, 240)}</Text>
          {src.citations.length > 0 && (
            <Text style={{ ...pdfBaseStyles.caption, marginTop: 2 }}>
              {src.citations.join('  ·  ')}
            </Text>
          )}
          {i < detail.evidenceSources.length - 1 && (
            <View style={{ ...pdfBaseStyles.hairline, marginTop: 10 }} />
          )}
        </View>
      ))}
    </View>
  );
}

function ProvenanceSection({ detail }: { detail: OpportunityDetail }) {
  const p = detail.provenance;

  if (!p) {
    return (
      <View style={styles.sectionGap}>
        <Text style={pdfBaseStyles.h2}>Source Provenance</Text>
        <View style={styles.hairlineAfterH2} />
        <Text style={pdfBaseStyles.caption}>No provenance data available.</Text>
      </View>
    );
  }

  return (
    <View style={styles.sectionGap}>
      <Text style={pdfBaseStyles.h2}>Source Provenance</Text>
      <View style={styles.hairlineAfterH2} />
      <Text style={pdfBaseStyles.caption}>
        {`File: ${p.sourceFilename ?? p.sourceDocumentId}  •  Page ${p.pageNumber ?? '—'}  •  Span ${p.spanStart ?? '—'}–${p.spanEnd ?? '—'}`}
      </Text>
      {p.excerptText && (
        <View style={styles.excerptBlock}>
          <Text style={pdfBaseStyles.body}>{p.excerptText}</Text>
        </View>
      )}
    </View>
  );
}

function citationLabel(citation: ResolvedCitation): string {
  return citation.patentId ?? citation.title ?? citation.ref;
}

function NoveltyDeltaSection({ detail }: { detail: OpportunityDetail }) {
  return (
    <View style={styles.sectionGap}>
      <Text style={pdfBaseStyles.h2}>What&apos;s New Beyond Your Document</Text>
      <View style={styles.hairlineAfterH2} />
      <Text style={pdfBaseStyles.body}>
        {detail.noveltyDelta && detail.noveltyDelta.trim() !== ''
          ? detail.noveltyDelta
          : 'No novelty delta was recorded for this opportunity.'}
      </Text>
    </View>
  );
}

function GroundedInSection({ detail }: { detail: OpportunityDetail }) {
  const excerpts = detail.groundedIn?.excerpts ?? [];
  return (
    <View style={styles.sectionGap}>
      <Text style={pdfBaseStyles.h2}>Built From Your Document</Text>
      <View style={styles.hairlineAfterH2} />
      {excerpts.length === 0 ? (
        <Text style={pdfBaseStyles.caption}>No source passages were recorded for this opportunity.</Text>
      ) : (
        excerpts.map((e, i) => (
          <View wrap={false} key={`${e.chunkId}-${i}`} style={styles.blockGap}>
            <Text style={pdfBaseStyles.h3}>
              {e.sectionLabel && e.sectionLabel.trim() !== '' ? e.sectionLabel : `Source passage ${i + 1}`}
            </Text>
            <View style={styles.excerptBlock}>
              <Text style={pdfBaseStyles.body}>{e.text}</Text>
            </View>
          </View>
        ))
      )}
    </View>
  );
}

function PriorArtSection({ detail }: { detail: OpportunityDetail }) {
  const items = detail.priorArtProximity ?? [];
  return (
    <View style={styles.sectionGap}>
      <Text style={pdfBaseStyles.h2}>Closest Prior Art</Text>
      <View style={styles.hairlineAfterH2} />
      {items.length === 0 ? (
        <Text style={pdfBaseStyles.caption}>
          {detail.landscapeCorpusOnly
            ? 'Prior-art landscape was corpus-only for this batch — no live patent matches were retrieved.'
            : 'No close prior art was found for this concept.'}
        </Text>
      ) : (
        items.map((item, i) => (
          <View wrap={false} key={`${item.reference}-${i}`} style={pdfBaseStyles.rowContainer}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
              <Text style={pdfBaseStyles.h3}>{item.title || item.reference}</Text>
              <Text style={pdfBaseStyles.caption}>{`${Math.round(item.relevanceScore * 100)}%`}</Text>
            </View>
            <Text style={pdfBaseStyles.caption}>
              {item.isCorpus ? 'Corpus match — no external link' : `Live match — ${item.source}`}
            </Text>
            {i < items.length - 1 && <View style={{ ...pdfBaseStyles.hairline, marginTop: 8 }} />}
          </View>
        ))
      )}
    </View>
  );
}

function SeedingAxisRow({ axis }: { axis: AxisScore }) {
  return (
    <View wrap={false} style={styles.axisRow}>
      <Text style={styles.axisName}>{humanizeAxis(axis.axis)}</Text>
      <Text style={styles.axisScore}>{`${axis.score}/100`}</Text>
      <View style={styles.axisReasoning}>
        {axis.citations.length > 0 && (
          <Text style={pdfBaseStyles.caption}>{axis.citations.map(citationLabel).join('  ·  ')}</Text>
        )}
      </View>
    </View>
  );
}

function SeedingAxesSection({ detail }: { detail: OpportunityDetail }) {
  if (detail.axisScores.length === 0) return null;
  return (
    <View style={styles.sectionGap}>
      <Text style={pdfBaseStyles.h2}>Patentability Axes</Text>
      <View style={styles.hairlineAfterH2} />
      {detail.axisScores.map((axis) => (
        <SeedingAxisRow key={axis.axis} axis={axis} />
      ))}
    </View>
  );
}

function SeedingBody({ detail }: { detail: OpportunityDetail }) {
  return (
    <>
      <NoveltyDeltaSection detail={detail} />
      <GroundedInSection detail={detail} />
      <PriorArtSection detail={detail} />
      <SeedingAxesSection detail={detail} />
    </>
  );
}

function HarvestingBody({ detail }: { detail: OpportunityDetail }) {
  return (
    <>
      <AxesSection detail={detail} />
      <EvidenceSection detail={detail} />
      <ProvenanceSection detail={detail} />
    </>
  );
}

export function OpportunityPdfDocument({
  detail,
  batchId,
  exportDate,
}: OpportunityPdfDocumentProps) {
  const isSeeding = detail.source === 'seeding';
  return (
    <Document>
      <Page size="A4" style={pdfBaseStyles.page}>
        <CandidateHeader detail={detail} batchId={batchId} exportDate={exportDate} />
        <AbstractAndClaim detail={detail} />
        {isSeeding ? <SeedingBody detail={detail} /> : <HarvestingBody detail={detail} />}
        <PdfPageFooter />
      </Page>
    </Document>
  );
}
