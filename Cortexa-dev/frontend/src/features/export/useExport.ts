import { useCallback, useRef, useState } from 'react';
import type { ResultsData } from '../results/resultsTypes';
import type { OpportunityDetail } from '../opportunity/opportunityTypes';
import type { ExportFormat } from './exportTypes';
import { buildReportJsonBlob, buildOpportunityJsonBlob } from './jsonExport';
import { downloadBlob } from './downloadBlob';
import { buildReportFilename, buildOpportunityFilename } from './exportFilename';

export interface UseExportReturn {
  isExporting: boolean;
  exportReport: (data: ResultsData, batchId: string, format: ExportFormat) => Promise<void>;
  exportOpportunity: (detail: OpportunityDetail, batchId: string, format: ExportFormat) => Promise<void>;
}

export function useExport(): UseExportReturn {
  const [isExporting, setIsExporting] = useState(false);
  const inFlightRef = useRef(false);

  const exportReport = useCallback(async (data: ResultsData, batchId: string, format: ExportFormat) => {
    if (inFlightRef.current) throw new Error('Export already in progress');
    inFlightRef.current = true;
    setIsExporting(true);
    try {
      let blob: Blob;
      if (format === 'json') {
        blob = buildReportJsonBlob(data, batchId);
      } else {
        const { buildReportPdfBlob } = await import('./pdfExport');
        blob = await buildReportPdfBlob(data.harvesting, data.seeding, batchId);
      }
      downloadBlob(blob, buildReportFilename(batchId, format));
    } finally {
      inFlightRef.current = false;
      setIsExporting(false);
    }
  }, []);

  const exportOpportunity = useCallback(async (detail: OpportunityDetail, batchId: string, format: ExportFormat) => {
    if (inFlightRef.current) throw new Error('Export already in progress');
    inFlightRef.current = true;
    setIsExporting(true);
    try {
      let blob: Blob;
      if (format === 'json') {
        blob = buildOpportunityJsonBlob(detail, batchId);
      } else {
        const { buildOpportunityPdfBlob } = await import('./pdfExport');
        blob = await buildOpportunityPdfBlob(detail, batchId);
      }
      downloadBlob(blob, buildOpportunityFilename(detail.candidateId, format));
    } finally {
      inFlightRef.current = false;
      setIsExporting(false);
    }
  }, []);

  return { isExporting, exportReport, exportOpportunity };
}
