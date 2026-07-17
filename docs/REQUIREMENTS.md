# SpanSight — National Bridge Asset Intelligence Platform

**Software Requirements Specification** · v1.1 · Author: Raziel Arias · Date: 2026-07-17 · Status: For review — baselines on merge to `main`

---

## 1. Overview

SpanSight (working title) is a portfolio capstone: a production-grade web platform that ingests the FHWA National Bridge Inventory (~624,000 bridges), serves it through a spatial API and interactive map, computes condition and deterioration analytics from 30+ years of history, and demonstrates real-time streaming with transit vehicle positions.

**Primary purpose:** demonstrate senior-level engineering across the C#/.NET + React stack for active job applications.
**Secondary purpose:** architecture leaves the door open to future commercialization (condition dashboards for small agencies and consultancies) without committing to it now.

### 1.1 Document conventions

Requirements follow the process defined in [SDLC.md](./SDLC.md) (rolling-wave elaboration, PR-based change control §4, stable IDs).

| Convention | Rule |
|---|---|
| ID scheme | `GR-n` ground rules · `G-n` goals · `P-n` personas · `UC-p.n` use cases · `FR-p.n` functional requirements (p = phase; the AI-assist series added 2026-07-17 uses `FR-AI.n`, Phase 0.5) · `NFR-n` non-functional · `R-n` risks · `OQ-n` open questions · `AC-n` acceptance criteria (scoped to their parent FR) |
| ID stability | IDs are never renumbered or reused; a dropped requirement is marked *Withdrawn* in place |
| Priority | MoSCoW, judged within the requirement's phase (a Phase-2 *Must* still waits for Phase 2) |
| Verification | **Test** (automated) · **Demo** (scripted manual walkthrough) · **Inspection** (review of artifact/config) · **Analysis** (measurement/benchmark) |
| Elaboration | Phase 0 requirements carry full acceptance criteria now; Phases 1–3 are scoped stubs, elaborated at the gate that opens them ([SDLC.md §3](./SDLC.md)) |
| Traceability | Every FR/NFR appears in [TRACEABILITY.md](./TRACEABILITY.md) mapping to design, implementation, and verification evidence |

### 1.2 References

[SDLC.md](./SDLC.md) · [ARCHITECTURE.md](./ARCHITECTURE.md) · [DESIGN.md](./DESIGN.md) · [IMPLEMENTATION-PLAN.md](./IMPLEMENTATION-PLAN.md) · [TRACEABILITY.md](./TRACEABILITY.md) · [AI-USAGE.md](./AI-USAGE.md) · [HOSTING-ANALYSIS.md](./HOSTING-ANALYSIS.md)

## 2. Ground Rules — Employer Conflict Avoidance (non-negotiable)

| ID | Rule |
|----|------|
| GR-1 | **Zero FDOT content.** No FDOT data, APIs, branding, logos, documents, code, or references anywhere in the product, repo, or demo. |
| GR-2 | Florida data only from **non-FDOT public sources** (federal datasets; Miami-Dade County transit feeds). |
| GR-3 | Built exclusively on **personal time, equipment, and accounts**. |
| GR-4 | **No FDOT work product**: no code, configs, schemas, documents, or non-public knowledge acquired through employment. |
| GR-5 | **Portfolio-only while employed at FDOT.** No sales, contracts, or paid pilots without prior written FDOT ethics / outside-activity clearance (Fla. Stat. ch. 112 considerations). |
| GR-6 | In-app disclaimer: independent personal project; public federal/county data; **not engineering advice**; no affiliation with or endorsement by any DOT. |
| GR-7 | FDOT experience may appear in resume/interviews as work history only — never presented as the origin of this product. |

GR checklist is reviewed at every phase gate (see Risks, R-6).

## 3. Goals & Success Metrics

- **G-1** Live public demo a hiring manager can open in <5 seconds and understand in <2 minutes.
- **G-2** A repo that reads like production: README, ADRs, tests, CI/CD, observability, this docs set.
- **G-3** Cover target-role talking points: .NET API design, EF Core + PostGIS, React + TypeScript, Redis + outbox pattern, OpenTelemetry, vector tiles, CI/CD, cloud cost discipline.
- **G-4** A rehearsed 10-minute demo walkthrough per completed phase.
- **G-5** *(added 2026-07-17)* Demonstrate pragmatic AI-feature engineering: schema-constrained LLM outputs, prompt-injection posture, cost governors, provider abstraction (ADR-008).

**Metrics:** demo available throughout application season · p95 API latency target met (NFR-1) · ≥70% test coverage on core services · monthly cloud spend within the $50 budget ($40 alert) — NFR-2 as amended 2026-07-12.

## 4. Personas

- **P-1 Hiring manager / interviewer (primary).** Skims demo and repo in minutes; needs immediate signal of engineering quality.
- **P-2 County / consultancy asset engineer (product-realism persona).** Filters bridges by condition, pulls a county report; represents the future-monetization user.
- **P-3 Operator (Raziel).** One-command local spin-up (`docker compose up`); cheap, boring deployments.

### 4.1 Use cases (Phase 0)

Main success scenarios only; alternate/error flows are captured as acceptance criteria on the FRs each use case exercises.

| ID | Actor | Main flow → success end state | Exercises |
|---|---|---|---|
| UC-0.1 | P-1, P-2 | Open demo URL → national map renders with condition-colored bridges + KPI strip → pan/zoom smoothly. *Success:* oriented in under 2 minutes (G-1). | FR-0.4, FR-0.5 |
| UC-0.2 | P-2 | Set filters (state, county, condition, type/material, year) → map, KPIs, and result set recompute together from one predicate. *Success:* on-screen cohort matches the filter definition. | FR-0.3, FR-0.4 |
| UC-0.3 | P-1, P-2 | Click a bridge → detail drawer opens with decoded (human-readable) NBI values → deep-link `/bridge/{id}` reproduces the view. *Success:* no raw codes shown undecoded. | FR-0.3, FR-0.4 |
| UC-0.4 | P-1 | Open Swagger UI → browse endpoints/schemas → run a filtered query with pagination. *Success:* self-service API exploration without reading source. | FR-0.3 |
| UC-0.5 | P-3 | Run ingestion CLI against an annual NBI snapshot → parse, validate, quarantine rejects, upsert core, rebuild tiles → run summary logged. *Success:* re-run is a safe no-op (idempotent). | FR-0.1, FR-0.2, FR-0.5 |
| UC-0.6 | P-2, P-3 | Open Data QA report → totals, reject rates by reason and state reconcile with the latest run summary. *Success:* data trustworthiness is inspectable. | FR-0.2 |

## 5. Functional Requirements (phased)

Phase 0 requirements are fully elaborated (priority · verification · acceptance criteria). Phases 1–3 are **scoped stubs** — one-line commitments elaborated at the gate that opens each phase (SDLC.md §3); their IDs are stable now.

### Phase 0 — Foundation & Map Explorer (MVP)

**FR-0.1 — NBI snapshot ingestion** · Must · Verify: Test + Inspection
Ingest the latest NBI national snapshot (delimited files) into PostgreSQL/PostGIS via an idempotent .NET ingestion pipeline; re-runnable, logged, unit-tested parsing.

- AC-1 Running the pipeline on a clean database loads all parseable 2025-snapshot records into `staging_legacy` and `core`, and persists a run summary (rows read / loaded / quarantined, duration).
- AC-2 Re-running the same snapshot leaves `core` row counts and content unchanged (idempotent upsert), logged as a refresh, not duplicated.
- AC-3 A run interrupted mid-load completes safely on re-run without manual cleanup (resumable — NFR-3).
- AC-4 Parser and validators carry unit tests with malformed-row fixtures; coverage on this code ≥70% (NFR-5). No bulk NBI data in git — fixtures only (a few hundred rows).

**FR-0.2 — Data-quality pipeline** · Must · Verify: Test + Demo
Parse DMS-encoded coordinates; detect and flag invalid/zero coordinates, out-of-state locations, impossible values; quarantine table + QA report page.

- AC-1 DMS-encoded lat/long convert to WGS84 decimal degrees; converter unit-tested against known fixture pairs, including malformed input.
- AC-2 Rows failing validation (invalid/zero coordinates, geolocation outside the coded state, impossible values such as future year built) land in `quarantine` with machine-readable reason codes — never in `core`.
- AC-3 QA report page (UC-0.6) shows totals and reject rate by reason code and by state; figures reconcile exactly with the run summary (FR-0.1 AC-1).
- AC-4 Ingestion emits row-count and error-rate metrics to observability (NFR-6).

**FR-0.3 — Bridges REST API** · Must · Verify: Test + Analysis
REST API (ASP.NET Core): query bridges by state, county, condition ratings, structure type/material, year built, bounding box; pagination; OpenAPI/Swagger UI.

- AC-1 `GET /api/bridges` supports state, county, condition rating(s), structure type/material, year-built range, and bounding-box filters — combinable in one request — plus pagination with a documented page-size cap.
- AC-2 A bridge-detail endpoint returns one structure with raw codes decoded to human-readable values (feeds UC-0.3).
- AC-3 OpenAPI document + Swagger UI published; every endpoint, parameter, and schema described (UC-0.4).
- AC-4 Invalid parameters return RFC 9457 ProblemDetails with field-level detail; unknown routes/IDs return proper 404s.
- AC-5 Rate limiting, strict CORS, security headers, and health endpoints active (ARCHITECTURE §7); p95 latency per NFR-1 verified by the Week-3 index/EXPLAIN pass.

**FR-0.4 — Map Explorer front end** · Must · Verify: Test + Demo
React + TypeScript front end with MapLibre GL: national map with clustering/vector tiles, filter panel, bridge detail panel (raw NBI codes decoded to human-readable values).

- AC-1 Layout matches [DESIGN.md](./DESIGN.md): header, filter rail, KPI strip, legend, detail drawer, always-visible disclaimer footer (GR-6) with basemap attribution (NFR-8).
- AC-2 Filters apply instantly; KPIs, map layer, and result set recompute from one shared predicate (UC-0.2) — no Apply button.
- AC-3 Detail drawer opens on point click, closes on Esc/×, shows decoded values, and is deep-linkable at `/bridge/{id}` (UC-0.3).
- AC-4 Keyboard navigation works across filter controls and drawer; UI chrome meets WCAG 2.1 AA contrast (NFR-7).
- AC-5 Defined states implemented: map loading skeleton, zero-results empty state, API-error toast, basemap-offline fallback (DESIGN interaction notes).
- AC-6 Playwright smoke path (load map → filter → open bridge) runs in CI (FR-0.6 AC-3).

**FR-0.5 — Static vector tiles** · Must · Verify: Test + Analysis
Static vector tiles (tippecanoe → PMTiles) regenerated by the pipeline; served from static hosting/CDN, not per-request rendering.

- AC-1 A pipeline step exports `core` to GeoJSON and builds a single PMTiles artifact via tippecanoe (`tools/` script, reproducible from a clean checkout).
- AC-2 Tiles are served as a static asset over HTTP range requests (Blob/CDN) — no tile server, no per-request rendering (ADR-002).
- AC-3 National-scale map interaction stays smooth on a mid-range laptop (NFR-1); tile artifact carries a version/manifest tied to the ingestion run that produced it.

**FR-0.6 — Local stack, CI, public demo** · Must · Verify: Test + Demo
Docker Compose local stack; GitHub Actions CI (build, test, lint); deployed public demo.

- AC-1 `docker compose up` brings up the full local stack (Postgres+PostGIS, Redis, API, OTel collector, optional Grafana profile) green on a clean machine.
- AC-2 CI runs on every PR: build, tests, lint/format, dependency + container scan; a red check blocks merge (`main` stays deployable).
- AC-3 Deploy workflow: GitHub OIDC → Azure, Bicep provisioning, EF migration step, image rollout, SPA + tile publish, then the smoke test (FR-0.4 AC-6) — all green before the release is considered live.
- AC-4 Public demo URL reachable; initial map view interactive in <5 s on a typical connection (G-1).

### Phase 0.5 — AI Assist *(added 2026-07-17 via change control; ADR-008)*

Builds only after the Phase 0 core demo runs with real data; `Ai:Enabled` stays `false` until each FR's acceptance criteria are elaborated and met. Standing guardrail for the whole series: AI output is **translation or description of published data only — never engineering judgment** (GR-6); every AI-authored string carries the disclaimer; user text is data, not instructions (ADR-008 §2–3); LLM spend lives inside NFR-2 (target ≤$5/mo).

- **FR-AI.1 — Natural-language query** · Should · Ask-the-map box translates a plain-English request ("poor-condition steel bridges near Miami built before 1970") into the **existing validated filter predicate** via schema-constrained LLM output; the result is indistinguishable from having set the filters by hand, and the interpreted filter is shown for correction. *(ACs elaborated at build gate.)*
- **FR-AI.2 — Record narration** · Could · Drawer action that renders a bridge's already-displayed published values as a plain-English paragraph (template-framed, cached, disclaimer attached). *(ACs at build gate.)*
- **FR-AI.3 — Coding-guide RAG** · Could · "What does Item 60 mean?" answered from the public FHWA Coding Guide/SNBI definitions with citations, retrieval via pgvector in the serving Postgres. *(ACs + embedding-provider trade study at build gate.)*

### Phase 1 — Historical Analytics

- **FR-1.1** Ingest historical NBI files (1992–2025) into Parquet; DuckDB analytical layer. Full history stays out of the hosted Postgres to keep the serving DB small and cheap (NFR-2, ADR-005).
- **FR-1.2** Condition trend views per bridge / county / state (Good/Fair/Poor over time).
- **FR-1.3** Deterioration model: condition-rating transition probabilities (Markov-chain baseline) by structure type, material, and climate region; methodology and limitations documented.
- **FR-1.4** Rankings and report cards: worst condition, high-traffic + poor-condition, county report card; CSV/PDF export.
- **FR-1.5** Census TIGER/ACS enrichment: boundaries, population served.

### Phase 2 — Real-Time & Operations

- **FR-2.1** GTFS-Realtime ingestion service polling Miami-Dade Transit feeds (Swiftly API) into Redis Streams. Node/TypeScript acceptable here (secondary stack) — justify in an ADR.
- **FR-2.2** Transactional outbox pattern between ingestion store and event consumers; failure modes tested and documented.
- **FR-2.3** Live map layer: vehicle positions over the bridge condition layer; geofence event when a route crosses a Poor-condition bridge (demo scenario).
- **FR-2.4** OpenTelemetry traces/metrics/logs end to end (API, ingestion, front end); Grafana dashboard; basic SLOs and alerts.
- **FR-2.5** Weather overlay: NOAA api.weather.gov alerts for the visible map area (no API key required).
- **FR-2.6** End-user terms/disclaimer page (footer link) satisfying Swiftly API License §6 (NFR-8); ships with, and gates, the public release of the live map (FR-2.3).

### Phase 3 — Stretch / Monetization-Ready

- **FR-3.1** Auth (OIDC) + saved views/workspaces — multi-tenant-shaped, disabled by default.
- **FR-3.2** Hurricane exposure scoring: HURDAT2 historical tracks vs. bridge locations.
- **FR-3.3** ArcGIS interoperability: consume a public **non-FDOT** ArcGIS FeatureServer (another state DOT or federal service) to demonstrate Esri REST integration.
- **FR-3.4** Scheduled annual data refresh + "what changed in this year's NBI" changelog page.

## 6. Non-Functional Requirements

Each NFR carries priority and verification method (§1.1); evidence links live in the RTM.

- **NFR-1 Performance:** p95 < 300 ms for filtered API queries at national scale; smooth map interaction on a mid-range laptop (tiles pre-generated, never rendered per request). · *Must · Verify: Analysis — Week-3 index/EXPLAIN pass locally, load-test script pre-application-season, App Insights p95 panel in `demo`.*
- **NFR-2 Cost (amended 2026-07-12):** budget ≤ $50/mo during initial phases, Azure budget alert at $40. All-Azure topology per ADR-006-B (expected ~$18–21 Phases 0–1, ~$30–40 Phase 2). Hosted DB (PostgreSQL Flexible B1ms, 32 GiB) carries serving data + aggregates; full 30-yr history lives in Parquet + DuckDB on the dev Mac, archived to Blob. *(Original: ~$0 target / $20 cap on multi-vendor free tiers — see HOSTING-ANALYSIS.md.)* AI-feature LLM spend counts inside this budget (target ≤$5/mo, daily budget guard — ADR-008). · *Must · Verify: Inspection — alert armed before first resource; spend reviewed at every gate (SDLC §3.6).*
- **NFR-3 Reliability:** demo is best-effort but self-healing (restart policies); ingestion idempotent and resumable. · *Should · Verify: Test — kill-and-rerun ingestion test (FR-0.1 AC-3); restart policies in compose/Container Apps config.*
- **NFR-4 Security:** no PII anywhere in the system; secrets via environment/GitHub secrets; OWASP Top-10 basics; dependency scanning in CI. · *Must · Verify: Inspection + Test — public-asset-data-only sources (§7); CI dependency/container scans green; GitHub secret-push protection on; rate-limit/CORS/headers checks (FR-0.3 AC-5).*
- **NFR-5 Code quality:** ≥70% coverage on ingestion/API core; ADRs for major decisions; conventional commits. · *Should · Verify: Test + Inspection — coverage report in CI; ADR presence checked in PR review (DoD, IMPLEMENTATION-PLAN §7).*
- **NFR-6 Observability:** every request traceable end to end; ingestion emits row-count and error-rate metrics. · *Should · Verify: Demo — one W3C trace browser → API → DB visible in App Insights (Week-5 exit); ingestion metrics on dashboard.*
- **NFR-7 Accessibility:** keyboard-navigable UI; WCAG 2.1 AA contrast on UI chrome (map canvas exempt where impractical). · *Should · Verify: Test + Inspection — automated a11y scan on chrome components; keyboard walkthrough in the demo script.*
- **NFR-8 Licensing hygiene:** public-domain federal data; OSM-derived basemap used with required attribution; third-party licenses documented in the repo. Swiftly API License (accepted at key request, rev. 2025-09-22) additionally requires: end-user terms page mirroring its restrictions and liability disclaimers before the Phase 2 live map goes public (§6); no raw GTFS-RT dumps in the public repo (§4.vi); API key in secrets only (§10); delete cached Swiftly-derived data on termination (§14). · *Must · Verify: Inspection — attribution in UI footer (FR-0.4 AC-1); licenses documented; FR-2.6 gates the live map.*

## 7. Data Sources (verified 2026-07-12)

| Source | Content | Access | License | Notes |
|---|---|---|---|---|
| FHWA NBI (legacy) | ~624k bridges, annual files 1992–2025 | [fhwa.dot.gov/bridge/nbi/ascii.cfm](https://www.fhwa.dot.gov/bridge/nbi/ascii.cfm); geospatial copy via [BTS NTAD](https://geodata.bts.gov/datasets/national-bridge-inventory/about) | Public domain (US Gov) | 2025 file is the final Coding Guide-era dataset |
| FHWA NBI NextGen (SNBI) | 2026+ submittals | NBI NextGen portal (live Jan 2026) | Public domain | First hybrid SNBI submittal Mar 15, 2026; 100% verified SNBI target Mar 2028; FHWA publishes a [data crosswalk](https://www.fhwa.dot.gov/bridge/snbi/datacrosswalk.cfm) |
| Census TIGER/Line + ACS | Boundaries, demographics | Free downloads/API | Public domain | Enrichment (Phase 1) |
| NOAA api.weather.gov | Alerts, forecasts | Free, no key | Public domain | Phase 2 |
| NOAA HURDAT2 | Historical hurricane tracks | Flat file | Public domain | Phase 3 |
| Miami-Dade Transit GTFS / GTFS-RT | Schedules + live vehicle positions | Schedule zip public; RT via Swiftly API ([request access](https://www.miamidade.gov/global/transportation/open-data-feeds.page)) | Free | County agency — non-FDOT (GR-2). Fallback: another agency's open GTFS-RT if access lags |
| Basemap tiles | Vector basemap | OpenFreeMap / Protomaps builds | ODbL-derived, attribution required | Attribution in UI footer (NFR-8) |

**Design consequence:** the schema must support both legacy Coding Guide records (1992–2025) and SNBI records (2026+) behind one adapter/crosswalk layer. This is a deliberate showcase feature.

## 8. Technology Stack

- **Primary (target roles):** C#/.NET — ASP.NET Core Web API, Worker Services, EF Core + Npgsql + NetTopologySuite. Front end: React + TypeScript + MapLibre GL JS.
- **Secondary, where it earns its place:** Node/TypeScript (e.g., GTFS-RT poller, tile tooling scripts). Each use justified in an ADR.
- **Data:** PostgreSQL 16 + PostGIS (serving) · Parquet + DuckDB (history/analytics) · Redis (streams, cache).
- **Platform:** Docker Compose on macOS (dev) · GitHub Actions → Azure via OIDC (CI/CD) · Azure Static Web Apps + Blob (front end + PMTiles) · Azure Container Apps (API) + Azure Database for PostgreSQL Flexible Server · Bicep IaC (ADR-006-B).
- **Observability:** OpenTelemetry SDKs → Application Insights in cloud; Prometheus/Grafana in local compose (ADR-006-B).
- **Tiles:** tippecanoe → PMTiles.

## 9. Roadmap (~26 weeks, part-time)

| Weeks | Milestone |
|---|---|
| 1–2 | Repo, CI, compose stack, NBI download + schema study |
| 3–6 | **Phase 0 complete — public demo live** |
| 6–8 | Phase 0.5 — AI assist (FR-AI.1 first; overlaps Phase 1 start) |
| 7–12 | Phase 1 (historical analytics) |
| 13–18 | Phase 2 (real-time + observability) |
| 19–22 | Phase 3 picks (choose 1–2 stretch items) |
| 23–26 | Polish: docs, demo script, load tests, blog write-up |

Every phase ends demoable. Trunk stays deployable — job-application demos never depend on in-progress work.

## 10. Risks & Mitigations

| ID | Risk | Likelihood/Impact | Mitigation |
|---|---|---|---|
| R-1 | NBI coordinate/data quality | Certain / Medium | Quarantine + QA metrics (FR-0.2). Treat as a showcase feature, not just a fix |
| R-2 | SNBI schema shift 2026+ | Medium / Medium | Versioned schemas + FHWA crosswalk adapter (§7) |
| R-3 | Free-tier changes or DB size limits | Medium / Low | History in Parquet; only serving data hosted; monthly cost check |
| R-4 | Swiftly API access delay | Medium / Low | Fallback open GTFS-RT feed from another agency |
| R-5 | Scope creep | High / High | Phase gates; nothing starts until prior phase's exit criteria met; mid-phase additions require an explicit trade (SDLC §4) |
| R-6 | Employer-conflict drift | Low / Critical | GR checklist (§2) reviewed at every phase gate; no Florida state data sources, ever |
| R-7 | Time vs. job search | Medium / Medium | Phase 0 alone is a complete portfolio piece |

## 11. Out of Scope (v1)

Mobile apps · inspection workflow or data entry · load-rating or any engineering calculations (display-only decoding of published values) · anything FDOT · user-uploaded data · SLA-backed hosting.

## 12. Open Questions

- **OQ-1** ~~Project name~~ **Resolved 2026-07-12:** SpanSight, locked (repo `spansight`, public) — IMPLEMENTATION-PLAN.md.
- **OQ-2** ~~Container host~~ **Resolved:** Azure end-to-end — [ARCHITECTURE.md](./ARCHITECTURE.md) ADR-006-B (adopted 2026-07-12).
- **OQ-3** ~~Observability stack~~ **Resolved:** Application Insights (cloud) + self-hosted Grafana in local compose — ADR-006-B.
- **OQ-4** Phase 3 auth provider: Entra ID free tier vs. Auth0 vs. Keycloak.

## 13. Change Log

| Version | Date | Change |
|---|---|---|
| v0.1 | 2026-07-12 | Initial draft: scope, ground rules, goals, personas, phased FR/NFR set, data sources, roadmap, risks. |
| v1.0 | 2026-07-17 | SRS baseline candidate: document conventions + references (§1.1–1.2); Phase 0 use cases (§4.1); priority, verification method, and acceptance criteria for all Phase 0 FRs (§5) and NFRs (§6); change control per SDLC.md; R-5 mitigation extended. No requirement renumbered; no scope change. |
| v1.1 | 2026-07-17 | **Scope addition via change control:** Phase 0.5 AI-assist series FR-AI.1–3 (+ G-5, roadmap row, NFR-2 LLM-spend note) per ADR-008. Gated behind Phase 0 core demo; guardrails: GR-6 translation/description-only, schema-constrained outputs, cost caps. |

---

## Appendix A — SNBI Transition Notes (verified July 2026)

- Legacy Coding Guide sunset **Dec 31, 2025**; the 2025 national file is the last Coding Guide-era dataset.
- **NBI NextGen** portal live **Jan 1, 2026** (reports, queries, mapping, downloads).
- First (hybrid) SNBI submittal due **Mar 15, 2026**; target for 100% populated and verified SNBI data: **Mar 2028**.
- FHWA publishes a Coding Guide ↔ SNBI **data crosswalk** — the basis for this project's adapter layer.

Sources: [FHWA NBI](https://www.fhwa.dot.gov/bridge/nbi.cfm) · [FHWA SNBI](https://www.fhwa.dot.gov/bridge/snbi.cfm) · [SNBI data crosswalk](https://www.fhwa.dot.gov/bridge/snbi/datacrosswalk.cfm) · [NBI ASCII downloads](https://www.fhwa.dot.gov/bridge/nbi/ascii.cfm) · [BTS NTAD NBI](https://geodata.bts.gov/datasets/national-bridge-inventory/about) · [Miami-Dade open data feeds](https://www.miamidade.gov/global/transportation/open-data-feeds.page)
