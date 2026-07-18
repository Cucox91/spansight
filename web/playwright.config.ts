import { defineConfig, devices } from '@playwright/test'

// E2E smoke (FR-0.4 AC-6) + a11y scan (NFR-7). Runs against the dev server, which proxies
// /api to the local API (vite.config.ts) — CI brings up postgres + fixture + API first.
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
})
