import { test, expect } from '@playwright/test';
import { readFileSync } from 'fs';
import { resolve } from 'path';
import { DashboardPage } from '../src/support/pages';

interface RuntimeState {
  batchId: string;
}

function loadBatchId(): string {
  const statePath = resolve(process.cwd(), 'playwright/.runtime/state.json');
  const raw = readFileSync(statePath, 'utf-8');
  const state: RuntimeState = JSON.parse(raw);
  return state.batchId;
}

test.describe('Results dashboard', () => {
  let batchId: string;

  test.beforeAll(() => {
    batchId = loadBatchId();
  });

  test('AC3: dashboard renders results section for the active engine', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await dashboard.goto(batchId);

    await expect(dashboard.statCards).toBeVisible();
    await expect(dashboard.resultsSection.first()).toBeVisible();
  });
});
