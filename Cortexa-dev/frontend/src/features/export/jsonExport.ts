import type { ResultsData } from '../results/resultsTypes';
import type { OpportunityDetail } from '../opportunity/opportunityTypes';
import type { ReportExportPayload, OpportunityExportPayload } from './exportTypes';

function envelope(batchId: string): { schemaVersion: '1.0'; exportedAt: string; batchId: string } {
  return {
    schemaVersion: '1.0',
    exportedAt: new Date().toISOString(),
    batchId,
  };
}

export function buildReportJsonBlob(data: ResultsData, batchId: string): Blob {
  const payload: ReportExportPayload = {
    ...envelope(batchId),
    harvesting: data.harvesting,
    seeding: data.seeding,
  };
  return new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
}

export function buildOpportunityJsonBlob(detail: OpportunityDetail, batchId: string): Blob {
  const payload: OpportunityExportPayload = {
    ...envelope(batchId),
    opportunity: detail,
  };
  return new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
}
