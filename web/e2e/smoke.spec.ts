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

/**
 * FR-1.3 — cohort transition patterns. The fixture aggregates
 * (src/tests/fixtures/deterioration-aggregates) sit deliberately at 49, 50 and 51 transitions per
 * row, so both sides of the n >= 50 sample-size floor are reachable here.
 */
test.describe('deterioration patterns (FR-1.3)', () => {
  const cohort = 'typeGroup=Girder+%2F+Stringer&materialGroup=Steel&region=Northeast'

  test('the matrix is deep-linkable and shows counts with sample sizes', async ({ page }) => {
    await page.goto(`/patterns?component=Deck&${cohort}`)

    const region = page.getByRole('region', { name: /Girder \/ Stringer · Steel · Northeast, Deck/ })
    await expect(region).toBeVisible({ timeout: 20_000 })

    // Sample sizes are always visible (AC-1): the three fixture rows, by their totals. Addressed by
    // from-rating rather than by text — "insufficient data (n < 50)" contains "50", so a bare
    // toContainText('50') is satisfied by the very rows it is meant to distinguish from.
    const table = region.getByRole('table')
    await expect(table.locator('tr[data-from-rating="5"] td.n').first()).toContainText('51')
    await expect(table.locator('tr[data-from-rating="7"] td.n').first()).toContainText('50')
    await expect(table.locator('tr[data-from-rating="6"] td.n').first()).toContainText('49')

    // An above-floor row publishes rates.
    await expect(table).toContainText('80.0%')
    await expect(table).toContainText('20.0%')
  })

  test('rows under the sample-size floor say "insufficient data" and show no rate (AC-3)', async ({
    page,
  }) => {
    await page.goto(`/patterns?component=Deck&${cohort}`)
    const table = page.getByRole('region', { name: /Deck/ }).getByRole('table')
    await expect(table).toBeVisible({ timeout: 20_000 })

    // Rows are addressed by their from-rating, not by their text: "insufficient data (n < 50)"
    // itself contains "50", so a text filter matches the very rows it is meant to exclude.
    // Fixture: from-rating 6 holds 49 transitions, from-rating 7 holds exactly 50.
    const suppressed = table.locator('tr[data-from-rating="6"]')
    await expect(suppressed).toHaveClass(/insufficient/)
    await expect(suppressed).toContainText('49')
    await expect(suppressed).toContainText('insufficient data')

    // ...and not one of its ten cells carries a percentage.
    await expect(suppressed.locator('td.cell')).toHaveCount(10)
    await expect(suppressed).not.toContainText('%')

    // Counts survive suppression — the floor removes the rate, never the evidence.
    await expect(suppressed).toContainText('45')

    // The row of exactly 50 is on the floor, so it publishes: the boundary is >=, not >.
    const onTheFloor = table.locator('tr[data-from-rating="7"]')
    await expect(onTheFloor).not.toHaveClass(/insufficient/)
    await expect(onTheFloor).toContainText('80.0%')
  })

  test('every matrix carries the methodology link, the cadence caveat and the GR-6 framing (AC-4)', async ({
    page,
  }) => {
    await page.goto(`/patterns?component=Deck&${cohort}`)
    const region = page.getByRole('region', { name: /Deck/ })
    await expect(region).toBeVisible({ timeout: 20_000 })

    await expect(region).toContainText('not engineering advice')
    await expect(region).toContainText('not a prediction')
    await expect(region).toContainText('24-month')
    await expect(
      region.getByRole('link', { name: /Read the full methodology/ }),
    ).toHaveAttribute('href', /METHODOLOGY-DETERIORATION\.md$/)

    // The row's observed span — with all 33 year-pairs pooled this is the only thing standing between
    // a pooled cell and a misleading label (methodology §4/§6.8), so it is asserted in the DOM.
    const populated = region.locator('tr[data-from-rating="5"] td.n').nth(1)
    await expect(populated).toContainText('2020–2022')
    await expect(populated).toContainText('3 vintage pairs')
    // An unpopulated row shows a dash rather than inventing a span.
    await expect(region.locator('tr[data-from-rating="0"] td.n').nth(1)).toContainText('—')

    // Provenance travels with the matrix (NFR-3).
    await expect(region).toContainText('deterioration-fixture-0001')

    // The method note states the range of the run being served, not a hardcoded one.
    await expect(region).toContainText('2020–2023')

    // The site-wide disclaimer footer is still here too (GR-6).
    await expect(page.getByText('not engineering advice').first()).toBeVisible()
  })

  test('changing the cohort or component updates the URL so the view stays shareable', async ({
    page,
  }) => {
    await page.goto('/patterns?component=Deck')
    await expect(page.getByRole('region', { name: /All bridges/ })).toBeVisible({ timeout: 20_000 })

    await page.getByLabel('Component').selectOption('Culvert')
    await expect(page).toHaveURL(/component=Culvert/)
    await expect(page.getByRole('region', { name: /Culvert/ })).toBeVisible({ timeout: 20_000 })

    // Picking a cohort must put all three dimensions in the URL — they are meaningless apart, and
    // the API rejects a partial cohort.
    await page.getByLabel('Component').selectOption('Deck')
    await page.getByLabel('Cohort').selectOption('Girder / Stringer|Steel|Northeast')
    await expect(page).toHaveURL(/typeGroup=Girder/)
    await expect(page).toHaveURL(/materialGroup=Steel/)
    await expect(page).toHaveURL(/region=Northeast/)
    await expect(page.getByRole('region', { name: /Girder \/ Stringer · Steel · Northeast/ })).toBeVisible({
      timeout: 20_000,
    })

    // ...and returning to the national matrix must clear all three rather than leave a partial one.
    await page.getByLabel('Cohort').selectOption('')
    await expect(page).not.toHaveURL(/typeGroup=/)
    await expect(page).not.toHaveURL(/materialGroup=/)
    await expect(page).not.toHaveURL(/region=/)
    await expect(page.getByRole('region', { name: /All bridges/ })).toBeVisible({ timeout: 20_000 })
  })

  test('a matrix with no row above the floor states no share at all (AC-3)', async ({ page }) => {
    // The Culvert fixture is 10 pairs against a floor of 50, so every row is suppressed — and the
    // matrix-level unchanged share has to be suppressed with them. Computed over the whole matrix it
    // would read "100.0%", directly under ten rows saying "insufficient data".
    await page.goto('/patterns?component=Culvert')
    const region = page.getByRole('region', { name: /Culvert/ })
    await expect(region).toBeVisible({ timeout: 20_000 })

    await expect(region).toContainText('insufficient data')
    await expect(region).toContainText('no share is stated')
    await expect(region).not.toContainText('100.0%')
  })

  test('no per-structure deterioration surface exists anywhere in the view', async ({ page }) => {
    await page.goto(`/patterns?component=Deck&${cohort}`)
    await expect(page.getByRole('region', { name: /Deck/ })).toBeVisible({ timeout: 20_000 })

    // Cohort level only (methodology §7): a structure number must not appear on this page, and the
    // copy must not promise anything about the future.
    const body = (await page.locator('body').innerText()).toLowerCase()
    for (const forbidden of ['structure number', 'forecast', 'projected', 'remaining life']) {
      expect(body).not.toContain(forbidden)
    }
    // "prediction" may appear only inside the disclaimer that denies it.
    expect(body.split('prediction').length - 1).toBe(body.split('not a prediction').length - 1)
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

  test('trends view has no serious/critical violations (FR-1.2)', async ({ page }) => {
    await page.goto('/trends?level=state&fips=12')
    await expect(page.getByRole('region', { name: /Condition over time/ })).toBeVisible({
      timeout: 20_000,
    })
    const results = await new AxeBuilder({ page }).analyze()
    expect(severe(results.violations)).toEqual([])
  })

  test('patterns view has no serious/critical violations (FR-1.3)', async ({ page }) => {
    await page.goto(
      '/patterns?component=Deck&typeGroup=Girder+%2F+Stringer&materialGroup=Steel&region=Northeast',
    )
    await expect(page.getByRole('region', { name: /Deck/ })).toBeVisible({ timeout: 20_000 })
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
