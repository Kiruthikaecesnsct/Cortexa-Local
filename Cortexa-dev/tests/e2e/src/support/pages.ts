import { Page, Locator } from '@playwright/test';

export class LoginPage {
  readonly page: Page;
  readonly emailInput: Locator;
  readonly passwordInput: Locator;
  readonly submitButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.emailInput = page.locator('input[type="email"]');
    this.passwordInput = page.locator('input[type="password"]');
    this.submitButton = page.locator('button[type="submit"]');
  }

  async goto() {
    await this.page.goto('/login');
  }

  async login(email: string, password: string) {
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.submitButton.click();
  }
}

export class UploadPage {
  readonly page: Page;
  readonly dropzone: Locator;
  readonly fileInput: Locator;
  readonly submitButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.dropzone = page.getByTestId('upload-dropzone');
    this.fileInput = page.getByTestId('upload-file-input');
    this.submitButton = page.getByTestId('upload-submit-button');
  }

  async goto() {
    await this.page.goto('/batches/new');
  }

  async uploadFile(filePath: string) {
    await this.fileInput.setInputFiles(filePath);
  }

  async submit() {
    await this.submitButton.click();
  }
}

export class ProgressPage {
  readonly page: Page;

  constructor(page: Page) {
    this.page = page;
  }

  async goto(batchId: string) {
    await this.page.goto(`/batches/${batchId}`);
  }

  async waitForQueued() {
    await this.page.waitForSelector('text=/Queued|Processing|Ingesting/i', { timeout: 10000 });
  }
}

export class DashboardPage {
  readonly page: Page;
  readonly statCards: Locator;
  readonly resultsSection: Locator;
  readonly exportPdfButton: Locator;
  readonly exportJsonButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.statCards = page.getByTestId('results-stat-cards');
    this.resultsSection = page.getByTestId(/^(harvesting|seeding)-results$/);
    this.exportPdfButton = page.getByTestId('export-pdf-button');
    this.exportJsonButton = page.getByTestId('export-json-button');
  }

  async goto(batchId: string) {
    await this.page.goto(`/batches/${batchId}/results`);
  }

  async clickFirstOpportunity() {
    await this.page.locator('[style*="cursor: pointer"]').first().click();
  }
}

export class OpportunityDetailPage {
  readonly page: Page;
  readonly header: Locator;
  readonly evidenceSection: Locator;
  readonly exportPdfButton: Locator;
  readonly exportJsonButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.header = page.getByTestId('opportunity-detail-header');
    this.evidenceSection = page.getByTestId('evidence-sources-section');
    this.exportPdfButton = page.getByTestId('detail-export-pdf-button');
    this.exportJsonButton = page.getByTestId('detail-export-json-button');
  }

  async goto(batchId: string, candidateId: string) {
    await this.page.goto(`/batches/${batchId}/results/${candidateId}`);
  }
}
