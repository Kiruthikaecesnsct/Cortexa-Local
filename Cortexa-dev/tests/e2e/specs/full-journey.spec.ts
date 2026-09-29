import { test, expect } from '@playwright/test';
import { resolve } from 'path';
import { loadConfig } from '../src/support/env';
import { ApiClient } from '../src/support/apiClient';
import { pollUntilTerminal } from '../src/support/batchResolver';
import { getAdminCredentials } from '../src/support/secrets';
import { UploadPage, DashboardPage } from '../src/support/pages';

test.describe('Full journey', () => {
  test('@live upload, wait for terminal state, view dashboard', async ({ page }) => {
    const env = loadConfig();
    const uploadPage = new UploadPage(page);
    await uploadPage.goto();

    const fixturePath = resolve(__dirname, '../fixtures/sample_paper.pdf');
    await uploadPage.uploadFile(fixturePath);

    await uploadPage.submit();

    await page.waitForURL(/\/batches\/[a-f0-9-]+$/, { timeout: 30000 });

    const currentUrl = page.url();
    const batchIdMatch = currentUrl.match(/\/batches\/([a-f0-9-]+)/);
    expect(batchIdMatch).not.toBeNull();
    if (!batchIdMatch) throw new Error('Failed to extract batch ID from URL');
    const batchId = batchIdMatch[1];

    const credentials = await getAdminCredentials(
      env.keyVaultName,
      env.adminEmailSecret,
      env.adminPasswordSecret
    );

    const apiClient = new ApiClient(env.gatewayBaseUrl);
    await apiClient.init();
    await apiClient.login(credentials.email, credentials.password);

    const terminalState = await pollUntilTerminal(
      apiClient,
      batchId,
      env.liveTimeoutMs,
      env.pollIntervalMs
    );

    expect(terminalState).toBe('Completed');

    await apiClient.dispose();

    const dashboard = new DashboardPage(page);
    await dashboard.goto(batchId);

    await expect(dashboard.statCards).toBeVisible();
  });
});
