import { chromium } from '@playwright/test';
import { writeFileSync } from 'fs';
import { resolve } from 'path';
import { loadConfig } from './src/support/env';
import { getAdminCredentials } from './src/support/secrets';
import { ApiClient } from './src/support/apiClient';
import { resolveCompletedBatch } from './src/support/batchResolver';

interface RuntimeState {
  batchId: string;
}

async function globalSetup(): Promise<void> {
  const env = loadConfig();

  const credentials = await getAdminCredentials(
    env.keyVaultName,
    env.adminEmailSecret,
    env.adminPasswordSecret
  );

  const browser = await chromium.launch();
  const context = await browser.newContext({
    baseURL: env.baseUrl,
  });

  const page = await context.newPage();
  await page.goto('/login');
  await page.fill('input[type="email"]', credentials.email);
  await page.fill('input[type="password"]', credentials.password);
  await page.click('button[type="submit"]');
  await page.waitForURL('/dashboard', { timeout: 30000 });

  await context.storageState({ path: resolve(process.cwd(), 'playwright/.auth/user.json') });

  const apiClient = new ApiClient(env.gatewayBaseUrl);
  await apiClient.init();
  await apiClient.login(credentials.email, credentials.password);

  const batchId = await resolveCompletedBatch(apiClient, env.batchId);

  const runtime: RuntimeState = { batchId };
  writeFileSync(resolve(process.cwd(), 'playwright/.runtime/state.json'), JSON.stringify(runtime));

  await apiClient.dispose();
  await context.close();
  await browser.close();

  console.log(`Global setup complete: authenticated, resolved batch ${batchId}`);
}

export default globalSetup;
