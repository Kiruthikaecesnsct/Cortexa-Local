import { ApiClient } from './apiClient';

const TERMINAL_STATES = new Set(['Completed', 'Failed', 'PartiallyFailed']);
const MAX_POLL_DELAY_MS = 60000;

export async function resolveCompletedBatch(
  client: ApiClient,
  envBatchId: string | undefined
): Promise<string> {
  if (envBatchId) {
    console.log(`Using batch from CORTEXA_E2E_BATCH_ID: ${envBatchId}`);
    return envBatchId;
  }

  const batches = await client.listBatches();
  const completed = batches.filter((b) => b.status === 'Completed');

  if (completed.length > 0) {
    completed.sort((a, b) => {
      const aTime = a.updated_at || a.created_at || '';
      const bTime = b.updated_at || b.created_at || '';
      return bTime.localeCompare(aTime);
    });
    const chosen = completed[0].batch_id;
    console.log(`Using newest completed batch: ${chosen}`);
    return chosen;
  }

  throw new Error('No completed batch found. Set CORTEXA_E2E_BATCH_ID or seed a batch first.');
}

export async function pollUntilTerminal(
  client: ApiClient,
  batchId: string,
  timeoutMs: number,
  intervalMs: number
): Promise<string> {
  const startTime = Date.now();
  let delayMs = intervalMs;
  let iteration = 0;

  while (Date.now() - startTime < timeoutMs) {
    const status = await client.getStatus(batchId);

    if (TERMINAL_STATES.has(status.status)) {
      if (status.status === 'Failed') {
        throw new Error(`Batch ${batchId} failed.`);
      }
      console.log(`Batch ${batchId} reached terminal state: ${status.status}`);
      return status.status;
    }

    console.log(`Batch ${batchId} status: ${status.status} (iteration ${++iteration})`);
    await new Promise((resolve) => setTimeout(resolve, delayMs));

    delayMs = Math.min(Math.floor(delayMs * 1.5), MAX_POLL_DELAY_MS);
  }

  throw new Error(`Batch ${batchId} did not complete within ${timeoutMs}ms`);
}
