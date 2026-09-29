import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import { getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';
import type { AdminUserError } from './adminUserTypes';

const client = new ApiClient(apiBaseUrl, getAccessToken);

export interface AuditEntryDto {
  id: string;
  userId: string;
  eventType: string;
  resourceType: string;
  resourceId: string;
  action: string;
  details: string;
  createdDate: string;
}

export interface AuditPageDto {
  items: AuditEntryDto[];
  page: number;
  size: number;
  total: number;
}

export interface AuditQuery {
  userId?: string;
  eventType?: string;
  from?: string;
  to?: string;
  page?: number;
  size?: number;
}

type AuditResult = { ok: true; data: AuditPageDto } | { ok: false; error: AdminUserError };

function buildQuery(q: AuditQuery): string {
  const params = new URLSearchParams();
  if (q.userId) params.set('user_id', q.userId);
  if (q.eventType) params.set('event_type', q.eventType);
  if (q.from) params.set('from', q.from);
  if (q.to) params.set('to', q.to);
  params.set('page', String(q.page ?? 1));
  params.set('size', String(q.size ?? 20));
  return params.toString();
}

function extractCorrelationId(err: unknown): string | undefined {
  if (err !== null && typeof err === 'object' && 'response' in err) {
    return (err as { response?: { data?: ApiResponse<unknown> } }).response?.data?.correlation_id;
  }
  return undefined;
}

function extractStatus(err: unknown): number | undefined {
  if (err !== null && typeof err === 'object' && 'response' in err) {
    return (err as { response?: { status?: number } }).response?.status;
  }
  return undefined;
}

export async function fetchAudit(query: AuditQuery): Promise<AuditResult> {
  try {
    const response = await client.get<AuditPageDto>(`/admin/audit?${buildQuery(query)}`);
    if (!response.success || !response.data) {
      return { ok: false, error: { kind: 'server', message: response.message ?? 'Could not load the audit log.', correlationId: response.correlation_id } };
    }
    return { ok: true, data: response.data };
  } catch (err) {
    const kind = extractStatus(err) === 403 ? 'forbidden' : 'network';
    return { ok: false, error: { kind, message: "Can't load the audit log right now.", correlationId: extractCorrelationId(err) } };
  }
}
