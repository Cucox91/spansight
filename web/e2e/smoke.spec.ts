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

/**
 * FR-1.2 — condition trends. The fixture aggregates cover three Miami-Dade structures
 * (src/tests/fixtures/trends), so both the populated and the empty paths are reachable here.
 */
test.describe('condition trends (FR-1.2)', () => {
  test('trends view is deep-linkable and renders shares over time', async ({ page }) => {
    await page.goto('/trends?level=state&fips=12')

    const region = page.getByRole('region', { name: /Condition over time for Florida/ })
    await expect(region).toBeVisible({ timeout: 20_000 })
    // Counts come straight from the rollups; the fixture's last year is 2025.
    await expect(region).toContainText('2025')
    await expect(region).toContainText(/% Poor/)

    // GR-6: the method sits with the chart, not only in the footer.
    await expect(region).toContainText('not engineering advice')
  })

  test('changing the query updates the URL so the view stays shareable', async ({ page }) => {
    await page.goto('/trends?level=state&fips=12')
    await expect(page.getByRole('region', { name: /Florida/ })).toBeVisible({ timeout: 20_000 })

    await page.getByLabel('From').fill('2023')
    await page.getByLabel('From').blur()

    await expect(page).toHaveURL(/fromYear=2023/)
    await expect(page.getByRole('region', { name: /Florida/ })).not.toContainText('2020')
  })

  test('an area with no published rows explains itself instead of erroring', async ({ page }) => {
    // American Samoa (60) is a real FIPS code that appears in no NBI vintage — a legitimate
    // question with no published answer, in the fixture DB and in a full national load alike.
    await page.goto('/trends?level=state&fips=60')
    await expect(page.getByText(/No published rows/)).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('not engineering advice')).toBeVisible()
  })

  test('the drawer shows a condition sparkline with its published values', async ({ page, request }) => {
    // Data-agnostic like the rest of the suite: find a bridge that actually has an aggregate row
    // rather than hard-coding one, so this runs against the CI fixture and a full national load.
    const withHistory = await findBridgeWithHistory(request)
    test.skip(withHistory === null, 'No condition aggregates loaded — run tools/trends/build-trends.sh + load-trends.')

    await page.goto(`/bridge/${withHistory!.state}/${encodeURIComponent(withHistory!.structureNumber)}`)
    const drawer = page.getByRole('region', { name: 'Bridge detail' })
    await expect(drawer).toBeVisible()

    const history = drawer.getByRole('region', { name: 'Condition history' })
    await expect(history).toBeVisible({ timeout: 20_000 })
    await expect(history.getByRole('img')).toHaveAttribute(
      'aria-label',
      /Lowest published NBI condition rating/,
    )

    // The values are in the DOM, not only in the graphic (NFR-7).
    await history.getByRole('group').getByText(/Show the \d+ published rating/).click()
    await expect(history).toContainText(String(withHistory!.firstYear))

    // GR-6 wording sits with the chart.
    await expect(history).toContainText('not engineering advice')
  })

  test('a structure with no published history says so rather than drawing an empty chart', async ({
    page,
    request,
  }) => {
    const missing = await findBridgeWithoutHistory(request)
    test.skip(missing === null, 'Every sampled bridge has history; nothing to assert here.')

    await page.goto(`/bridge/${missing!.state}/${encodeURIComponent(missing!.structureNumber)}`)
    const history = page.getByRole('region', { name: 'Condition history' })
    await expect(history).toContainText(/No published condition history/, { timeout: 20_000 })
  })
})

type BridgeRef = { state: string; structureNumber: string; firstYear: number }

/** Walks a page of bridges asking for each one's history until one has aggregates (or none do). */
async function findBridgeWithHistory(
  request: import('@playwright/test').APIRequestContext,
): Promise<BridgeRef | null> {
  const response = await request.get(`${API_BASE}/api/bridges?pageSize=40`)
  const { items } = (await response.json()) as Array<never> & { items: Array<{ id: string }> }
  for (const item of items) {
    const [state, ...rest] = item.id.split('-')
    const structureNumber = rest.join('-')
    const history = await request.get(
      `${API_BASE}/api/bridges/${state}/${encodeURIComponent(structureNumber)}/history`,
    )
    if (history.ok()) {
      const body = (await history.json()) as { firstYear: number }
      return { state, structureNumber, firstYear: body.firstYear }
    }
  }
  return null
}

/** The mirror case: a bridge the aggregates do not cover. */
async function findBridgeWithoutHistory(
  request: import('@playwright/test').APIRequestContext,
): Promise<BridgeRef | null> {
  const response = await request.get(`${API_BASE}/api/bridges?pageSize=40`)
  const { items } = (await response.json()) as { items: Array<{ id: string }> }
  for (const item of items) {
    const [state, ...rest] = item.id.split('-')
    const structureNumber = rest.join('-')
    const history = await request.get(
      `${API_BASE}/api/bridges/${state}/${encodeURIComponent(structureNumber)}/history`,
    )
    if (history.status() === 404) {
      return { state, structureNumber, firstYear: 0 }
    }
  }
  return null
}

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

  test('trends view has no serious/critical violations (FR-1.2)', async ({ page }) => {
    await page.goto('/trends?level=state&fips=12')
    await expect(page.getByRole('region', { name: /Condition over time/ })).toBeVisible({
      timeout: 20_000,
    })
    const results = await new AxeBuilder({ page }).analyze()
    expect(severe(results.violations)).toEqual([])
  })

  test('the drawer sparkline is reachable and labelled (FR-1.2)', async ({ page, request }) => {
    const withHistory = await findBridgeWithHistory(request)
    test.skip(withHistory === null, 'No condition aggregates loaded — run tools/trends/build-trends.sh + load-trends.')

    await page.goto(`/bridge/${withHistory!.state}/${encodeURIComponent(withHistory!.structureNumber)}`)
    const history = page.getByRole('region', { name: 'Condition history' })
    await expect(history).toBeVisible({ timeout: 20_000 })

    const results = await new AxeBuilder({ page }).exclude('.maplibregl-map').analyze()
    expect(severe(results.violations)).toEqual([])
  })
})
