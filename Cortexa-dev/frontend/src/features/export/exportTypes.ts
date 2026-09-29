import type { HarvestingResultDto, SeedingResultDto } from '../../core/api/types';
import type { OpportunityDetail } from '../opportunity/opportunityTypes';

export type ExportFormat = 'pdf' | 'json';

export interface ExportEnvelope {
  schemaVersion: '1.0';
  exportedAt: string;
  batchId: string;
}

export interface ReportExportPayload extends ExportEnvelope {
  harvesting: HarvestingResultDto;
  seeding: SeedingResultDto;
}

export interface OpportunityExportPayload extends ExportEnvelope {
  opportunity: OpportunityDetail;
}
