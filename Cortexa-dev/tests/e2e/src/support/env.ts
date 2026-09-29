import { config } from 'dotenv';
import { resolve } from 'path';

config({ path: resolve(process.cwd(), '.env') });

interface E2EConfig {
  baseUrl: string;
  gatewayBaseUrl: string;
  keyVaultName: string;
  adminEmailSecret: string;
  adminPasswordSecret: string;
  batchId?: string;
  liveTimeoutMs: number;
  pollIntervalMs: number;
}

function required(key: string): string {
  const value = process.env[key];
  if (!value) {
    throw new Error(`Missing required environment variable: ${key}`);
  }
  return value;
}

function optional(key: string): string | undefined {
  return process.env[key] || undefined;
}

function parseMs(key: string, defaultValue: number): number {
  const raw = process.env[key];
  if (!raw) return defaultValue;
  const parsed = parseInt(raw, 10);
  if (isNaN(parsed)) {
    throw new Error(`Invalid number for ${key}: ${raw}`);
  }
  return parsed;
}

export function loadConfig(): E2EConfig {
  return {
    baseUrl: required('CORTEXA_E2E_BASE_URL'),
    gatewayBaseUrl: required('CORTEXA_GATEWAY_BASE_URL'),
    keyVaultName: required('CORTEXA_KEY_VAULT_NAME'),
    adminEmailSecret: required('CORTEXA_ADMIN_EMAIL_SECRET'),
    adminPasswordSecret: required('CORTEXA_ADMIN_PASSWORD_SECRET'),
    batchId: optional('CORTEXA_E2E_BATCH_ID'),
    liveTimeoutMs: parseMs('CORTEXA_E2E_LIVE_TIMEOUT_MS', 900000),
    pollIntervalMs: parseMs('CORTEXA_E2E_POLL_INTERVAL_MS', 5000),
  };
}
