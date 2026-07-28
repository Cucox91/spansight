import { defineConfig, devices } from '@playwright/test'

// E2E smoke (FR-0.4 AC-6) + a11y scan (NFR-7). Default: runs against the local dev server,
// which proxies /api to the local API (vite.config.ts) — CI brings up postgres + fixture +
// API first. Post-deploy: set PLAYWRIGHT_BASE_URL to the public SPA URL and the same suite
// runs against the live demo with no local server (FR-0.6 AC-3 smoke).
const deployedBaseUrl = process.env.PLAYWRIGHT_BASE_URL

// The tiles-mode variant (FR-0.5). Production serves bridges from a PMTiles archive; everything
// above serves them from the API GeoJSON fallback, and three bugs have reached the live demo
// through exactly that gap (PRs #15, #16, #18). Setting SPANSIGHT_TILES_DIR to a directory
// holding a bridges.pmtiles registers a server for it and a `tiles` project that runs
// tiles.spec.ts against it.
//
// Registration is conditional on purpose: `--project=tiles` without the variable fails with
// "project not found" rather than quietly running zero tests, so a misconfigured CI job is loud.
const tilesDir = process.env.SPANSIGHT_TILES_DIR

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: deployedBaseUrl ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [
    // tiles.spec.ts asserts things that are only true in tiles mode (no GeoJSON request at all,
    // the minified tile schema, the skeleton never appearing), so it must not run in the default
    // fallback project.
    { name: 'chromium', use: { ...devices['Desktop Chrome'] }, testIgnore: /tiles\.spec\.ts/ },
    ...(tilesDir
      ? [{ name: 'tiles', testMatch: /tiles\.spec\.ts/, use: { ...devices['Desktop Chrome'] } }]
      : []),
  ],
  webServer: deployedBaseUrl
    ? undefined
    : [
        {
          command: 'npm run dev',
          url: 'http://localhost:5173',
          reuseExistingServer: !process.env.CI,
          timeout: 60_000,
        },
        // A different origin from the SPA, deliberately: production reads the archive
        // cross-origin from Blob storage, and `Range` is not a CORS-safelisted request header,
        // so the browser preflights every read. Serving it same-origin here would skip the
        // preflight the demo depends on.
        ...(tilesDir
          ? [
              {
                command: `node e2e/tiles-server.mjs ${JSON.stringify(tilesDir)} 8081`,
                url: 'http://127.0.0.1:8081/bridges.pmtiles',
                reuseExistingServer: !process.env.CI,
                timeout: 30_000,
              },
            ]
          : []),
      ],
})
