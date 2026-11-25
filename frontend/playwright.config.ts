import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  outputDir: '../.local/frontend-e2e-results',
  fullyParallel: false,
  workers: 1,
  timeout: 90000,
  expect: { timeout: 15000 },
  reporter: 'list',
  use: {
    baseURL: process.env.LUDUS_E2E_URL ?? 'http://127.0.0.1:8080',
    channel: process.env.LUDUS_E2E_BROWSER ?? 'msedge',
    viewport: { width: 1440, height: 1000 },
    headless: true,
    actionTimeout: 15000,
    // Traces and failure screenshots could contain credential fields.
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
});
