# SpanSight

A national bridge-inventory intelligence platform built on public [FHWA National Bridge Inventory](https://www.fhwa.dot.gov/bridge/nbi/ascii.cfm) data (~624k bridges): ingest → validate → explore on a map, with data quality treated as a feature, not a footnote.

**Portfolio project by Raziel Arias** — built to demonstrate senior-level C#/.NET, React/TypeScript, and Azure engineering with a documented, disciplined SDLC. It displays published inventory values only; it is **not engineering advice** and has no affiliation with any Department of Transportation.

## What it does (Phase 0)

- **Ingestion CLI** (.NET 10): parses the annual NBI snapshot, converts DMS-encoded coordinates to WGS84, validates every row (10 machine-readable quarantine reason codes), and upserts idempotently into PostgreSQL/PostGIS — reruns are SHA-256-detected no-ops, run summaries land in `ops.ingestion_run`.
- **REST API** (ASP.NET Core): filterable bridge queries (state, county, condition, structure type/material, year, bbox), decoded detail records, stats, and a QA summary that reconciles exactly with the ingestion run — with ProblemDetails, rate limiting, health checks, and OpenAPI/Scalar.
- **Map explorer** (React + MapLibre GL): national condition-colored map, instant filters driven by one shared predicate, deep-linkable bridge drawer (`/bridge/{state}/{id}`), and a Data QA page.
- **Static vector tiles** ([ADR-002](docs/ARCHITECTURE.md)): `tools/build-tiles.sh` exports `core` → GeoJSONSeq → tippecanoe → a single PMTiles artifact plus a manifest tied to the ingestion run that produced it. No tile server.

Later phases add historical analytics (Parquet + DuckDB), GTFS-Realtime live operations over Miami-Dade Transit, and an AI assist series — see the [roadmap](docs/REQUIREMENTS.md).

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
| [docs/AI-USAGE.md](docs/AI-USAGE.md) | The AI policy governing every session |

## How AI was used

Openly, and under a written policy: **[docs/AI-USAGE.md](docs/AI-USAGE.md)**. The short version — AI (Claude) drafts implementation across the codebase; I review **every line** before merge with one bar: *if I couldn't rewrite it, I don't merge it*. Every architectural decision is mine and recorded in ADRs. PRs where AI wrote a meaningful share carry the `ai-assisted` label. To keep the mastery claim honest, each phase I rebuild one core component by hand from a blank file and keep a weekly AI-free coding rep.

## License & data

Code © Raziel Arias. Built on public-domain US federal data (FHWA NBI). Basemap © [OpenFreeMap](https://openfreemap.org) · OpenMapTiles · OpenStreetMap contributors — attribution is shown in the app footer.
