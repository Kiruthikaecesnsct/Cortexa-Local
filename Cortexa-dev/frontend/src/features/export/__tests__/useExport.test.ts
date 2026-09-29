import { renderHook, act } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { useExport } from '../useExport';

vi.mock('../jsonExport', () => ({
  buildReportJsonBlob: vi.fn(() => new Blob()),
  buildOpportunityJsonBlob: vi.fn(() => new Blob()),
}));

vi.mock('../pdfExport', () => ({
  buildReportPdfBlob: vi.fn(() => Promise.resolve(new Blob())),
  buildOpportunityPdfBlob: vi.fn(() => Promise.resolve(new Blob())),
}));

vi.mock('../downloadBlob', () => ({
  downloadBlob: vi.fn(),
}));

vi.mock('../exportFilename', () => ({
  buildReportFilename: vi.fn(() => 'report.pdf'),
  buildOpportunityFilename: vi.fn(() => 'opp.json'),
}));

import { buildReportJsonBlob, buildOpportunityJsonBlob } from '../jsonExport';
import { buildReportPdfBlob, buildOpportunityPdfBlob } from '../pdfExport';
import { downloadBlob } from '../downloadBlob';

import type { ResultsData } from '../../results/resultsTypes';
import type { OpportunityDetail } from '../../opportunity/opportunityTypes';

const BATCH_ID = 'batch-abc-123';

const MOCK_RESULTS_DATA: ResultsData = {
  harvesting: {} as never,
  seeding: {} as never,
};

const MOCK_OPPORTUNITY_DETAIL: OpportunityDetail = {
  candidateId: 'cand-001',
  title: 'Test Opportunity',
} as never;

beforeEach(() => {
  vi.clearAllMocks();
});

describe('useExport', () => {
  it('isExporting_initial_isFalse', () => {
    const { result } = renderHook(() => useExport());

    expect(result.current.isExporting).toBe(false);
  });

  it('exportReport_jsonFormat_callsBuildReportJsonBlobAndDownloadBlob', async () => {
    const { result } = renderHook(() => useExport());

    await act(async () => {
      await result.current.exportReport(MOCK_RESULTS_DATA, BATCH_ID, 'json');
    });

    expect(buildReportJsonBlob).toHaveBeenCalledOnce();
    expect(buildReportJsonBlob).toHaveBeenCalledWith(MOCK_RESULTS_DATA, BATCH_ID);
    expect(buildReportPdfBlob).not.toHaveBeenCalled();
    expect(downloadBlob).toHaveBeenCalledOnce();
  });

  it('exportReport_pdfFormat_callsBuildReportPdfBlobAndDownloadBlob', async () => {
    const { result } = renderHook(() => useExport());

    await act(async () => {
      await result.current.exportReport(MOCK_RESULTS_DATA, BATCH_ID, 'pdf');
    });

    expect(buildReportPdfBlob).toHaveBeenCalledOnce();
    expect(buildReportPdfBlob).toHaveBeenCalledWith(
      MOCK_RESULTS_DATA.harvesting,
      MOCK_RESULTS_DATA.seeding,
      BATCH_ID,
    );
    expect(buildReportJsonBlob).not.toHaveBeenCalled();
    expect(downloadBlob).toHaveBeenCalledOnce();
  });

  it('exportOpportunity_jsonFormat_callsBuildOpportunityJsonBlobAndDownloadBlob', async () => {
    const { result } = renderHook(() => useExport());

    await act(async () => {
      await result.current.exportOpportunity(MOCK_OPPORTUNITY_DETAIL, BATCH_ID, 'json');
    });

    expect(buildOpportunityJsonBlob).toHaveBeenCalledOnce();
    expect(buildOpportunityJsonBlob).toHaveBeenCalledWith(MOCK_OPPORTUNITY_DETAIL, BATCH_ID);
    expect(buildOpportunityPdfBlob).not.toHaveBeenCalled();
    expect(downloadBlob).toHaveBeenCalledOnce();
  });

  it('exportOpportunity_pdfFormat_callsBuildOpportunityPdfBlobAndDownloadBlob', async () => {
    const { result } = renderHook(() => useExport());

    await act(async () => {
      await result.current.exportOpportunity(MOCK_OPPORTUNITY_DETAIL, BATCH_ID, 'pdf');
    });

    expect(buildOpportunityPdfBlob).toHaveBeenCalledOnce();
    expect(buildOpportunityPdfBlob).toHaveBeenCalledWith(MOCK_OPPORTUNITY_DETAIL, BATCH_ID);
    expect(buildOpportunityJsonBlob).not.toHaveBeenCalled();
    expect(downloadBlob).toHaveBeenCalledOnce();
  });

  it('exportReport_secondCallWhileFirstInFlight_doesNotStartSecondExport', async () => {
    let resolvePdf!: () => void;
    const deferredPdf = new Promise<Blob>((resolve) => {
      resolvePdf = () => resolve(new Blob());
    });
    vi.mocked(buildReportPdfBlob).mockReturnValueOnce(deferredPdf);

    const { result } = renderHook(() => useExport());

    let firstExportSettled = false;

    act(() => {
      result.current.exportReport(MOCK_RESULTS_DATA, BATCH_ID, 'pdf').then(() => {
        firstExportSettled = true;
      });
    });

    await act(async () => {
      await expect(
        result.current.exportReport(MOCK_RESULTS_DATA, BATCH_ID, 'pdf'),
      ).rejects.toThrow('Export already in progress');
    });

    expect(buildReportPdfBlob).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolvePdf();
      await deferredPdf;
    });

    expect(firstExportSettled).toBe(true);
  });

  it('exportReport_whileAsyncInFlight_isExportingIsTrueThenFalse', async () => {
    let resolvePdf!: () => void;
    const deferredPdf = new Promise<Blob>((resolve) => {
      resolvePdf = () => resolve(new Blob());
    });
    vi.mocked(buildReportPdfBlob).mockReturnValueOnce(deferredPdf);

    const { result } = renderHook(() => useExport());

    act(() => {
      result.current.exportReport(MOCK_RESULTS_DATA, BATCH_ID, 'pdf');
    });

    expect(result.current.isExporting).toBe(true);

    await act(async () => {
      resolvePdf();
      await deferredPdf;
    });

    expect(result.current.isExporting).toBe(false);
  });

  it('exportReport_pdfBlobThrows_isExportingReturnsFalseAndErrorRethrown', async () => {
    const exportError = new Error('PDF generation failed');
    vi.mocked(buildReportPdfBlob).mockRejectedValueOnce(exportError);

    const { result } = renderHook(() => useExport());

    await expect(
      act(async () => {
        await result.current.exportReport(MOCK_RESULTS_DATA, BATCH_ID, 'pdf');
      }),
    ).rejects.toThrow('PDF generation failed');

    expect(result.current.isExporting).toBe(false);
  });
});
