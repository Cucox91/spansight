# SpanSight — Requirements Traceability Matrix

**RTM** · v1.0 · Date: 2026-07-17 · Status: For review · Companion to [REQUIREMENTS.md](./REQUIREMENTS.md) (single source of requirement text — this file carries links, not copies)

**Maintenance rule (SDLC.md §§3–4):** update the row when a PR closes or changes a requirement; audited at every phase gate — a phase cannot close with an orphan (no design/implementation) or unverified (no evidence) in-phase requirement.

**Status values:** Planned · In progress · In review · Done · Deferred · Withdrawn.

> **2026-07-17:** Weeks 2–4 implementation pass delivered (AI-USAGE v1.1), line-reviewed and merged to `main` as PRs #2/#3. **2026-07-18 (AI-USAGE v1.2):** statuses now move to *Done* when the implementing PR merges with green CI; Raziel's mastery is verified in the post-completion code study. Real 2025 snapshot loaded locally: 743,398 read · 741,131 loaded · 2,267 quarantined (0.30%), run #1.

---

## 1. Phase 0 — functional requirements

| Req | Design | Implementation (planned → actual) | Verification | Status |
|---|---|---|---|---|
| FR-0.1 | [ARCH §4.2](./ARCHITECTURE.md) batch pipeline · ADR-005 | `SpanSight.Ingestion` `LoadPipeline` (unnest upsert, SHA-256 idempotency, run summaries) + `SpanSight.Core/Ingestion` parser — merged PR #2 | `Ingestion.Tests` dry-run exact-split; integration: reload no-op + `--force` convergence vs real PostGIS ✓; real 2025 snapshot: 743,398 → 741,131 loaded (2026-07-18) ✓ | Done |
| FR-0.2 | [ARCH §4.1](./ARCHITECTURE.md) staging/quarantine model | `NbiDmsCoordinateConverter` + `BridgeRowValidator` (10 reason codes) + `/api/qa/summary` + `web` QA page — merged PR #2 | `Core.Tests` converter/validator fixtures ✓; integration: QA reconciles with run summary + core count ✓; real data: 2,267 quarantined (0.30%), QA page reconciles ✓ | Done |
| FR-0.3 | [ARCH §3](./ARCHITECTURE.md) containers · §7 cross-cutting | `SpanSight.Api` endpoints (bridges/geojson/detail/lookups/stats), ProblemDetails, rate limiting, CORS, health, OpenAPI+Scalar — merged PR #2 | `BridgeQueryBuilderTests` ✓; Testcontainers: filters/bbox-GIST/pagination/404/400 ✓; EXPLAIN/index pass at 741k rows 2026-07-18: GIST bbox 0.9 ms, paged filter 1.6 ms, national group-by 31 ms; warm API 3–52 ms end-to-end ✓ | Done |
| FR-0.4 | [DESIGN.md](./DESIGN.md) + [mockup](./design/mockup.html) → React mapping | `web/src` — `AppShell`, `FilterRail`, `KpiStrip`, `BridgeMap`, `BridgeDrawer`, `QaPage`, shared `FilterState` predicate, `/bridge/{state}/{id}` deep link — merged PR #2 | lint + `tsc`/vite build clean ✓; full-stack smoke (fixture + national data, truncation note at 741k) ✓; Playwright smoke (AC-6) + axe scan in CI ✓ (PR #5, 5/5 vs national load) | Done |
| FR-0.5 | ADR-002 static PMTiles | `GeoJsonExporter` (`export-geojson` + run-meta sidecar) ✓ · `tools/build-tiles.sh` (tippecanoe → PMTiles + run-linked manifest) ✓ — merged PR #3 · Blob publish (Week 5) | Script clean from checkout: fixture 99 → pmtiles ✓; national 2026-07-18: 741,131 features → 22.4 MB `bridges.pmtiles` + manifest tied to run #1 ✓; tile-load smoke + laptop interaction check at Blob publish | In progress |
| FR-0.6 | [ARCH §7](./ARCHITECTURE.md) CI/CD · §5 topology | `docker-compose.yml` ✓ · `ci.yml` (4 jobs incl. e2e) ✓ · `infra/` Bicep incl. budget + container-app modules ✓ · API `Dockerfile` ✓ · `deploy.yml` (OIDC → Bicep → Entra-token migration → GHCR rollout → SWA publish → smoke) ✓ authored · [RUNBOOK.md](./RUNBOOK.md) §1 one-time setup pending [RAZIEL] | CI green on PRs ✓; first Deploy run + demo URL check pending §1 setup | In progress |

## 2. Non-functional requirements (Phase 0 scope)

| Req | Design | Implementation | Verification | Status |
|---|---|---|---|---|
| NFR-1 | ADR-002 (tiles off API) · indexed `core` | GIST on `location` + btrees on every filter column (`InitialSchema`); pagination caps | EXPLAIN pass at 741k rows 2026-07-18 ✓ (0.9–31 ms hot shapes, warm API 3–52 ms local); load-test script (pre-application season); App Insights p95 panel | In progress |
| NFR-2 | ADR-006-B topology · [HOSTING-ANALYSIS](./HOSTING-ANALYSIS.md) | `infra/modules/budget.bicep` — $50 cap, $40 actual + forecast alerts, deploys with the first resource (same template) ✓ | Portal inspection at each gate; spend log in gate notes | In progress |
| NFR-3 | ARCH §5 restart policies | Idempotent upsert + resumable runs (Week 2) | Kill-and-rerun test (FR-0.1 AC-3); compose/ACA restart config inspection | Planned |
| NFR-4 | ARCH §7 security | Rate limiter, CORS, headers (Week 3); scans in `ci.yml` ✓; secret-push protection | CI scan results; config inspection; no-PII source review (§7 SRS) | In progress |
| NFR-5 | AI-USAGE merge bar · DoD | Coverage reporting in CI (Week 2); ADR practice ✓ | Coverage gate ≥70% on parser/API core; PR-review inspection | In progress |
| NFR-6 | ARCH §7 observability · ADR-006-B | OTel config-gated in API: OTLP (local collector) / Azure Monitor distro (`APPLICATIONINSIGHTS_CONNECTION_STRING`, set by Bicep) ✓ | One trace browser → API → DB in App Insights (Week-5 exit); metrics dashboard demo | In progress |
| NFR-7 | [DESIGN.md](./DESIGN.md) a11y section | Native inputs, focus ring, aria-live KPIs ✓; QA report region keyboard-focusable (axe finding, fixed PR #5) | axe scan in CI failing on serious/critical (map canvas exempt) ✓; keyboard walkthrough in demo script (pending) | In progress |
| NFR-8 | SRS §7 licensing column | Footer attribution + disclaimer (Week 4); licenses doc (Week 6) | Inspection: footer, licenses file; FR-2.6 gates Phase-2 live map | Planned |

## 3. Ground rules (standing, all phases)

| Req | Where enforced | Verification | Status |
|---|---|---|---|
| GR-1/GR-2 | [ARCH §2](./ARCHITECTURE.md): externals limited to FHWA/BTS/Census/NOAA/Miami-Dade | Gate checklist review (R-6); repo/data-source inspection | Continuously verified |
| GR-3/GR-4 | Personal accounts + hardware only; [AI-USAGE](./AI-USAGE.md) employer-hygiene section | Gate checklist review | Continuously verified |
| GR-5 | Portfolio-only posture; no billing/sales surface | Gate checklist review | Continuously verified |
| GR-6 | Disclaimer footer (FR-0.4 AC-1); not-engineering-advice scope (SRS §11) | UI inspection each release | In progress |
| GR-7 | Resume/interview conduct — outside the product | Self-check at gates | Continuously verified |

## 4. Phases 1–3 (stubs — rows elaborated at each phase gate)

| Req | Design anchor today | Verification sketch | Status |
|---|---|---|---|
| FR-1.1 | ADR-005 Parquet + DuckDB | Vintage row-count reconciliation vs FHWA files | Deferred to Phase 1 gate |
| FR-1.2 | ARCH §4.1 `condition_snapshot` | Trend query tests vs known bridges | Deferred |
| FR-1.3 | ADR-005 offline computation | Model math hand-written [ME] + unit tests; methodology doc review | Deferred |
| FR-1.4 | ARCH §3 aggregates → Postgres | Export golden-file tests | Deferred |
| FR-1.5 | SRS §7 Census row | Join-coverage checks | Deferred |
| FR-2.1 | ADR-007 Node poller · ARCH §4.3 | Protobuf-decode fixtures; stream write tests | Deferred to Phase 2 gate |
| FR-2.2 | ADR-003 outbox · ARCH §4.3 failure modes | Failure-mode tests (poller/consumer/hub crash) — core [ME] | Deferred |
| FR-2.3 | ARCH §3 SignalR + live layer | Geofence math tests [ME]; live demo scenario | Deferred |
| FR-2.4 | ARCH §7 observability | Trace/SLO dashboard inspection; alert drill | Deferred |
| FR-2.5 | SRS §7 NOAA row | Overlay render test with recorded alert payloads | Deferred |
| FR-2.6 | SRS NFR-8 Swiftly terms | Terms page inspection — **gates FR-2.3 release** | Deferred |
| FR-3.1–3.4 | SRS §5 Phase 3 · OQ-4 open | Elaborated with phase picks at gate 2 | Deferred to Phase 3 gate |

## 5. Phase 0.5 — AI assist (added 2026-07-17, ADR-008)

| Req | Design | Implementation (planned) | Verification sketch | Status |
|---|---|---|---|---|
| FR-AI.1 | ADR-008 §2 (schema-constrained → FilterSpec) | `SpanSight.Core.Ai` port + Anthropic adapter in Api; `Ai:Enabled` flag (scaffolded, off) | Golden NL→FilterSpec cases; injection-attempt suite; cost-cap unit tests | Planned (post-Phase 0 demo) |
| FR-AI.2 | ADR-008 §2 narration guardrails | Drawer action + cached narration endpoint | Template-frame assertions (no un-displayed fields); disclaimer presence test | Planned |
| FR-AI.3 | ADR-008 §5 pgvector RAG | Coding-guide corpus loader + retrieval endpoint | Citation-presence checks; retrieval hit-rate spot checks | Planned (embedding trade study first) |
