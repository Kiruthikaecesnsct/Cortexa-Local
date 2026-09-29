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

test.describe('Export functionality', () => {
  let batchId: string;

  test.beforeAll(() => {
    batchId = loadBatchId();
  });

  test('AC5a: export PDF from dashboard', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await dashboard.goto(batchId);

    const downloadPromise = page.waitForEvent('download');
    await dashboard.exportPdfButton.click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toMatch(/\.pdf$/i);

    const path = await download.path();
    expect(path).not.toBeNull();
    if (!path) throw new Error('Download path is null');

    const content = readFileSync(path, 'utf-8');
    expect(content.startsWith('%PDF-')).toBe(true);
    expect(content.length).toBeGreaterThan(0);
  });

  test('AC5b: export JSON from dashboard', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await dashboard.goto(batchId);

    const downloadPromise = page.waitForEvent('download');
    await dashboard.exportJsonButton.click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toMatch(/\.json$/i);

    const path = await download.path();
    expect(path).not.toBeNull();
    if (!path) throw new Error('Download path is null');

    const raw = readFileSync(path, 'utf-8');
    const json = JSON.parse(raw);

    expect(json.schemaVersion).toBe('1.0');
    expect(json.batchId).toBe(batchId);
    expect(json.harvesting || json.seeding).toBeTruthy();
  });

  test('AC5c: export PDF from opportunity detail', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await dashboard.goto(batchId);

    await dashboard.clickFirstOpportunity();
    await page.waitForURL(/\/batches\/[a-f0-9-]+\/results\/[a-f0-9-]+/, { timeout: 10000 });

    const detailPage = new OpportunityDetailPage(page);

    const downloadPromise = page.waitForEvent('download');
    await detailPage.exportPdfButton.click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toMatch(/\.pdf$/i);

    const path = await download.path();
    expect(path).not.toBeNull();
    if (!path) throw new Error('Download path is null');

    const content = readFileSync(path, 'utf-8');
    expect(content.startsWith('%PDF-')).toBe(true);
  });

  test('AC5d: export JSON from opportunity detail', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await dashboard.goto(batchId);

    await dashboard.clickFirstOpportunity();
    await page.waitForURL(/\/batches\/[a-f0-9-]+\/results\/[a-f0-9-]+/, { timeout: 10000 });

    const detailPage = new OpportunityDetailPage(page);

    const downloadPromise = page.waitForEvent('download');
    await detailPage.exportJsonButton.click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toMatch(/\.json$/i);

    const path = await download.path();
    expect(path).not.toBeNull();
    if (!path) throw new Error('Download path is null');

    const raw = readFileSync(path, 'utf-8');
    const json = JSON.parse(raw);

    expect(json.schemaVersion).toBe('1.0');
    expect(json.batchId).toBe(batchId);
    expect(json.opportunity || json.report).toBeTruthy();
  });
});
