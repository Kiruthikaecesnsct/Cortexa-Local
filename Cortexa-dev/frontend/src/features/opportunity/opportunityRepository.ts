import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { OpportunityDetailDto } from '../../core/api/types';
import type { OpportunityDetailError } from './opportunityTypes';
import type { RankedCandidate } from '../results/resultsTypes';

const client = new ApiClient(apiBaseUrl, getAccessToken);

export const CANDIDATE_DETAIL_ENDPOINT = (batchId: string, candidateId: string): string =>
  `/batches/${batchId}/results/${candidateId}`;

type Result = { ok: true; data: RankedCandidate } | { ok: false; error: OpportunityDetailError };

function extractCorrelationId(err: unknown): string | undefined {
  if (err !== null && typeof err === 'object' && 'response' in err) {
    const resp = (err as { response?: { data?: ApiResponse<unknown> } }).response;
    return resp?.data?.correlation_id;
  }
  return undefined;
}

function extractStatus(err: unknown): number | undefined {
  if (err !== null && typeof err === 'object' && 'response' in err) {
    const resp = (err as { response?: { status?: number } }).response;
    return resp?.status;
  }
  return undefined;
}

function mapDetailDtoToCandidate(dto: OpportunityDetailDto): RankedCandidate {
  return {
    id: dto.id,
    title: dto.title,
    abstract: dto.abstract,
    claimDraft: dto.claim_draft,
    patentability_score: dto.patentability_score ?? dto.weighted_score ?? 0,
    recommendation: dto.recommendation ?? 'investigate',
    rank: dto.rank ?? 0,
    maturity: (dto.maturity?.toLowerCase() ?? 'emerging') as 'mature' | 'emerging' | 'speculative',
    rationale: dto.rationale ?? '',
    source_asset_id: dto.source_asset_id,
    batch_id: dto.batch_id,
    noveltyHypothesis: dto.novelty_hypothesis,
    axes: dto.axes,
    agreementFlag: dto.agreement_flag,
    citations: dto.citations,
    provenanceLinks: dto.provenance_links,
    sourceAvailability: dto.source_availability,
    sourceStatus: dto.source_status,
    evidenceSources: dto.evidence_sources,
    description: dto.description,
  };
}

export const DOCUMENT_CONTENT_ENDPOINT = (batchId: string, documentId: string): string =>
  `/documents/${batchId}/${documentId}/content`;

type DocumentContentResult = { ok: true; data: Blob } | { ok: false; error: OpportunityDetailError };

function mapDocumentContentError(err: unknown): OpportunityDetailError {
  const status = extractStatus(err);
  const correlationId = extractCorrelationId(err);
  if (status === 403) {
    return { kind: 'forbidden', message: "You don't have access to this document.", correlationId };
  }
  if (status === 404) {
    return { kind: 'not_found', message: 'This document has no viewable preview.', correlationId };
  }
  return { kind: 'network', message: "Can't load this document right now.", correlationId };
}

export async function fetchDocumentContent(batchId: string, documentId: string, signal?: AbortSignal): Promise<DocumentContentResult> {
  try {
    const data = await client.getBlob(DOCUMENT_CONTENT_ENDPOINT(batchId, documentId), signal);
    return { ok: true, data };
  } catch (err) {
    return { ok: false, error: mapDocumentContentError(err) };
  }
}

export async function fetchCandidateDetail(batchId: string, candidateId: string): Promise<Result> {
  try {
    const response = await client.get<OpportunityDetailDto>(CANDIDATE_DETAIL_ENDPOINT(batchId, candidateId));
    if (!response.success || !response.data) {
      const kind = response.error_code === 'CANDIDATE_NOT_FOUND' ? 'not_found' : 'server';
      return { ok: false, error: { kind, message: response.message ?? 'Could not load this candidate.', correlationId: response.correlation_id, errorCode: response.error_code } };
    }
    return { ok: true, data: mapDetailDtoToCandidate(response.data) };
  } catch (err) {
    const status = extractStatus(err);
    if (status === 404) {
      return { ok: false, error: { kind: 'not_found', message: 'Candidate not found.', correlationId: extractCorrelationId(err) } };
    }
    return { ok: false, error: { kind: 'network', message: "Can't load this candidate right now.", correlationId: extractCorrelationId(err) } };
  }
}
