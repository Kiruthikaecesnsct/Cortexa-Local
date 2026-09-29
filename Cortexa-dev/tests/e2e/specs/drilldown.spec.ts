import { test, expect } from '@playwright/test';
import { readFileSync } from 'fs';
import { resolve } from 'path';
import { DashboardPage, OpportunityDetailPage } from '../src/support/pages';

interface RuntimeState {
  batchId: string;
}

function loadBatchId(): string {
  const statePath = resolve(process.cwd(), 'playwright/.runtime/state.json');
  const raw = readFileSync(statePath, 'utf-8');
  const state: RuntimeState = JSON.parse(raw);
  return state.batchId;
}

test.describe('Opportunity drilldown', () => {
  let batchId: string;

  test.beforeAll(() => {
    batchId = loadBatchId();
  });

  test('AC4: click into opportunity and assert detail elements', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await dashboard.goto(batchId);

    await dashboard.clickFirstOpportunity();

    await page.waitForURL(/\/batches\/[a-f0-9-]+\/results\/[a-f0-9-]+/, { timeout: 10000 });

    const detailPage = new OpportunityDetailPage(page);

    await expect(detailPage.header).toBeVisible();
    await expect(detailPage.evidenceSection).toBeVisible();

    await expect(page.getByText(/Axis Scores|Evidence Sources/i)).toBeVisible();
  });
});
