import { createElement } from 'react';
import type { ReactElement } from 'react';
import type { ResultsData } from '../results/resultsTypes';
import type { OpportunityDetail } from '../opportunity/opportunityTypes';
import { formatExportDate } from './pdf/pdfHelpers';

export async function buildReportPdfBlob(
  harvesting: ResultsData['harvesting'],
  seeding: ResultsData['seeding'],
  batchId: string,
): Promise<Blob> {
  const { pdf } = await import('@react-pdf/renderer');
  const { BatchReportDocument } = await import('./pdf/BatchReportDocument');

  const exportDate = formatExportDate();
  const element = createElement(BatchReportDocument, { harvesting, seeding, batchId, exportDate });

  return pdf(element as ReactElement<never>).toBlob();
}

export async function buildOpportunityPdfBlob(
  detail: OpportunityDetail,
  batchId: string,
): Promise<Blob> {
  const { pdf } = await import('@react-pdf/renderer');
  const { OpportunityPdfDocument } = await import('./pdf/OpportunityPdfDocument');

  const exportDate = formatExportDate();
  const element = createElement(OpportunityPdfDocument, { detail, batchId, exportDate });

  return pdf(element as ReactElement<never>).toBlob();
}
