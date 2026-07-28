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

test('QA page publishes the county join coverage with its method note (FR-1.5 AC-2)', async ({
  page,
}) => {
  await page.goto('/qa')
  await expect(page.getByRole('heading', { name: 'County join coverage' })).toBeVisible({
    timeout: 20_000,
  })

  // Data-agnostic: the section renders its published empty state when the join has not been
  // loaded, and CI does load it. Either branch is correct; silently rendering neither is not.
  const notPublished = page.getByText('has not been published yet')
  if (await notPublished.isVisible()) {
    test.skip(true, 'county join not published in this environment')
  }

  // A coverage figure is a percentage, and it must never be a bare "100%" while structures are
  // quarantined — the API sends four decimals for exactly that reason.
  await expect(
    page.locator('.kpi', { hasText: 'Structures matched to a county' }).locator('.value'),
  ).toHaveText(/\d+\.\d{4}%/)

  // Every miss carries a reason, addressed structurally rather than by text.
  const reasons = page.getByRole('region', { name: 'Join misses by reason' })
  await expect(reasons.locator('tbody tr')).not.toHaveCount(0)
  await expect(reasons.locator('[data-reason="on_county_boundary"]')).toHaveCount(1)

  // GR-6: the rule the number was measured under is adjacent to the number, and the page says
  // plainly that a disagreement is not a correction. The predicate is asserted in both places it
  // appears — the server-authored method note and the provenance line — because a number whose
  // provenance names a different rule than the caption is the failure worth catching.
  const methodNote = page.getByText('Each published bridge coordinate was tested')
  await expect(methodNote).toContainText('ST_Within')
  await expect(methodNote).toContainText('not a correction')
  await expect(page.getByText(/^Job county-join-/)).toContainText('ST_Within')
})

test('rankings show the rule that produced them, and export it (FR-1.4 AC-1/AC-3)', async ({
  page,
}) => {
  await page.goto('/rankings?groupBy=county&limit=10')

  const result = page.getByRole('region', { name: /counties by the share/ })
  await expect(result).toBeVisible({ timeout: 20_000 })

  // AC-1: the definition is *alongside* the results, in the same region — not a tooltip, not a
  // collapsed panel. All four rules must be in the DOM.
  await expect(result).toContainText('Sorted by')
  await expect(result).toContainText('Includes')
  await expect(result).toContainText('Excludes')
  await expect(result).toContainText('Share of')

  // GR-6: the note denies what the list is not.
  await expect(result).toContainText('not a priority list')
  await expect(result).toContainText('not engineering advice')

  // A share ranking always says how much its minimum set aside, so the list never reads as
  // exhaustive when it is not.
  await expect(result).toContainText(/\d+ (counties|county) (fall|falls) below the 50-structure minimum/)

  // AC-3: the export link is server-generated and carries the same parameters.
  const csv = result.getByRole('link', { name: /Download this ranking as CSV/ })
  await expect(csv).toHaveAttribute('href', /\/api\/rankings\.csv\?view=worst-condition&groupBy=county/)
})

test('the structure-level ranking is Poor condition ordered by traffic (FR-1.4 AC-1)', async ({
  page,
}) => {
  await page.goto('/rankings?view=high-adt-poor&limit=5')

  const result = page.getByRole('region', { name: /structures published in Poor condition/ })
  await expect(result).toBeVisible({ timeout: 20_000 })

  // Data-agnostic: the fixture database may hold no Poor structure with a published traffic count.
  const rows = result.locator('tbody tr')
  const count = await rows.count()
  test.skip(count === 0, 'no Poor structures with a published traffic count in this database')

  await expect(rows.first().locator('[data-rank="1"]').or(rows.first())).toBeVisible()
  // A structure-level list has no share, so the denominator row must be absent.
  await expect(result).not.toContainText('Share of')
  await expect(result).toContainText('publish no traffic count')
})

test('the county report card deep-links and cites its ACS vintage (FR-1.4 AC-2, FR-1.5 AC-3)', async ({
  page,
}) => {
  await page.goto('/county/12086')

  const card = page.getByRole('region', { name: /Report card for/ })
  await expect(card).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('heading', { name: /Miami-Dade County, Florida/ })).toBeVisible()

  // FR-1.5 AC-3: the ACS vintage is cited where the figure appears, with the estimate caveat.
  await expect(card).toContainText('American Community Survey')
  await expect(card).toContainText('2020-2024')
  await expect(card).toContainText('not a count')

  // Shares are of rated structures, and an unrated structure is outside the denominator rather
  // than a zero in it.
  await expect(card).toContainText('excluded from the share')

  // The URL is the state, so a reload reproduces the page.
  await page.reload()
  await expect(page.getByRole('heading', { name: /Miami-Dade County/ })).toBeVisible({
    timeout: 20_000,
  })

  await expect(card.getByRole('link', { name: /Download as CSV/ })).toHaveAttribute(
    'href',
    /\/api\/counties\/12086\.csv/,
  )
})

test('a county code neither publisher knows renders an empty state, not an error (FR-1.4 AC-2)', async ({
  page,
}) => {
  await page.goto('/county/12999')

  await expect(page.getByRole('heading', { name: 'County report card' })).toBeVisible({
    timeout: 20_000,
  })
  await expect(page.getByText('No county with FIPS')).toBeVisible()
  // The empty state offers a way forward rather than a dead end.
  await expect(page.getByRole('link', { name: /Browse counties by condition/ })).toBeVisible()
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

  test('rankings view has no serious/critical violations (FR-1.4)', async ({ page }) => {
    await page.goto('/rankings?groupBy=county&limit=10')
    await expect(page.getByRole('heading', { name: 'Rankings' })).toBeVisible({ timeout: 20_000 })
    const results = await new AxeBuilder({ page }).analyze()
    expect(severe(results.violations)).toEqual([])
  })

  test('county report card has no serious/critical violations (FR-1.4)', async ({ page }) => {
    await page.goto('/county/12086')
    await expect(page.getByRole('region', { name: /Report card for/ })).toBeVisible({
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

  // Every scan above runs at Playwright's 1280px default, and two of the three serious defects the
  // P1-W6 pass found were invisible at that width and only at that width:
  //
  //   - the header is one 52px row needing 897px, and below ~700px it overflowed its own background
  //     rather than wrapping, so three of the five destinations and the search box were laid out in
  //     white on the light page behind it — three contrast failures per route, at 1.07:1;
  //   - `.qa-table-wrap` only scrolls when the viewport is narrower than the table, and axe's
  //     scrollable-region-focusable only fires on a region that is actually scrolling.
  //
  // A suite that only ever looks at a desktop viewport cannot see either. These run the same four
  // Phase 1 routes at 375px.
  test.describe('at a phone viewport (375px)', () => {
    test.use({ viewport: { width: 375, height: 812 } })

    // Each route names the element the scan exists to reach, and the test waits for *that* rather
    // than for any heading. Waiting on a heading is not a readiness gate: every one of these pages
    // renders its <h2> before the fetch resolves and again in its error branch, so a route whose
    // API is down would scan the chrome, find nothing, and report success — which is a green tick
    // for a page that never rendered.
    const MOBILE_ROUTES: Array<[string, string, string]> = [
      // Renders StructureTable, whose scrolling wrapper is one of the four this pass made
      // focusable. Structure rankings have no sample-size floor, so they populate at fixture scale.
      ['high-ADT ranking', '/rankings?view=high-adt-poor&limit=10', 'region:Ranked structures'],
      // Both report-card tables: the condition counts and the year-by-year history.
      ['county report card', '/county/12086', "region:Condition of this county's structures"],
      ['county report card history', '/county/12086', 'region:Condition over time for this county'],
      ['trends', '/trends?level=state&fips=12', 'region:Condition over time for Florida'],
      [
        'patterns',
        '/patterns?component=Deck&typeGroup=Girder+%2F+Stringer&materialGroup=Steel&region=Northeast',
        'group:Transition matrix',
      ],
      ['QA', '/qa', 'region:Join misses by reason'],
      // The empty state, deliberately: a ranking whose every group falls below the n>=50 floor is
      // what the CI fixture produces, and it is a rendered state like any other.
      ['rankings, all groups below the floor', '/rankings?groupBy=county&limit=10', 'text:No group in this snapshot'],
    ]

    for (const [name, path, expected] of MOBILE_ROUTES) {
      test(`${name} has no serious/critical violations at 375px (NFR-7)`, async ({ page }) => {
        await page.goto(path)

        const [kind, value] = [expected.slice(0, expected.indexOf(':')), expected.slice(expected.indexOf(':') + 1)]
        const target =
          kind === 'text'
            ? page.getByText(new RegExp(value))
            : page.getByRole(kind as 'region' | 'group', { name: value })
        await expect(target, `${path} never rendered ${expected}`).toBeVisible({ timeout: 20_000 })

        const results = await new AxeBuilder({ page }).exclude('.maplibregl-map').analyze()
        expect(severe(results.violations)).toEqual([])
      })
    }

    // Known gap, stated rather than papered over: GroupTable — the worst-condition ranking's table
    // — cannot render at fixture scale. RankingEndpoints applies a floor of 50 rated structures per
    // group, and the 114-row fixture's largest county has 15 and its largest state 21, so the route
    // renders "No group in this snapshot meets the inclusion rule" and the table is never mounted.
    // Its focusable wrapper is therefore covered only by the full-scale local run, where reverting
    // the tabIndex does fail the scan. Closing this properly needs a ranking fixture that clears
    // the floor, which is its own change.

    // The contrast rule catches the header defect only because the overflowed items land on a pale
    // background; a future header that overflows onto a dark one would pass axe and still be
    // unreachable. This asserts the geometry instead: nothing in the header may sit outside it.
    // Every width from a phone up to where the row genuinely fits. The first version of this fix
    // pinned the wrap to a `max-width: 700px` media query while the row needs 897 px, which left
    // 701-896 as broken as before and passed this test anyway because it only ran at 375. The
    // widths below straddle that band deliberately: 768 is an iPad in portrait.
    for (const width of [375, 414, 701, 768, 896, 1024]) {
      test(`the header wraps rather than overflowing off-screen at ${width}px (NFR-7)`, async ({
        page,
      }) => {
        await page.setViewportSize({ width, height: 812 })
        await page.goto('/rankings?groupBy=state&limit=10')
        await expect(page.getByRole('heading', { name: 'Rankings' })).toBeVisible({ timeout: 20_000 })

        const overflow = await page.evaluate(() => {
          const header = document.querySelector('.app-header') as HTMLElement
          const right = header.getBoundingClientRect().right
          return [...header.querySelectorAll('a, button, input')]
            .filter((el) => el.getBoundingClientRect().right > right + 1)
            .map((el) => el.textContent?.trim() || el.getAttribute('aria-label') || el.tagName)
        })

        expect(overflow, 'header controls laid out past the header itself').toEqual([])

        // And the page itself must not scroll sideways — the symptom a reader actually sees.
        const horizontallyScrolls = await page.evaluate(
          () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
        )
        expect(horizontallyScrolls, 'the document scrolls horizontally').toBe(false)
      })
    }
  })
})
