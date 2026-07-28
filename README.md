# SpanSight

A national bridge-inventory intelligence platform built on public [FHWA National Bridge Inventory](https://www.fhwa.dot.gov/bridge/nbi/ascii.cfm) data (~624k bridges): ingest → validate → explore on a map, with data quality treated as a feature, not a footnote.

**Live demo:** [www.spansights.com](https://www.spansights.com)

**Portfolio project by Raziel Arias** — built to demonstrate senior-level C#/.NET, React/TypeScript, and Azure engineering with a documented, disciplined SDLC. It displays published inventory values only; it is **not engineering advice** and has no affiliation with any Department of Transportation.

## What it does

### The current snapshot (Phase 0)

- **Ingestion CLI** (.NET 10): parses the annual NBI snapshot, converts DMS-encoded coordinates to WGS84, validates every row (10 machine-readable quarantine reason codes), and upserts idempotently into PostgreSQL/PostGIS — reruns are SHA-256-detected no-ops, run summaries land in `ops.ingestion_run`.
- **REST API** (ASP.NET Core): filterable bridge queries (state, county, condition, structure type/material, year, bbox), decoded detail records, stats, and a QA summary that reconciles exactly with the ingestion run — with ProblemDetails, rate limiting, health checks, and OpenAPI/Scalar.
- **Map explorer** (React + MapLibre GL): national condition-colored map, instant filters driven by one shared predicate, deep-linkable bridge drawer (`/bridge/{state}/{id}`), and a Data QA page.
- **Static vector tiles** ([ADR-002](docs/ARCHITECTURE.md)): `tools/build-tiles.sh` exports `core` → GeoJSONSeq → tippecanoe → a single PMTiles artifact plus a manifest tied to the ingestion run that produced it. No tile server.

### Thirty-four years of it (Phase 1)

Every NBI vintage from 1992 to 2025 — **22,307,363 published rows** — converted to Parquet and reconciled vintage by vintage, then reduced offline with DuckDB into the compact aggregates the site serves ([ADR-005](docs/ARCHITECTURE.md)). The 50 million individual transitions stay in Parquet; about 235 MB reaches the serving database.

- **Condition trends** (FR-1.2): a Good/Fair/Poor series per structure across all 34 vintages — 1,039,109 structures, 20,649,259 observations — as a sparkline in the drawer and a deep-linkable `/trends` view per state or county. A year FHWA did not publish is a gap, and stays a gap.
- **Deterioration patterns** (FR-1.3): 10×10 rating-transition matrices by structure type × material × NOAA climate region, over 19.5 million structure pairs. Every rate is suppressed below **n ≥ 50** — counts and year-spans are still served, so suppression removes the rate and never the evidence — and every matrix carries its method note, methodology version and cadence caveat from the API, so no view can render one bare. The method is written down: [docs/METHODOLOGY-DETERIORATION.md](docs/METHODOLOGY-DETERIORATION.md).
- **Rankings and county report cards** (FR-1.4): worst-condition by state, county or cohort, and high-traffic structures in Poor condition, each **serving its own definition** — headline, sort rule, inclusion rule, exclusion rule, share denominator — rendered inside the same region as the rows. Every view exports server-generated CSV carrying that definition as leading comment lines, so the copy that leaves the building cannot be read without the rule that produced it.
- **The Census join** (FR-1.5): TIGER county boundaries and ACS population, point-in-polygon against every served structure — **741,076 of 741,131 matched (99.99%)**, all 55 misses quarantined with a reason and the metres to the nearest county. Published as a coverage block on the QA page, and kept as a cross-check rather than an override: the report card is still keyed on the county code NBI itself publishes.

Nothing here predicts, scores, weights or prioritises anything. Every number is a descriptive statistic of published federal inspection ratings ([GR-6](docs/REQUIREMENTS.md)).

Later phases add GTFS-Realtime live operations over Miami-Dade Transit and an AI assist series — see the [roadmap](docs/REQUIREMENTS.md).

## Getting started

```bash
docker compose up -d postgres          # PostGIS 16
dotnet run --project src/SpanSight.Ingestion -- load \
  --file src/tests/fixtures/nbi_sample_2025.csv --snapshot-year 2025
dotnet run --project src/SpanSight.Api  # http://localhost:5194 (Scalar UI at /scalar/v1)
cd web && npm install && npm run dev    # http://localhost:5173
```

`dotnet test` runs the full suite, including Testcontainers integration tests against real PostGIS when Docker is available.

## Engineering artifacts

The docs set ships with the product on purpose — process is part of the portfolio:

| | |
|---|---|
| [docs/SDLC.md](docs/SDLC.md) | Lifecycle model, phase gates, change control |
| [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) | SRS: ground rules, FRs with acceptance criteria, NFRs |
| [docs/TRACEABILITY.md](docs/TRACEABILITY.md) | Requirements traceability matrix |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | C4 views + ADRs |
| [docs/IMPLEMENTATION-PLAN.md](docs/IMPLEMENTATION-PLAN.md) | WBS, conventions, definition of done |
| [docs/METHODOLOGY-DETERIORATION.md](docs/METHODOLOGY-DETERIORATION.md) | How the transition matrices are computed, and what they do not mean |
| [docs/RUNBOOK.md](docs/RUNBOOK.md) | Deploy, data publish and rollback procedures |
| [docs/AI-USAGE.md](docs/AI-USAGE.md) | The AI policy governing every session |

## How AI was used

Openly, and under a written policy with a public change log: **[docs/AI-USAGE.md](docs/AI-USAGE.md)**. The short version — AI (Claude) implements the product under my direction; merges are gated by green CI (full test suite including Testcontainers integration against real PostGIS, lint, scans) plus AI self-review against the policy's hard rules. Every architectural decision is mine and recorded in ADRs; credentials, billing, and account actions never touch AI. PRs where AI wrote a meaningful share carry the `ai-assisted` label — since 2026-07-17 that is most implementation PRs, and the policy's change log records that shift plainly (v1.1 required line-by-line pre-merge review; v1.2 moved my deep review to a structured post-completion code study, where I work through the codebase until I can explain and rebuild any part of it).

## License & data

Code © Raziel Arias. Built on public-domain US federal data (FHWA NBI). Basemap © [OpenFreeMap](https://openfreemap.org) · OpenMapTiles · OpenStreetMap contributors — attribution is shown in the app footer.
