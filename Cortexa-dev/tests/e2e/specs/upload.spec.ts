import { test, expect } from '@playwright/test';
import { resolve } from 'path';
import { UploadPage, ProgressPage } from '../src/support/pages';

test.describe('Upload flow', () => {
  test('AC2: upload sample PDF and start batch', async ({ page }) => {
    const uploadPage = new UploadPage(page);
    await uploadPage.goto();

    const fixturePath = resolve(__dirname, '../fixtures/sample_paper.pdf');
    await uploadPage.uploadFile(fixturePath);

    await expect(page.getByText(/sample_paper\.pdf/i)).toBeVisible();

    await uploadPage.submit();

    await page.waitForURL(/\/batches\/[a-f0-9-]+$/, { timeout: 30000 });

    const currentUrl = page.url();
    const batchIdMatch = currentUrl.match(/\/batches\/([a-f0-9-]+)/);
    expect(batchIdMatch).not.toBeNull();

    const progressPage = new ProgressPage(page);
    await progressPage.waitForQueued();
  });
});
