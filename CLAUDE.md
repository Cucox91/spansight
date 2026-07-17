# SpanSight — AI Session Context

Portfolio project by Raziel Arias: a national bridge-inventory intelligence platform built on public FHWA NBI data (~624k bridges). Purpose: demonstrate senior-level C#/.NET + React + Azure engineering. This file is committed intentionally as a transparency artifact (see docs/AI-USAGE.md).

## Read before working

- `docs/SDLC.md` — lifecycle model, phase gates, change control, artifact index
- `docs/REQUIREMENTS.md` — SRS: scope, ground rules (GR-1…GR-7), use cases, FRs with acceptance criteria, NFRs
- `docs/TRACEABILITY.md` — RTM; update the row when a PR closes or changes a requirement
- `docs/ARCHITECTURE.md` — ADRs; all-Azure topology (ADR-006-B)
- `docs/IMPLEMENTATION-PLAN.md` — current WBS with [ME]/[AI] task tags
- `docs/AI-USAGE.md` — the AI policy governing every session

## Hard rules — never violate

1. **Zero FDOT content** (GR-1/GR-2): no FDOT data, APIs, branding, or references anywhere. Florida data only from federal or county sources (e.g., Miami-Dade Transit).
2. **Own-the-code review** (AI-USAGE v1.1, amended 2026-07-17): AI may implement any component, including first-of-a-kind cores. Every line is still reviewed by Raziel before merge; merge bar unchanged (*can't rewrite it → don't merge it*); one core per phase gets hand-rebuilt by Raziel as a mastery kata. PRs with meaningful AI code get the `ai-assisted` label. *(v1.0 reserved parser/converter/schema/first endpoint/first component for hand-writing — superseded.)*
3. **Small, reviewable diffs.** Raziel reviews every line before merge; prefer several small commits/PRs over one large one. PRs with meaningful AI-written code get the `ai-assisted` label.
4. **No bulk NBI data in git** — test fixtures only (a few hundred rows max).
5. **Azure discipline:** never create/modify cloud resources except through `infra/` Bicep; monthly budget ≤ $50 (alert at $40).
6. **Not engineering advice:** UI must keep the disclaimer footer; app displays published data, never computes engineering judgments.

## Stack (pinned 2026-07)

.NET 10 LTS · ASP.NET Core + SignalR · EF Core + Npgsql + NetTopologySuite · React + TypeScript + Vite · MapLibre GL JS · PostgreSQL 16 + PostGIS · Redis (Streams) · Parquet + DuckDB (analytics, local) · tippecanoe → PMTiles · OpenTelemetry → App Insights (cloud) / Grafana (local) · Bicep · GitHub Actions (OIDC to Azure) · Node 24 LTS (GTFS-RT poller only, Phase 2).

## Conventions

Trunk-based; short-lived branches; conventional commits; PR for everything (template in `.github/`); CI must be green; `main` always deployable; xUnit + Testcontainers + one Playwright smoke; ADR in `docs/` for any surprising decision; requirement changes follow SDLC.md §4 (SRS edit + change-log row + RTM update, by PR).

## Layout

`src/SpanSight.Core|Api|Ingestion` + `src/tests/` · `web/` (SPA) · `infra/` (Bicep) · `tools/` (tiles/DuckDB scripts) · `docs/` (SDLC set).

## Current status (keep this section updated at every phase gate)

**Phase 0, Weeks 2–4 implementation pass — on branch `week2-4-implementation`, PR open for line review.** A Cowork session executed the Weeks 2–4 workload under AI-USAGE v1.1 (policy amended same day: AI implements, Raziel reviews line-by-line; see its change log); a Claude Code session verified it on the dev Mac and committed it 2026-07-17. Also that day: SDLC formalized (`docs/SDLC.md`, SRS v1.1 with Phase 0 acceptance criteria + Phase 0.5 AI series FR-AI.1–3, `docs/TRACEABILITY.md` RTM, ADR-008).

Delivered: Core domain (parser, DMS converter, validators, classifier, lookups, EF model + `InitialSchema` migration, BridgeFilter, AI port scaffold) · Ingestion CLI (idempotent unnest-upsert load, quarantine, run summaries, GeoJSON export) · API (bridges/geojson/detail/lookups/stats/qa/ai endpoints, ProblemDetails, rate limiting, CORS, health, OpenAPI+Scalar, OTel config-gated) · test suites (unit + Testcontainers integration) · fixture + generator · web SPA (explorer, drawer, QA page per mockup).

**Verified on the dev Mac 2026-07-17:** `dotnet build` 0 warnings · `dotnet format` clean · **106/106 tests green** including the Testcontainers integration suite against real PostGIS · `web/` lint + build clean · full-stack smoke pass (fixture load 114/99/15 + idempotent no-op rerun, map → filter → drawer deep link `/bridge/FL/…` → `/qa` reconciling exactly). Four small fixes landed during verification: GeoJSON typing in `BridgeMap`, root-anchored `/data/` gitignore (bare `data/` swallowed `src/**/Data/` on case-insensitive macOS), ingestion CLI content root, AbortError filter in `QaPage`.

Earlier done: docs set v1, repo public, Week 1 scaffold on `main`, Azure trial ($200/30d — upgrade to PAYG before it lapses). **Swiftly access granted 2026-07-17** (agencyKey `miami`, GTFS-rt trip updates + vehicle positions, 180 req/15 min) — risk R-4 fallback closed; key lives in Raziel's password manager, enters `.env`/GitHub secrets only when Phase 2 starts (NFR-8 §10 — never in the repo).

Next: [ME] line-review + merge the `week2-4-implementation` PR (`ai-assisted`) · `tools/build-tiles.sh` · root README with "How AI was used" · refresh `SpanSight.Api.http` · [ME] Azure budget alert · [ME] real 2025 snapshot download + load · [ME] pick rebuild kata (parser or DMS) · `Ai:Enabled` stays false until the Phase 0.5 gate.
