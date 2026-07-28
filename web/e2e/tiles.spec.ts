import { createExpression, latest } from '@maplibre/maplibre-gl-style-spec'
import { expect, test, type Page, type Request } from '@playwright/test'

/**
 * FR-0.5 — the map in tiles mode, which is how production runs and how CI never did.
 *
 * Three bugs reached the live demo through that gap, each one a thing that is only true on the
 * PMTiles path:
 *
 *   #15  the loading skeleton never cleared, because `setLoadingData(false)` lives only inside
 *        the fallback effect, which returns early when VITE_TILES_URL is set;
 *   #16  every bridge rendered grey, because the paint expression read `conditionClass` and the
 *        tiles ship the minified key `cond` — the API GeoJSON does carry `conditionClass`, so
 *        the identical expression was correct on the path CI exercised;
 *   #18  the filter rail did not reach the map, because the tile layer has its own client-side
 *        filter over those same minified keys.
 *
 * Each of the three has an assertion below that fails if its fix is reverted. Run with
 * `--project=tiles` and SPANSIGHT_TILES_DIR pointing at a built archive; see the README.
 */

const DESIGN = {
  Good: 'rgba(46,125,50,1)',
  Fair: 'rgba(230,168,23,1)',
  Poor: 'rgba(198,40,40,1)',
} as const
const UNKNOWN = 'rgba(92,107,122,1)'

/** A basemap that cannot fail. The bridge layer is what this file tests, not OpenFreeMap. */
const STUB_STYLE = {
  version: 8,
  sources: {},
  layers: [{ id: 'bg', type: 'background', paint: { 'background-color': '#e8eef3' } }],
}

type TileFeature = { id: string; cond: string; state: string; design: string; keys: string[] }

async function openMap(page: Page) {
  const requests: Request[] = []
  page.on('request', (r) => requests.push(r))
  await page.route('https://tiles.openfreemap.org/**', (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(STUB_STYLE) }),
  )

  await page.goto('/')
  // StrictMode double-mounts in dev, so the handle can be replaced under us — wait for a map that
  // is both current and finished loading rather than grabbing the first one that appears.
  await page.waitForFunction(
    () => {
      const map = (window as never as { __spansightMap?: maplibregl.Map }).__spansightMap
      return Boolean(map?.isStyleLoaded() && map.isSourceLoaded('bridges'))
    },
    undefined,
    { timeout: 30_000 },
  )
  await settle(page)
  return requests
}

/**
 * Wait for the map to stop working. `idle` is the right signal but it only fires if there was
 * something to do — after an interaction that changes nothing (which is exactly what a *broken*
 * filter looks like) it never fires at all, and the test hangs for the full timeout instead of
 * failing on its assertion. The bounded fallback keeps the failure in the assertion, where it
 * says something.
 */
async function settle(page: Page) {
  await page.evaluate(
    () =>
      new Promise<void>((resolve) => {
        const map = (window as never as { __spansightMap: maplibregl.Map }).__spansightMap
        const done = () => resolve()
        map.once('idle', done)
        setTimeout(done, 3000)
      }),
  )
}

/** Rendered bridge features, deduped by id — tile buffers repeat a feature across tiles. */
async function rendered(page: Page): Promise<TileFeature[]> {
  return page.evaluate(() => {
    const map = (window as never as { __spansightMap: maplibregl.Map }).__spansightMap
    const seen = new Map<string, TileFeature>()
    for (const f of map.queryRenderedFeatures({ layers: ['bridge-points'] })) {
      const p = f.properties as Record<string, string>
      seen.set(String(p.id), {
        id: String(p.id),
        cond: String(p.cond),
        state: String(p.state),
        design: String(p.design),
        keys: Object.keys(p),
      })
    }
    return [...seen.values()]
  })
}

// ---------------------------------------------------------------- the guard everything rests on

test('the map is actually reading tiles, and never the GeoJSON fallback', async ({ page }) => {
  const requests = await openMap(page)

  const archive = requests.filter((r) => r.url().endsWith('.pmtiles'))
  expect(archive.length, 'no .pmtiles request — the SPA is not in tiles mode').toBeGreaterThan(0)

  // pmtiles reads the archive with HTTP range requests. A server that answers 200 instead of 206
  // still renders, because pmtiles falls back to fetching the whole file under 26 MB — so a broken
  // range path would pass every other assertion here. Pin the 206.
  const ranged = archive.filter((r) => r.headers()['range'])
  expect(ranged.length, 'no ranged .pmtiles request').toBeGreaterThan(0)
  const response = await ranged[0].response()
  expect(response?.status()).toBe(206)
  expect(response?.headers()['content-range']).toMatch(/^bytes \d+-\d+\/\d+$/)

  // Without this the whole file passes vacuously if VITE_TILES_URL fails to reach the dev server:
  // the SPA would silently serve the fallback, and every tile assertion below would be satisfied
  // by the wrong code path.
  expect(
    requests.filter((r) => r.url().includes('/api/bridges/geojson')).map((r) => r.url()),
    'the SPA fetched GeoJSON — it is running the fallback path, not tiles',
  ).toEqual([])
})

// ---------------------------------------------------------------- #15

test('the loading skeleton never appears in tiles mode (PR #15)', async ({ page }) => {
  await openMap(page)

  await expect(page.locator('.skeleton')).toHaveCount(0)
  await expect(page.getByText('Loading bridges…')).toHaveCount(0)

  // The bug was a flag that nothing ever cleared, so it survived interaction. Toggle a filter and
  // look again.
  await page.getByRole('checkbox', { name: /Good/ }).uncheck()
  await settle(page)
  await expect(page.locator('.skeleton')).toHaveCount(0)
})

// ---------------------------------------------------------------- the tile schema itself

test('rendered features carry the exporter’s minified tile schema', async ({ page }) => {
  await openMap(page)
  const features = await rendered(page)

  // Guards every assertion below: an empty layer would satisfy all of them.
  expect(features.length, 'no bridge features rendered').toBeGreaterThanOrEqual(20)

  for (const f of features) {
    expect(f.keys, `feature ${f.id}`).toEqual(expect.arrayContaining(['id', 'cond', 'state', 'design']))
    // The property the #16 bug read. It is not in the tiles and never was — asserting its absence
    // is what stops the paint expression from quietly depending on it again.
    expect(f.keys).not.toContain('conditionClass')
    expect(f.id).toMatch(/^[A-Z]{2}-.+/)
  }

  const classes = new Set(features.map((f) => f.cond))
  for (const c of classes) expect(['Good', 'Fair', 'Poor', 'Unknown']).toContain(c)
  expect(classes.size, 'need more than one condition class in view to test colour').toBeGreaterThan(1)
})

// ---------------------------------------------------------------- #16

test('every published condition class paints its own colour (PR #16)', async ({ page }) => {
  await openMap(page)
  const features = await rendered(page)
  const paint = await page.evaluate(() => {
    const map = (window as never as { __spansightMap: maplibregl.Map }).__spansightMap
    return map.getPaintProperty('bridge-points', 'circle-color')
  })
  const zoom = await page.evaluate(
    () => (window as never as { __spansightMap: maplibregl.Map }).__spansightMap.getZoom(),
  )

  // Compiling the layer's own expression and evaluating it against the layer's own features is
  // what makes this catch #16 without a GPU or pixel sampling. Under the bug a {cond:'Good'} tile
  // feature evaluates to the Unknown token; the same expression still paints an API-shaped
  // {conditionClass:'Good'} feature green, so this cannot be satisfied by breaking the fallback.
  const compiled = createExpression(paint, latest.paint_circle['circle-color'])
  expect(compiled.result, 'the layer paint expression does not compile').toBe('success')

  for (const f of features) {
    const colour = String(compiled.value.evaluate({ zoom }, { properties: { cond: f.cond } }))
    if (f.cond in DESIGN) {
      expect(colour, `${f.id} is ${f.cond}`).toBe(DESIGN[f.cond as keyof typeof DESIGN])
      expect(colour, `${f.id} is ${f.cond} but paints as Unknown`).not.toBe(UNKNOWN)
    } else {
      expect(colour).toBe(UNKNOWN)
    }
  }

  // Belt and braces, and a better failure message: walk the compiled expression for the property
  // names it reads and assert at least one of them exists on the tiles. Under #16 the only
  // property referenced is `conditionClass`, which no tile feature has — an empty intersection
  // that names the mismatch instead of printing a colour.
  const read = new Set<string>()
  const walk = (node: unknown): void => {
    if (!Array.isArray(node)) return
    if (node[0] === 'get' && typeof node[1] === 'string') read.add(node[1])
    node.forEach(walk)
  }
  walk(paint)
  const available = new Set(features.flatMap((f) => f.keys))
  expect(
    [...read].filter((k) => available.has(k)),
    `the paint expression reads ${[...read].join(', ')}; the tiles carry ${[...available].join(', ')}`,
  ).not.toEqual([])
})

// ---------------------------------------------------------------- #18

test('the filter rail reaches the tile layer (PR #18)', async ({ page }) => {
  await openMap(page)
  const before = await rendered(page)
  expect(before.length).toBeGreaterThanOrEqual(20)

  await page.getByRole('checkbox', { name: /Good/ }).uncheck()
  await page.getByRole('checkbox', { name: /Fair/ }).uncheck()
  await settle(page)

  const after = await rendered(page)
  // Relative, never absolute: tippecanoe's drop-rate thins low zooms, so the count in view is a
  // property of the tile pyramid rather than of the fixture.
  expect(after.length, 'unchecking two classes removed nothing').toBeLessThan(before.length)
  for (const f of after) expect(f.cond, `${f.id} survived the filter`).toBe('Poor')
})

// ---------------------------------------------------------------- the drawer, from a tile

test('clicking a tile feature deep-links to its drawer', async ({ page }) => {
  await openMap(page)
  const features = await rendered(page)
  const target = features.find((f) => f.cond === 'Poor') ?? features[0]

  const point = await page.evaluate((id: string) => {
    const map = (window as never as { __spansightMap: maplibregl.Map }).__spansightMap
    const hit = map
      .queryRenderedFeatures({ layers: ['bridge-points'] })
      .find((f) => String((f.properties as Record<string, string>).id) === id)
    const [lng, lat] = (hit!.geometry as GeoJSON.Point).coordinates
    const p = map.project([lng, lat])
    const box = map.getCanvas().getBoundingClientRect()
    return { x: box.x + p.x, y: box.y + p.y }
  }, target.id)

  await page.mouse.click(point.x, point.y)

  // Asserted against *any* rendered feature rather than against `target`: several bridges can sit
  // within a few pixels at this zoom, so which one wins the click is not the app's contract. What
  // is the app's contract — and what nothing else on the tile path asserts — is the round trip of
  // the `id` property, written by GeoJsonExporter as "{abbrev}-{structureNumber}" and split back
  // apart by the click handler.
  await expect(page).toHaveURL(/\/bridge\/[A-Z]{2}\/.+$/, { timeout: 20_000 })

  const [, state, structureNumber] = /\/bridge\/([A-Z]{2})\/([^/?#]+)$/.exec(page.url())!
  expect(
    features.map((f) => f.id),
    'the drawer opened on a structure the tile layer never rendered',
  ).toContain(`${state}-${decodeURIComponent(structureNumber)}`)

  await expect(page.getByRole('region', { name: /Bridge detail/i })).toBeVisible({ timeout: 20_000 })
})
