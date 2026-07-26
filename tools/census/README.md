# Census staging — county boundaries + population (FR-1.5)

Stages the two public-domain Census products the Phase 1 county analytics need: TIGER/Line county
boundaries (the polygon side of a point-in-polygon assignment) and ACS 5-year county population
(the denominator for per-capita figures). Both land as Parquet that DuckDB reads directly, next to
the NBI vintage catalog.

```
tools/census/download.sh          # fetch into data/census/raw/ (gitignored)
tools/census/convert.sh           # → data/census/parquet/{counties,county_population}.parquet
tools/census/convert.sh --fixtures  # the committed six-county fixture (what CI runs)
tools/census/verify-fixtures.sh   # assert the fixture outputs are actually usable (what CI runs)
tools/census/make-fixtures.sh     # re-cut the committed fixtures from a real download
```

Requires `duckdb` (`brew install duckdb`) with the spatial extension, `python3` and `unzip`.

**This stages data and nothing else.** Bridges gain nothing here: the spatial join, the
join-coverage metric and any population-served figure are W5 (FR-1.5 AC-2/AC-3). No script in this
directory reads bridge data.

## What is staged, exactly

Recorded per source in `tools/census/catalog.json` (committed): URL, SHA-256, byte size, upstream
`Last-Modified`, vintage, citation, licence and disclaimer (NFR-8).

| | Boundaries | Population |
|---|---|---|
| Product | TIGER/Line County and Equivalent Entity, National | ACS 5-Year Estimates, table B01003 (total population) |
| Vintage | **2025** — released 2025-09-23, boundaries as of 2025-01-01 | **2024 (2020–2024)** — released 2026-01-29 |
| File | `tl_2025_us_county.zip` (84 MB) | `acsdt5y2024-b01003.dat` (18 MB) |
| Rows | 3,235 counties / equivalents | 3,222 county rows (of 616,690 across all summary levels) |
| Output | `counties.parquet` — `GEOMETRY` in lon/lat WGS84 | `county_population.parquet` — keyed by 5-digit `GEOID` |

Both are US Government works: **public domain**, not eligible for copyright (17 U.S.C. §105). The
Census Bureau asks only to be cited, and asks that repackaged TIGER data say so — the citation and
disclaimer strings live in `catalog.json` so anything published from this geometry can carry them.

## Why the bulk file and not the API

`api.census.gov` **requires an API key for every data request** — verified 2026-07-26, and it is
unconditional: a single-row query returns `302 → missing_key.html` exactly like a national one. The
old keyless low-volume allowance is gone from the current API User Guide.

A key would mean a secret in `.env` and GitHub secrets for data that is entirely public. The
table-based Summary File on `www2.census.gov` needs no key at all and is the same published
numbers, so that is what `download.sh` fetches. (Metadata endpoints on the API stay key-free, which
is how the script can still assert a vintage exists without holding a secret.)

## The quirks that actually matter

**Connecticut has planning regions, not counties.** Since 2022 Census publishes nine *planning
regions* for CT (`09110`–`09190`, `CLASSFP` `H5`) in place of the eight historic counties
(`09001`–`09015`). Boundaries and population agree with each other here because they are the same
vintage — but **NBI vintages from before the change carry the retired county codes**, so a naive
join on `COUNTY_CODE_003` will drop every Connecticut bridge in the older half of the series. This
is a W5 problem, and it is precisely what the AC-2 join-coverage metric exists to surface. Noted
here so it is found by design rather than by a suspicious gap in a chart.

**13 counties have a boundary but no population.** ACS 5-year covers the 50 states, DC and Puerto
Rico — not American Samoa (`60`), Guam (`66`), the Northern Mariana Islands (`69`) or the US Virgin
Islands (`78`). 3,235 boundaries against 3,222 population rows is correct, not a download that went
wrong. The converter records the gap in `catalog.json` rather than hiding it.

**Puerto Rico is in the population file but outside the national total.** 3,222 = 3,144 states + DC,
plus 78 PR municipios. Summing all 3,222 and comparing to the published national figure will always
disagree; summing the 3,144 gives **334,922,499**, which equals the published `0100000US` row
exactly. `convert.sh` asserts that equality on every real run — a free end-to-end integrity check,
the same idea as the vintage row reconciliation.

**FIPS codes are text, forever.** `06037` is not the number 6037. Every FIPS-bearing column stays
`VARCHAR` through the whole pipeline, and the fixture verification asserts it, because the moment
one becomes an integer the leading zero is gone and the join to NBI's (also textual) state and
county codes silently matches nothing.

**Jam values are not populations.** ACS encodes "not available" as large negative sentinels
(`-555555555`, and `-666666666` / `-999999999` elsewhere), in estimate columns as well as margins.
Anything negative becomes NULL and the count is recorded, rather than being averaged into a
statistic later.

**The margin of error travels with the estimate.** ACS publishes survey *estimates*, not counts. A
bridges-per-capita figure divides a near-census count by a sampled estimate, so `POP_MOE` is
carried alongside `POP_EST` and has to be shown wherever a derived figure is (GR-6).

**Axis order is pinned.** TIGER ships NAD83 (`EPSG:4269`) whose CRS declares latitude first;
everything downstream — NBI items 016/017, PMTiles, PostGIS — is lon/lat. `convert.sh` sets
`geometry_always_xy` and reprojects to WGS84 explicitly, and checks the source `.prj` rather than
assuming it, because a shapefile missing its `.prj` still opens fine and would silently assert a
datum the data never had.

## Fixtures (what CI runs)

`src/tests/fixtures/census/` holds a real six-county shapefile and the matching ACS rows — ~170 KB,
cut from the real downloads by `make-fixtures.sh` (deterministic; re-running produces identical
bytes). The six are chosen to carry the edge cases rather than six well-behaved rows:

| County | Why |
|---|---|
| `06037` Los Angeles CA | largest population, 9,808,667 |
| `12086` Miami-Dade FL | Florida from the **federal** TIGER file — never a state DOT source (GR-1) |
| `17031` Cook IL | second large metro, ordinary case |
| `48301` Loving TX | smallest population in the country, 33 |
| `72001` Adjuntas PR | in ACS, but outside the national total |
| `60010` Eastern District AS | has a boundary, has **no** ACS row at all |

The population fixture also carries 280 rows from *other* summary levels whose `GEO_ID` ends in one
of the same five FIPS codes. Filtering counties by the `0500000US` prefix keeps 5; filtering by a
trailing-five-characters suffix — the tempting shortcut — would keep 285. CI asserts 5.

`verify-fixtures.sh` then checks what a row count cannot see: four real city coordinates land in
the right counties, a mirrored `+118` longitude and a mid-Atlantic point land in none (the NBI
west-positive-longitude trap), GEOIDs are still 5-character text with `06037` intact, and no
negative population survived.

## Reproducing from a clean checkout

```bash
git clone https://github.com/Cucox91/spansight && cd spansight
brew install duckdb
tools/census/download.sh     # ~102 MB from www2.census.gov; re-runnable, skips what it has
tools/census/convert.sh      # → data/census/parquet/ + the conversion record in catalog.json
```

Then, from the repository root:

```bash
duckdb -c "LOAD spatial;
  SELECT c.GEOID, c.NAMELSAD, p.POP_EST, p.POP_MOE
  FROM read_parquet('data/census/parquet/counties.parquet') c
  JOIN read_parquet('data/census/parquet/county_population.parquet') p USING (GEOID)
  ORDER BY p.POP_EST DESC LIMIT 5;"
```

Nothing but `catalog.json` and the fixtures is committed — no bulk data in git (CLAUDE.md rule 4).
