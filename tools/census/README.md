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
tools/census/join-counties.sh     # AC-2: assign bridges to counties and measure the gap (W5)
```

Requires `duckdb` (`brew install duckdb`) with the spatial extension, `python3` and `unzip`.

**Everything except `join-counties.sh` stages data and nothing else** — the four staging scripts
never read bridge data, and each says so in its own header. `join-counties.sh` is the one script
here that reads both sides; it arrived with W5 and is documented under [The
join](#the-join-fr-15-ac-2) below.

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

**Connecticut has planning regions, not counties — and NBI never followed.** Since 2022 Census
publishes nine *planning regions* for CT (`09110`–`09190`, `CLASSFP` `H5`) in place of the eight
historic counties (`09001`–`09015`). Boundaries and population agree with each other here because
they are the same vintage.

The W5 join measured what NBI does about it: **nothing**. Item 3 publishes the eight legacy county
codes in *every* vintage through 2025 — not just the older half of the series — so every Connecticut
row in the 2025 snapshot carries a code the current boundary file does not contain: **5,644 served
rows, of which 4,362 are record-type-1 structures**. All nine planning regions carry zero NBI-coded
bridges. That is 8 of the 8 codes absent and every one of the state's structures affected, measured
2026-07-27 and published by `cj_diagnostic_retired_codes`, which reports both counts because they
differ by 1,282 rows that are routes under a structure rather than bridges.

It is therefore not a gap that closes as the series moves forward, and it is not evidence that a
coordinate is wrong: the coordinate is fine and lands in the right planning region, while the
published code names a county that no longer exists. `nbi_fips_in_tiger` on each disagreement row is
the flag that separates the two, and the QA page states the distinction next to the number. This is
precisely what the AC-2 join-coverage metric exists to surface.

*(An earlier version of this paragraph said NBI vintages "from before the change" carry the retired
codes, implying NBI adopted the planning regions afterwards. It did not. `tools/trends/trends.sql`
still carries the same wrong claim and is corrected separately.)*

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

## The join (FR-1.5 AC-2)

Staging is only half of FR-1.5. `join-counties.sh` assigns each served bridge to the county polygon
that contains it, cross-checks that against the county code item 3 published, and publishes the gap.
The method is `county-join.sql` — read that file, not the script.

```bash
tools/census/join-counties.sh              # the serving database + the real TIGER/ACS set
tools/census/join-counties.sh --fixtures   # committed hand-placed points + the 6-county fixture (CI)
dotnet run --project src/SpanSight.Ingestion -- load-county-join --file data/census/join
```

Publishing procedure and rollback: [RUNBOOK §10.5](../../docs/RUNBOOK.md).

> A disagreement is a **measurement, not a correction**. Item 3 remains the county SpanSight reports
> everywhere — the filters, the FR-1.2 trend rollups, the FR-1.4 county report card. This job says
> how often the coordinate says otherwise, and nothing more (GR-6).

### What it produces

Run of 2026-07-27 against the 2025 snapshot (741,131 served rows, 3,235 counties):

| Relation | Rows | What it is |
|---|---|---|
| `county.csv` | 3,235 | Every TIGER county with its ACS population — including the 27 carrying no NBI-coded bridge, so a report card for an empty county can name it |
| `miss.csv` | 55 | Every served row inside no polygon (19 of them record-type-1 structures), with a reason, the nearest county and the distance to it |
| `disagreement.csv` | 4,022 | Each (published code → containing polygon) pair that disagrees, with the served rows and the structures taking it |

**Coverage: 741,076 of 741,131 matched — 99.9926%.** Restricted to record type 1 (the structure
itself, which is what "bridge" means in FR-1.2 and FR-1.3): 623,331 of 623,350, or **99.9970%**.

Both denominators are published because they are genuinely different populations: the serving table
also holds the route records published *under* a structure, and quoting only one of the two answers
a different question than AC-2 asks.

### Why the bridge side comes from the database

Two reasons, both about the number meaning what it says. AC-2 asks for the share of the bridges a
reader is *served*, and those are the rows in `core.bridge` — measuring a different population
publishes a percentage for an inventory nobody sees. And the WGS84 point there is produced by
`NbiDmsCoordinateConverter`, which decodes items 016/017 from DMS and negates the west-positive
longitude NBI publishes; redoing that in SQL would be a second implementation of the conversion this
repo has tested hardest, whose failure mode is silent — a missed negation reports ~0% coverage
rather than an error.

DuckDB reads it through the `postgres` scanner, with `ST_X`/`ST_Y` evaluated server-side so PostGIS
geometry never has to cross the wire in a format the scanner would need to understand. Local
PostGIS was the alternative and was rejected: the join would then run inside the serving database
(ADR-005 keeps heavy analytics in DuckDB), and the 99 MB of county polygons would have to be loaded
into a B1ms instance to support a query that runs in three seconds offline.

### The two choices that move the numbers

**`ST_Within`, so a boundary point is quarantined rather than assigned.** The predicate excludes the
boundary, so a structure sitting exactly on a county line is inside neither county. Assigning it to
whichever polygon sorts first would be SpanSight inventing a geography the publisher did not state,
and the case is real — bridges frequently *are* the county line, because the boundary follows the
river they cross. Measured: 2 structures nationally, both quarantined as `on_county_boundary`. The
predicate travels in the manifest, on the run row and in the QA payload, so the rule a coverage
figure was measured under is never separated from the figure.

**Misses are quarantined with evidence, not just a reason.** TIGER boundaries stop at the
international border and at the shoreline, and NBI structures legitimately sit on both, so each miss
records the nearest county and the metres to it. That is what separates a metre of shoreline slop
from a coordinate in the wrong ocean — both are otherwise just "unmatched". The 2026-07-27 spread
over the 55 unmatched served rows: 13 under 100 m, 3 between 100 m and 1 km, 10 between 1 and 10 km,
27 over 10 km, and 2 on a boundary.

Every count in this section is a **served-row** count unless it says "structures". The serving table
holds the routes NBI publishes *under* a structure as their own records, and they are 19% of it — so
`cj_disagreement` and `cj_diagnostic_retired_codes` each publish both numbers, and the QA page labels
its columns accordingly. A row count under the word "structures" is wrong by a fifth, which is how
"5,644 Connecticut structures" would have shipped as a figure that is really 4,362.

### How the SQL is kept honest

Eight invariant views, all of which must return zero rows before anything is written. The two worth
naming:

`cj_check_reconciles` compares the input against assigned-plus-quarantined with a **full outer
join**, not arithmetic over the coverage row — a structure that vanished from both sides is
invisible to a sum of the two sides, and that is the failure it exists to catch.

`cj_check_sign_convention` guards the west-positive trap. A decoder that stopped negating mirrors
every point into the eastern hemisphere; row counts survive that unchanged, so the guard is the
share of matched structures landing in the state item 1 published (99.9% measured, 90% floor) **and,
separately, whether anything matched at all** — because a mirrored hemisphere matches no polygon,
which would otherwise satisfy every check in the file.

`CountyJoinGoldenTests` executes this same SQL over 16 hand-placed points covering all five outcomes
and both miss reasons, asserts each invariant returns nothing, and then mutates a relation per
invariant to prove each one *can* fail. Two of the eight originally could not: `cj_check_miss_reason`
compared a miss's reason against the very column that reason was derived from, and
`cj_check_disagreement_resolves` asked a `count(*)` whether it was positive. Both now recompute
independently — the first from the polygons, the second against `cj_coverage` — which is the
det_check_span lesson applied twice more. The fixture and its expected numbers are documented in
[`src/tests/fixtures/census-join/README.md`](../../src/tests/fixtures/census-join/README.md).
