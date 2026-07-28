# The end-to-end suite

Two projects, because the SPA has two data paths.

| Project | Data path | Spec | Where it runs |
|---|---|---|---|
| `chromium` | API GeoJSON fallback | `smoke.spec.ts` | every PR, and post-deploy against the live SPA |
| `tiles` | PMTiles vector tiles | `tiles.spec.ts` | every PR (the `tiles` matrix leg) |

`tiles` is the path **production** runs. Until P1-W6 nothing in CI exercised it, and three bugs
reached the live demo through that gap — a stuck loading skeleton (PR #15), every bridge painted
grey (PR #16), and the filter rail not reaching the map (PR #18). Each was structurally invisible
to a fallback-only suite, because each is a thing that is only true when `VITE_TILES_URL` is set.

## Running the fallback suite

```bash
npm run e2e
```

Playwright starts the dev server; the API and a loaded database must already be up (see the repo
README and the `e2e` job in `.github/workflows/ci.yml`).

## Running the tiles suite

It needs a real PMTiles archive. Nothing is committed; the output lands in gitignored `artifacts/`.

`build-tiles.sh` exports whatever is in the database it is pointed at, and on a dev machine that is
usually the full national load — which builds fine (22 MB) and the suite passes against it, but
takes a few minutes. For a 10 KB archive that matches what CI builds, point it at a scratch
database holding only the fixture:

```bash
CS="Host=localhost;Port=5432;Database=spansight_e2e;Username=spansight;Password=spansight"
createdb -h localhost -U spansight spansight_e2e     # once
dotnet run --project src/SpanSight.Ingestion -- load \
  --file src/tests/fixtures/nbi_sample_2025.csv --snapshot-year 2025 --connection "$CS"
tools/build-tiles.sh --out-dir artifacts/tiles --connection "$CS"
```

Point the API at the same database (`ConnectionStrings__SpanSight="$CS"`) so the drawer test reads
the same structures the tiles carry. Requires `tippecanoe` (`brew install tippecanoe`; CI builds
2.79.0 from source and caches it).

Then, from `web/`:

```bash
SPANSIGHT_TILES_DIR="$PWD/../artifacts/tiles" \
VITE_TILES_URL=http://127.0.0.1:8081/bridges.pmtiles \
VITE_E2E_MAP_HANDLE=1 \
  npm run e2e -- --project=tiles
```

`SPANSIGHT_TILES_DIR` does two things: it registers the `tiles` project, and it starts
`tiles-server.mjs` for the archive. Both are conditional on purpose — `--project=tiles` without it
fails with *project not found* rather than quietly running zero tests.

If you already have a dev server on 5173 from an earlier run, **kill it first**. Vite reads
`VITE_*` at start, and `reuseExistingServer` is on locally, so a stale server silently serves the
app in fallback mode and the mode guard is what catches it.

## Three things that are load-bearing, and look like over-engineering

**The mode guard runs first and everything depends on it.** `tiles.spec.ts` asserts that a
`.pmtiles` request happened, that it was ranged and got a 206, that **no** request went to
`/api/bridges/geojson`, and — separately — that at least twenty bridge features actually decoded.
The first three certify the *mode* and would all hold for an archive that rendered nothing, which
is why the fourth is there. Without the guard the entire file passes vacuously if `VITE_TILES_URL`
fails to reach the dev server: the SPA quietly serves the fallback and every tile assertion below
is satisfied by the wrong code path.

**`tiles-server.mjs` is not a convenience.** It answers HTTP Range with 206, and it serves from
`127.0.0.1:8081` — a different origin from the SPA. `python3 -m http.server` ignores Range and
returns 200; pmtiles 3.2.1 aborts and throws on a 200 whose `Content-Length` exceeds the requested
length (*"Check that your storage backend supports HTTP Byte Serving"*), so the map would render
nothing at all — byte serving is not an optimisation here, it is the difference between a map and
no map. The separate origin is what forces the CORS preflight (`Range` is not a safelisted request
header), which is the classic way this breaks in production. Its header set mirrors
`infra/modules/storage.bicep` — keep the two side by side.

**The skeleton is asserted from both sides.** `tiles.spec.ts` says `.skeleton` never appears in
tiles mode; on its own, deleting the element satisfies that. `smoke.spec.ts` holds the GeoJSON
response open and asserts the skeleton shows and then clears. Neither is worth much alone.

## Assertions to keep relative, not absolute

tippecanoe's default drop-rate thins low zooms, and tile buffers repeat a feature across tiles —
measured with `tippecanoe-decode` on the archive the committed fixture actually builds (99 loaded
features): z0=3, z1=7, z2=21, z3=54, **z4=113**. So: dedupe by
`properties.id`, assert counts relatively (fewer after a filter, not *n* after a filter), and read
`getSource('bridges').maxzoom` at runtime rather than hardcoding a zoom. `-zg` derives the maxzoom
from the data, so the pyramid changes when the fixture does.

Do not "fix" thinning by passing `-r1` or a fixed `-z` in CI. Deviating from the flags
`build-tiles.sh` uses in production removes the reason this suite exists.
