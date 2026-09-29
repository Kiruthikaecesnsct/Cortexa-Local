import { Document, Page, Text, View, StyleSheet } from '@react-pdf/renderer';
import type { HarvestingResultDto, SeedingResultDto, VerdictDto } from '../../../core/api/types';
import { truncate, humanizeRecommendation, recommendationColor, clampScore } from './pdfHelpers';
import { PDF_COLORS, pdfBaseStyles } from './pdfStyles';
import { PdfPageFooter } from './PdfPageFooter';

const styles = StyleSheet.create({
  h1: {
    fontFamily: 'Helvetica-Bold',
    fontSize: 22,
    color: PDF_COLORS.textPrimary,
    marginBottom: 6,
  },
  h2Row: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    marginBottom: 4,
  },
  h2WithGrow: {
    fontFamily: 'Helvetica-Bold',
    fontSize: 14,
    color: PDF_COLORS.brandAccent,
    flexGrow: 1,
  },
  h2Count: {
    fontFamily: 'Helvetica',
    fontSize: 8,
    color: PDF_COLORS.textSecondary,
    marginBottom: 1,
  },
  hairlineAfterH2: {
    borderBottomWidth: 0.5,
    borderBottomColor: PDF_COLORS.divider,
    borderBottomStyle: 'solid',
    marginBottom: 10,
  },
  sectionGap: {
    marginTop: 24,
  },
  rowMeta: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    marginTop: 2,
    marginBottom: 4,
    gap: 8,
  },
});

interface BatchReportDocumentProps {
  harvesting: HarvestingResultDto;
  seeding: SeedingResultDto;
  batchId: string;
  exportDate: string;
}

function buildVerdictMap(harvesting: HarvestingResultDto): Map<string, VerdictDto> {
  const map = new Map<string, VerdictDto>();
  for (const verdict of harvesting.verdicts) {
    map.set(verdict.candidate_id, verdict);
  }
  return map;
}

function CoverHeader({ batchId, exportDate }: { batchId: string; exportDate: string }) {
  return (
    <View>
      <Text style={pdfBaseStyles.coverWordmark}>Cortexa</Text>
      <Text style={styles.h1}>Batch Report</Text>
      <Text style={pdfBaseStyles.caption}>{`Batch ID: ${batchId}  •  Exported: ${exportDate}`}</Text>
      <View style={pdfBaseStyles.coverHairline} />
    </View>
  );
}

interface HarvestingRowProps {
  candidate: HarvestingResultDto['candidates'][number];
  verdict: VerdictDto | undefined;
  index: number;
  isLast: boolean;
}

function HarvestingRow({ candidate, verdict, index, isLast }: HarvestingRowProps) {
  const rec = verdict?.recommendation ?? 'investigate';
  const score = verdict?.patentability_score ?? 0;
  const rationale = truncate(verdict?.rationale ?? '', 280);

  return (
    <View wrap={false} style={pdfBaseStyles.rowContainer}>
      <Text style={pdfBaseStyles.h3}>{`#${index + 1}  ${candidate.title}`}</Text>
      <View style={styles.rowMeta}>
        <Text style={pdfBaseStyles.caption}>{`Score: ${score}/100`}</Text>
        <Text
          style={{
            ...pdfBaseStyles.caption,
            color: recommendationColor(rec),
            fontFamily: 'Helvetica-Bold',
          }}
        >
          {humanizeRecommendation(rec)}
        </Text>
      </View>
      {rationale.length > 0 && <Text style={pdfBaseStyles.body}>{rationale}</Text>}
      {!isLast && <View style={{ ...pdfBaseStyles.hairline, marginTop: 10 }} />}
    </View>
  );
}

interface SeedingRowProps {
  opportunity: SeedingResultDto['opportunities'][number];
  isLast: boolean;
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function SeedingRow({ opportunity, isLast }: SeedingRowProps) {
  const description = truncate(opportunity.description, 320);
  const noveltyDelta = truncate(opportunity.novelty_delta ?? '', 280);

  return (
    <View wrap={false} style={pdfBaseStyles.rowContainer}>
      <Text style={pdfBaseStyles.h3}>{opportunity.title}</Text>
      <View style={styles.rowMeta}>
        <Text style={pdfBaseStyles.caption}>{`Confidence: ${clampScore(opportunity.confidence_score)}/100`}</Text>
        {opportunity.category && (
          <Text style={{ ...pdfBaseStyles.caption, fontFamily: 'Helvetica-Bold' }}>{capitalize(opportunity.category)}</Text>
        )}
      </View>
      {description.length > 0 && <Text style={pdfBaseStyles.body}>{description}</Text>}
      {noveltyDelta.length > 0 && (
        <Text style={{ ...pdfBaseStyles.body, marginTop: 4 }}>{`What's new: ${noveltyDelta}`}</Text>
      )}
      {opportunity.roadmap_alignment.length > 0 && (
        <Text style={{ ...pdfBaseStyles.caption, marginTop: 4 }}>{opportunity.roadmap_alignment}</Text>
      )}
      {!isLast && <View style={{ ...pdfBaseStyles.hairline, marginTop: 10 }} />}
    </View>
  );
}

export function BatchReportDocument({
  harvesting,
  seeding,
  batchId,
  exportDate,
}: BatchReportDocumentProps) {
  const verdictMap = buildVerdictMap(harvesting);
  const candidates = harvesting.candidates;
  const opportunities = seeding.opportunities;

  return (
    <Document>
      <Page size="A4" style={pdfBaseStyles.page}>
        <CoverHeader batchId={batchId} exportDate={exportDate} />

        <View>
          <View style={styles.h2Row}>
            <Text style={styles.h2WithGrow}>Harvesting — Ranked Candidates</Text>
            <Text style={styles.h2Count}>
              {`${candidates.length} candidate${candidates.length !== 1 ? 's' : ''}`}
            </Text>
          </View>
          <View style={styles.hairlineAfterH2} />
          {candidates.map((c, i) => (
            <HarvestingRow
              key={c.id}
              candidate={c}
              verdict={verdictMap.get(c.id)}
              index={i}
              isLast={i === candidates.length - 1}
            />
          ))}
        </View>

        <View style={styles.sectionGap}>
          <View style={styles.h2Row}>
            <Text style={styles.h2WithGrow}>Seeding — New Opportunities</Text>
            <Text style={styles.h2Count}>
              {`${opportunities.length} opportunit${opportunities.length !== 1 ? 'ies' : 'y'}`}
            </Text>
          </View>
          <View style={styles.hairlineAfterH2} />
          {opportunities.map((o, i) => (
            <SeedingRow key={o.id} opportunity={o} isLast={i === opportunities.length - 1} />
          ))}
        </View>

        <PdfPageFooter />
      </Page>
    </Document>
  );
}
