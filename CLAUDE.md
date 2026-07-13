# SpanSight — AI Session Context

Portfolio project by Raziel Arias: a national bridge-inventory intelligence platform built on public FHWA NBI data (~624k bridges). Purpose: demonstrate senior-level C#/.NET + React + Azure engineering. This file is committed intentionally as a transparency artifact (see docs/AI-USAGE.md).

## Read before working

- `docs/REQUIREMENTS.md` — scope, ground rules (GR-1…GR-7), NFRs, phased FRs
- `docs/ARCHITECTURE.md` — ADRs; all-Azure topology (ADR-006-B)
- `docs/IMPLEMENTATION-PLAN.md` — current WBS with [ME]/[AI] task tags
- `docs/AI-USAGE.md` — the AI policy governing every session

## Hard rules — never violate

1. **Zero FDOT content** (GR-1/GR-2): no FDOT data, APIs, branding, or references anywhere. Florida data only from federal or county sources (e.g., Miami-Dade Transit).
2. **Respect the [ME] tags** in IMPLEMENTATION-PLAN §5. Do NOT implement: NBI parser core, DMS coordinate converter, schema design, first API endpoint, first React/MapLibre component, core test assertions. Provide interfaces + `// TODO(raziel)` stubs instead.
3. **Small, reviewable diffs.** Raziel reviews every line before merge; prefer several small commits/PRs over one large one. PRs with meaningful AI-written code get the `ai-assisted` label.
4. **No bulk NBI data in git** — test fixtures only (a few hundred rows max).
5. **Azure discipline:** never create/modify cloud resources except through `infra/` Bicep; monthly budget ≤ $50 (alert at $40).
6. **Not engineering advice:** UI must keep the disclaimer footer; app displays published data, never computes engineering judgments.

## Stack (pinned 2026-07)

.NET 10 LTS · ASP.NET Core + SignalR · EF Core + Npgsql + NetTopologySuite · React + TypeScript + Vite · MapLibre GL JS · PostgreSQL 16 + PostGIS · Redis (Streams) · Parquet + DuckDB (analytics, local) · tippecanoe → PMTiles · OpenTelemetry → App Insights (cloud) / Grafana (local) · Bicep · GitHub Actions (OIDC to Azure) · Node 24 LTS (GTFS-RT poller only, Phase 2).

## Conventions

Trunk-based; short-lived branches; conventional commits; PR for everything (template in `.github/`); CI must be green; `main` always deployable; xUnit + Testcontainers + one Playwright smoke; ADR in `docs/` for any surprising decision.

## Layout

`src/SpanSight.Core|Api|Ingestion` + `src/tests/` · `web/` (SPA) · `infra/` (Bicep) · `tools/` (tiles/DuckDB scripts) · `docs/` (SDLC set).

## Current status (keep this section updated at every phase gate)

**Phase 0, Week 1.** Done: docs set v1, repo public, Azure trial account ($200/30-day credit — upgrade to PAYG before it lapses), Swiftly API access requested (pending — fallback R-4 if delayed). Next: Week 1 [AI] scaffold per IMPLEMENTATION-PLAN §5.
