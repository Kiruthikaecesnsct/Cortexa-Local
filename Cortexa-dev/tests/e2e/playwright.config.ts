import { defineConfig, devices } from '@playwright/test';
import { loadConfig } from './src/support/env';

const env = loadConfig();

export default defineConfig({
  testDir: './specs',
  timeout: 60000,
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: [['html'], ['list']],
  globalSetup: './global-setup.ts',

  use: {
    baseURL: env.baseUrl,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    storageState: 'playwright/.auth/user.json',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  grep: process.env.CI ? /@live/ : undefined,
  grepInvert: process.env.CI ? undefined : /@live/,
});
