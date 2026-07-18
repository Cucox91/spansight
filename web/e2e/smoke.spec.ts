import { test, expect } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

/**
 * Smoke path per FR-0.4 AC-6: load map → filter → open bridge (deep link) → QA page.
 * Data-agnostic: works against the fixture DB (CI) and a full national load (dev Mac).
 */

test('explorer: KPIs load, filter recomputes, disclaimer always visible', async ({ page }) => {
  await page.goto('/')

  // Disclaimer footer (GR-6) + basemap attribution (NFR-8)
  await expect(page.getByText('not engineering advice')).toBeVisible()
  await expect(page.getByRole('link', { name: 'OpenFreeMap' })).toBeVisible()

  // KPIs populate from /api/stats/summary
  const bridgesShown = page.locator('.kpi', { hasText: 'Bridges shown' }).locator('.value')
  await expect(bridgesShown).toHaveText(/\d/, { timeout: 20_000 })
  const before = await bridgesShown.textContent()

  // Unchecking a condition drives the shared predicate — KPIs recompute, no Apply button (AC-2)
  await page.getByRole('checkbox', { name: /Fair/ }).uncheck()
  await expect(bridgesShown).not.toHaveText(before ?? '', { timeout: 20_000 })
})

// Local runs hit /api through the dev-server proxy; post-deploy runs pass the Container App
// origin via PLAYWRIGHT_API_URL because the deployed SPA calls the API cross-origin.
const API_BASE = process.env.PLAYWRIGHT_API_URL ?? ''

test('drawer deep link opens decoded record, Esc closes (AC-3)', async ({ page }) => {
  // Discover a real bridge id from the API so the test is dataset-independent
  const response = await page.request.get(`${API_BASE}/api/bridges?pageSize=1`)
  expect(response.ok()).toBeTruthy()
  const { items } = (await response.json()) as { items: Array<{ id: string; state: string }> }
  expect(items.length).toBeGreaterThan(0)
  const [state, ...rest] = items[0].id.split('-')
  const structureNumber = rest.join('-')

  await page.goto(`/bridge/${state}/${encodeURIComponent(structureNumber)}`)
  const drawer = page.getByRole('region', { name: 'Bridge detail' })
  await expect(drawer).toBeVisible()
  await expect(drawer).toContainText(/Structure type|Year built/, { timeout: 20_000 })

  await page.keyboard.press('Escape')
  await expect(drawer).not.toBeVisible()
  await expect(page).toHaveURL('/')
})

test('QA page renders the reconciled run summary (FR-0.2 AC-3)', async ({ page }) => {
  await page.goto('/qa')
  await expect(page.getByRole('heading', { name: /Data quality/ })).toBeVisible({
    timeout: 20_000,
  })
  await expect(page.locator('.kpi', { hasText: 'Rows read' }).locator('.value')).toHaveText(/\d/)
  await expect(page.getByText('not engineering advice')).toBeVisible()
})

test('ask-the-map degrades to the Phase 0.5 notice while Ai:Enabled is false (FR-AI.1)', async ({
  page,
}) => {
  await page.goto('/')
  const box = page.getByRole('textbox', { name: 'Ask the map in plain English' })
  await box.fill('poor truss bridges in Florida')
  await page.getByRole('button', { name: 'Ask' }).click()
  // Dark by default (ADR-008): the API answers 503 and the box explains itself; hand-set
  // filters keep working. When the 0.5 gate flips the flag, this asserts the enabled path.
  await expect(page.locator('.ask-notice')).toContainText(/Phase 0.5|Showing:/, {
    timeout: 15_000,
  })
})

test.describe('accessibility (NFR-7 — UI chrome, map canvas exempt)', () => {
  const severe = (violations: Array<{ impact?: string | null }>) =>
    violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')

  test('explorer chrome has no serious/critical violations', async ({ page }) => {
    await page.goto('/')
    await expect(page.locator('.kpi .value').first()).toHaveText(/\d/, { timeout: 20_000 })
    const results = await new AxeBuilder({ page }).exclude('.maplibregl-map').analyze()
    expect(severe(results.violations)).toEqual([])
  })

  test('QA page has no serious/critical violations', async ({ page }) => {
    await page.goto('/qa')
    await expect(page.getByRole('heading', { name: /Data quality/ })).toBeVisible({
      timeout: 20_000,
    })
    const results = await new AxeBuilder({ page }).analyze()
    expect(severe(results.violations)).toEqual([])
  })
})
