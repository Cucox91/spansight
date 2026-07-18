# SpanSight — AI Session Context

Portfolio project by Raziel Arias: a national bridge-inventory intelligence platform built on public FHWA NBI data (~624k bridges). Purpose: demonstrate senior-level C#/.NET + React + Azure engineering. This file is committed intentionally as a transparency artifact (see docs/AI-USAGE.md).

## Read before working

- `docs/SDLC.md` — lifecycle model, phase gates, change control, artifact index
- `docs/REQUIREMENTS.md` — SRS: scope, ground rules (GR-1…GR-7), use cases, FRs with acceptance criteria, NFRs
- `docs/TRACEABILITY.md` — RTM; update the row when a PR closes or changes a requirement
- `docs/ARCHITECTURE.md` — ADRs; all-Azure topology (ADR-006-B)
- `docs/IMPLEMENTATION-PLAN.md` — current WBS ([ME]/[AI] tags are historical; AI executes all tasks since AI-USAGE v1.2)
- `docs/AI-USAGE.md` — the AI policy governing every session

## Hard rules — never violate

1. **Zero FDOT content** (GR-1/GR-2): no FDOT data, APIs, branding, or references anywhere. Florida data only from federal or county sources (e.g., Miami-Dade Transit).
2. **Full AI implementation, post-completion study** (AI-USAGE v1.2, amended 2026-07-17): AI implements everything — the [ME]/[AI] task split is removed. Merges are gated by green CI + AI self-review; Raziel studies the finished codebase to mastery after the build completes. PRs with meaningful AI code get the `ai-assisted` label. **Never AI's:** credentials, API keys, billing, account actions — secrets never in the repo or a prompt. *(v1.1 required pre-merge line review — superseded; the change log records the shift.)*
3. **Small, coherent diffs.** Prefer several small conventional commits/PRs over one large one — the PR history is the study trail. CI must be green before merge; PRs with meaningful AI-written code get the `ai-assisted` label.
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

**Phase 0 core merged to `main` 2026-07-17 (PRs #2, #3); real 2025 snapshot loaded; policy now AI-USAGE v1.2 (full AI build, Raziel studies post-completion).** The Weeks 2–4 pass was authored in Cowork under v1.1, verified and committed by Claude Code, line-reviewed and merged by Raziel the same day — the last pre-merge line review before v1.2 took effect. Also that day: SDLC formalized (`docs/SDLC.md`, SRS v1.1 with Phase 0 acceptance criteria + Phase 0.5 AI series FR-AI.1–3, `docs/TRACEABILITY.md` RTM, ADR-008) · `tools/build-tiles.sh` + root README + `SpanSight.Api.http` refresh (PR #3).

Delivered: Core domain (parser, DMS converter, validators, classifier, lookups, EF model + `InitialSchema` migration, BridgeFilter, AI port scaffold) · Ingestion CLI (idempotent unnest-upsert load, quarantine, run summaries, GeoJSON export) · API (bridges/geojson/detail/lookups/stats/qa/ai endpoints, ProblemDetails, rate limiting, CORS, health, OpenAPI+Scalar, OTel config-gated) · test suites (unit + Testcontainers integration) · fixture + generator · web SPA (explorer, drawer, QA page per mockup).

**Verified on the dev Mac 2026-07-17:** `dotnet build` 0 warnings · `dotnet format` clean · **106/106 tests green** including the Testcontainers integration suite against real PostGIS · `web/` lint + build clean · full-stack smoke pass (fixture load 114/99/15 + idempotent no-op rerun, map → filter → drawer deep link `/bridge/FL/…` → `/qa` reconciling exactly). Four small fixes landed during verification: GeoJSON typing in `BridgeMap`, root-anchored `/data/` gitignore (bare `data/` swallowed `src/**/Data/` on case-insensitive macOS), ingestion CLI content root, AbortError filter in `QaPage`.

Earlier done: docs set v1, repo public, Week 1 scaffold on `main`, Azure trial ($200/30d — upgrade to PAYG before it lapses). **Swiftly access granted 2026-07-17** (agencyKey `miami`, GTFS-rt trip updates + vehicle positions, 180 req/15 min) — risk R-4 fallback closed; key lives in Raziel's password manager, enters `.env`/GitHub secrets only when Phase 2 starts (NFR-8 §10 — never in the repo).

**Real 2025 snapshot loaded locally 2026-07-17:** 743,398 read · 741,131 loaded · 2,267 quarantined (0.30%) from `data/2025AllRecordsDelimitedAllStates.txt` (291 MB, gitignored).

Next (all AI per v1.2, except credential/billing steps which stay Raziel's): EXPLAIN/index pass at national scale (NFR-1) · national tile build + SPA-at-scale check · Playwright smoke + a11y scan in CI · deploy workflow (OIDC → Azure, Bicep, budget alert resource, Blob/SWA publish, `VITE_TILES_URL`) — Raziel does portal consent/PAYG upgrade + GitHub secrets · then the Phase 0.5 AI series (FR-AI.1 first; `Ai:Enabled` false until its gate; Anthropic key via .env/secrets only) · Raziel's post-completion code study begins at Phase 0 gate.
