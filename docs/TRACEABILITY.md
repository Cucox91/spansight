# SpanSight — Requirements Traceability Matrix

**RTM** · v1.1 · Date: 2026-07-24 (v1.0: 2026-07-17) · Status: Baselined at gate 0 · Companion to [REQUIREMENTS.md](./REQUIREMENTS.md) (single source of requirement text — this file carries links, not copies)

**Maintenance rule (SDLC.md §§3–4):** update the row when a PR closes or changes a requirement; audited at every phase gate — a phase cannot close with an orphan (no design/implementation) or unverified (no evidence) in-phase requirement.

**Status values:** Planned · In progress · In review · Done · Deferred · Withdrawn.

> **2026-07-17:** Weeks 2–4 implementation pass delivered (AI-USAGE v1.1), line-reviewed and merged to `main` as PRs #2/#3. **2026-07-18 (AI-USAGE v1.2):** statuses now move to *Done* when the implementing PR merges with green CI; Raziel's mastery is verified in the post-completion code study. Real 2025 snapshot loaded locally: 743,398 read · 741,131 loaded · 2,267 quarantined (0.30%), run #1.
>
> **2026-07-24 — Phase 0 gate audit.** FR-0.5/FR-0.6 closed with the demo live at `https://www.spansights.com` (+ apex); NFR rows brought to their Phase 0 verdicts. Two waivers recorded (container-scan CI job, coverage report in CI) with carry-over tasks in the Phase 1 WBS — gate note in [IMPLEMENTATION-PLAN.md §9](./IMPLEMENTATION-PLAN.md). Phase 1 rows elaborated per SRS v1.3.
>
> **2026-07-25 — NFR-5 coverage waiver discharged (P1-W1).** CI now measures line coverage and fails the build below 70% on ingestion/API core (PR #22). The gate-0 waiver is closed; the container-scan waiver (FR-0.6 AC-2) stays open as the remaining P1-W1 carry-over. Row closed in the delivering PR per the maintenance rule (gate-0 retro lesson 3).

---

## 1. Phase 0 — functional requirements

| Req | Design | Implementation (planned → actual) | Verification | Status |
|---|---|---|---|---|
| FR-0.1 | [ARCH §4.2](./ARCHITECTURE.md) batch pipeline · ADR-005 | `SpanSight.Ingestion` `LoadPipeline` (unnest upsert, SHA-256 idempotency, run summaries) + `SpanSight.Core/Ingestion` parser — merged PR #2 | `Ingestion.Tests` dry-run exact-split; integration: reload no-op + `--force` convergence vs real PostGIS ✓; real 2025 snapshot: 743,398 → 741,131 loaded (2026-07-18) ✓; cloud load run #2 reconciles exactly with local (2026-07-19) ✓ | Done |
| FR-0.2 | [ARCH §4.1](./ARCHITECTURE.md) staging/quarantine model | `NbiDmsCoordinateConverter` + `BridgeRowValidator` (10 reason codes) + `/api/qa/summary` + `web` QA page — merged PR #2 | `Core.Tests` converter/validator fixtures ✓; integration: QA reconciles with run summary + core count ✓; real data: 2,267 quarantined (0.30%), QA page reconciles ✓ | Done |
| FR-0.3 | [ARCH §3](./ARCHITECTURE.md) containers · §7 cross-cutting | `SpanSight.Api` endpoints (bridges/geojson/detail/lookups/stats), ProblemDetails, rate limiting, CORS, health, OpenAPI+Scalar — merged PR #2 | `BridgeQueryBuilderTests` ✓; Testcontainers: filters/bbox-GIST/pagination/404/400 ✓; EXPLAIN/index pass at 741k rows 2026-07-18: GIST bbox 0.9 ms, paged filter 1.6 ms, national group-by 31 ms; warm API 3–52 ms end-to-end ✓ | Done |
| FR-0.4 | [DESIGN.md](./DESIGN.md) + [mockup](./design/mockup.html) → React mapping | `web/src` — `AppShell`, `FilterRail`, `KpiStrip`, `BridgeMap`, `BridgeDrawer`, `QaPage`, shared `FilterState` predicate, `/bridge/{state}/{id}` deep link — merged PR #2 | lint + `tsc`/vite build clean ✓; full-stack smoke (fixture + national data) ✓; Playwright smoke (AC-6) + axe scan in CI ✓ (PR #5); full live e2e + axe green vs the deployed demo (2026-07-19) ✓ | Done |
| FR-0.5 | ADR-002 static PMTiles | `GeoJsonExporter` + `tools/build-tiles.sh` (tippecanoe → PMTiles + run-linked manifest) — merged PR #3 · Blob publish + storage CORS by Bicep (PR #13) | Script clean from checkout: fixture 99 → pmtiles ✓; national: 741,131 features → 22.4 MB `bridges.pmtiles` + manifest tied to run #1 ✓; published to Blob, `VITE_TILES_URL` set, SPA in tiles mode live ✓; range-request CORS verified from both custom-domain origins (2026-07-24) ✓; laptop interaction check live ✓; two tiles-mode-only bugs found live and fixed (PRs #15/#16 — CI e2e runs fallback mode, gap logged in gate note) | **Done** (2026-07-24) |
| FR-0.6 | [ARCH §7](./ARCHITECTURE.md) CI/CD · §5 topology | `docker-compose.yml` ✓ · `ci.yml` (4 jobs incl. e2e) ✓ · `infra/` Bicep incl. budget + container-app + custom-domain modules ✓ · API `Dockerfile` ✓ · `deploy.yml` (OIDC → Bicep → Entra-token migration → GHCR rollout → SWA publish → smoke) ✓ · deploy-on-main (PR #14) ✓ · RUNBOOK §1 one-time setup executed 2026-07-19 | CI green on every PR ✓; nine Deploy runs to first green (RUNBOOK §7) then demo live 2026-07-19 ✓; full live e2e green ✓; custom domains `www.spansights.com` + apex serving with certs, API + storage CORS verified (2026-07-24) ✓. **AC-2 waiver:** container-scan CI job deferred to P1-W1 (NuGet audit already fails the build on known-vulnerable packages — NU1903 + TreatWarningsAsErrors); recorded in gate note | **Done** (2026-07-24, AC-2 waiver logged) |

## 2. Non-functional requirements (Phase 0 scope)

| Req | Design | Implementation | Verification | Status |
|---|---|---|---|---|
| NFR-1 | ADR-002 (tiles off API) · indexed `core` | GIST on `location` + btrees on every filter column (`InitialSchema`); pagination caps | EXPLAIN pass at 741k rows 2026-07-18 ✓ (0.9–31 ms hot shapes, warm API 3–52 ms local); live demo interaction smooth in tiles mode ✓; load-test script remains scheduled pre-application-season; App Insights p95 panel reviewed at gates | Done (P0 scope) |
| NFR-2 | ADR-006-B topology · [HOSTING-ANALYSIS](./HOSTING-ANALYSIS.md) | `infra/modules/budget.bicep` — $50 cap, $40 actual + forecast alerts, deploys with every run ✓ | Alert armed (Bicep, verified in deploy runs) ✓; spend figure recorded by Raziel in each gate note (gate 0: see IMPLEMENTATION-PLAN §9) | Done (P0 gate; re-checked every gate) |
| NFR-3 | ARCH §5 restart policies | Idempotent upsert + resumable runs; ACA liveness/readiness probes + restart in `container-app.bicep` | Integration: reload no-op + `--force` convergence ✓; real kill-and-rerun evidence: 2026-07-19 national cloud load died mid-run at ~250k rows (WAN timeout), rerun converged with counts reconciling exactly (RUNBOOK §3 note) ✓; probes live ✓ | Done (P0 scope) |
| NFR-4 | ARCH §7 security | Rate limiter, strict CORS, security headers ✓; NuGet audit fails build on known-vulnerable packages ✓; GitHub secret-push protection (public repo) | Testcontainers + endpoint tests cover 400/404/429 paths ✓; CORS verified live from all three origins ✓; no-PII source review (§7 SRS) ✓; **container-scan job deferred to P1-W1 (waiver, see FR-0.6)** | Done (P0 scope, waiver logged) |
| NFR-5 | AI-USAGE merge bar · DoD | ADR practice ✓; conventional commits + PR discipline ✓; coverage gate in `ci.yml` — `coverlet.runsettings` (measurement scope: EF migrations + generated code excluded) + `tools/coverage-gate.py` (merges the per-test-project Cobertura reports, gates the scoped number) — merged PR #22 | CI measures line coverage on every run and fails below **70%** scoped to what NFR-5 names — Core parsing/validation/DMS-conversion/classification · the ingestion pipeline · the API querying path; the repo-wide number prints but does not gate. First green run [30185355956](https://github.com/Cucox91/spansight/actions/runs/30185355956) (2026-07-25): **scoped 85.7% (776/906 lines)**, repo-wide 81.4% ✓, 116/116 tests incl. Testcontainers ✓. Per-assembly + per-file tables in the step summary; Cobertura XML uploaded as an artifact. Fail path exercised locally before merge (degraded suite → 51.7%, exit 1). **Gate-0 waiver discharged.** | **Done** (2026-07-25) |
| NFR-6 | ARCH §7 observability · ADR-006-B | OTel config-gated in API: OTLP (local collector) / Azure Monitor distro (`APPLICATIONINSIGHTS_CONNECTION_STRING`, set by Bicep) ✓; Npgsql instrumentation on both paths ✓ | API → DB traces flowing to App Insights in `demo`; [RAZIEL] spot-check of one request trace at gate sign-off (gate note); browser-leg instrumentation + dashboards/SLOs are Phase 2 scope (FR-2.4) | Done (P0 scope: API→DB; full e2e tracing = FR-2.4) |
| NFR-7 | [DESIGN.md](./DESIGN.md) a11y section | Native inputs, focus ring, aria-live KPIs ✓; QA report region keyboard-focusable (axe finding, fixed PR #5) ✓ | axe scan in CI failing on serious/critical (map canvas exempt) ✓, green vs live demo ✓; keyboard walkthrough ships inside the demo script — carry-over task P1-W1 (gate note) | Done (P0 scope; walkthrough → demo script P1-W1) |
| NFR-8 | SRS §7 licensing column | Footer attribution + GR-6 disclaimer live ✓; licenses documented in README (License & data) ✓ | Inspection: footer live on both hostnames ✓; README section ✓; Swiftly obligations (terms page, key handling, cache deletion) remain Phase 2-gated — FR-2.6 gates the live map | Done (P0 scope) |

## 3. Ground rules (standing, all phases)

| Req | Where enforced | Verification | Status |
|---|---|---|---|
| GR-1/GR-2 | [ARCH §2](./ARCHITECTURE.md): externals limited to FHWA/BTS/Census/NOAA/Miami-Dade | Gate 0 checklist pass 2026-07-24 (repo/data-source inspection — IMPLEMENTATION-PLAN §9) | Continuously verified |
| GR-3/GR-4 | Personal accounts + hardware only; [AI-USAGE](./AI-USAGE.md) employer-hygiene section | Gate 0 checklist pass 2026-07-24 | Continuously verified |
| GR-5 | Portfolio-only posture; no billing/sales surface | Gate 0 checklist pass 2026-07-24 | Continuously verified |
| GR-6 | Disclaimer footer (FR-0.4 AC-1); not-engineering-advice scope (SRS §11) | Footer + disclaimer live on `www.spansights.com` and apex ✓; extends to AI-authored strings (ADR-008) and Phase 1 methodology framing (SRS v1.3 FR-1.3) | Continuously verified |
| GR-7 | Resume/interview conduct — outside the product | Self-check at gates ([RAZIEL]) | Continuously verified |

## 4. Phase 1 — historical analytics (elaborated at gate 0, SRS v1.3)

| Req | Design | Implementation (planned) | Verification sketch | Status |
|---|---|---|---|---|
| FR-1.1 | ADR-005 Parquet + DuckDB · ARCH §4.2 | `tools/` vintage pipeline: NBI ASCII 1992–2025 → normalized Parquet + catalog manifest (SHA-256, row counts); Blob cool-tier archive | Per-vintage row-count reconciliation vs FHWA files; rejects itemized; fixture-vintage tests; reproducible from clean checkout | Planned (P1-W1–W2) |
| FR-1.2 | ARCH §4.1 `condition_snapshot` · ADR-005 aggregates | DuckDB trend computation → aggregate tables in PG (bridge series; county/state × year rollups); API endpoints; drawer sparkline + trends view | Golden tests vs ≥3 hand-checked bridges; rollup/per-bridge reconciliation; EXPLAIN pass on new tables (NFR-1) | Planned (P1-W3) |
| FR-1.3 | ADR-005 offline computation · SRS v1.3 AC guardrails | DuckDB transition-frequency job (type group × material × NOAA climate region) → matrices with sample sizes; `docs/METHODOLOGY-DETERIORATION.md`; cohort-level UI | Unit tests vs hand-computed fixture matrices; sample-size floor rendering; methodology doc review (GR-6 framing) | Planned (P1-W4) |
| FR-1.4 | ARCH §3 aggregates → Postgres | Rankings + county report card views; CSV export (PDF = Could) | Golden-file export tests; sort-definition inspection in UI | Planned (P1-W5) |
| FR-1.5 | SRS §7 Census row | TIGER boundaries + ACS population staging with provenance; join-coverage metric | Join-coverage checks published; misses quarantined with reasons; ACS vintage cited in UI | Planned (P1-W2/W5) |

## 5. Phases 2–3 (stubs — rows elaborated at each phase gate)

| Req | Design anchor today | Verification sketch | Status |
|---|---|---|---|
| FR-2.1 | ADR-007 Node poller · ARCH §4.3 | Protobuf-decode fixtures; stream write tests | Deferred to Phase 2 gate |
| FR-2.2 | ADR-003 outbox · ARCH §4.3 failure modes | Failure-mode tests (poller/consumer/hub crash) | Deferred |
| FR-2.3 | ARCH §3 SignalR + live layer | Geofence math tests; live demo scenario | Deferred |
| FR-2.4 | ARCH §7 observability | Trace/SLO dashboard inspection; alert drill | Deferred |
| FR-2.5 | SRS §7 NOAA row | Overlay render test with recorded alert payloads | Deferred |
| FR-2.6 | SRS NFR-8 Swiftly terms | Terms page inspection — **gates FR-2.3 release** | Deferred |
| FR-3.1–3.4 | SRS §5 Phase 3 · OQ-4 open | Elaborated with phase picks at gate 2 | Deferred to Phase 3 gate |

## 6. Phase 0.5 — AI assist (added 2026-07-17, ADR-008)

| Req | Design | Implementation (planned) | Verification sketch | Status |
|---|---|---|---|---|
| FR-AI.1 | ADR-008 §2 + implementation pins (2026-07-18); SRS v1.2 AC-1…6 | `NlFilterSpec`/`NlFilterTranslator` (Core, rail-shaped schema, fail-closed) · `AnthropicAssistant` (C# SDK, claude-haiku-4-5, structured outputs) · `StubAssistant` · `AiRequestBudget` + cache in `/api/ai/query` · web AskTheMap ✓ built dark · **cloud wiring merged via `infra/ai-flip` (ACA secret + `Ai__*` env via Bicep, `UserSecretsId`, deploy passthrough — ships dark; RUNBOOK §9 is the operator procedure)** | Translator unit tests ✓; endpoint tests (dark path, stub path, budget trip, cache hit) ✓; e2e dark-path notice ✓; browser smoke vs national data ✓; **live-key smoke + `AI_ENABLED` flip = the 0.5 gate [RAZIEL] (RUNBOOK §9)** | In progress (dark) |
| FR-AI.2 | ADR-008 §2 narration guardrails | Drawer action + cached narration endpoint | Template-frame assertions (no un-displayed fields); disclaimer presence test | Planned (Could) |
| FR-AI.3 | ADR-008 §5 pgvector RAG | Coding-guide corpus loader + retrieval endpoint | Citation-presence checks; retrieval hit-rate spot checks | Planned (Could; embedding trade study first) |
