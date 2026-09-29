const raw = import.meta.env['VITE_API_BASE_URL'];

if (!raw || typeof raw !== 'string' || raw.trim() === '') {
  throw new Error(
    '[Cortexa] VITE_API_BASE_URL is not set. ' +
      'Copy .env.example to .env.development and provide a value.'
  );
}

export const apiBaseUrl: string = raw.trim();

export const entraTenantId: string = (import.meta.env['VITE_ENTRA_TENANT_ID'] as string | undefined) ?? '';
export const entraClientId: string = (import.meta.env['VITE_ENTRA_CLIENT_ID'] as string | undefined) ?? '';
export const entraRedirectUri: string = (import.meta.env['VITE_ENTRA_REDIRECT_URI'] as string | undefined) ?? '';
export const entraApiScope: string = (import.meta.env['VITE_ENTRA_API_SCOPE'] as string | undefined) ?? '';
export const sentryDsn: string = (import.meta.env['VITE_SENTRY_DSN'] as string | undefined) ?? '';

function parsePositiveInt(raw: string | undefined, fallback: number): number {
  if (!raw) {
    return fallback;
  }
  const parsed = Number.parseInt(raw, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

export const maxBatchFiles: number = parsePositiveInt(
  import.meta.env['VITE_MAX_BATCH_FILES'] as string | undefined,
  25
);

export const maxBatchSizeBytes: number = parsePositiveInt(
  import.meta.env['VITE_MAX_BATCH_SIZE_BYTES'] as string | undefined,
  200 * 1024 * 1024
);

export const maxFileSizeBytes: number = parsePositiveInt(
  import.meta.env['VITE_MAX_FILE_SIZE_BYTES'] as string | undefined,
  50 * 1024 * 1024
);

export const jobPollIntervalMs: number = parsePositiveInt(
  import.meta.env['VITE_JOB_POLL_INTERVAL_MS'] as string | undefined,
  2000
);

export const batchListPollIntervalMs: number = parsePositiveInt(
  import.meta.env['VITE_BATCH_LIST_POLL_INTERVAL_MS'] as string | undefined,
  8000
);

export const batchRetentionDays: number = parsePositiveInt(
  import.meta.env['VITE_CLEANUP_RETENTION_DAYS'] as string | undefined,
  7
);

export const batchDeleteTimeoutMs: number = parsePositiveInt(
  import.meta.env['VITE_BATCH_DELETE_TIMEOUT_MS'] as string | undefined,
  120000
);
