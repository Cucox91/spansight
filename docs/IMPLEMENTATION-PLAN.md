# SpanSight — Implementation Plan

v1.1 · 2026-07-17 (v1.0: 2026-07-12) · Name locked: **SpanSight** (repo `spansight`, public from day 1 — OQ-2 closed). Companion to [REQUIREMENTS.md](./REQUIREMENTS.md) · [ARCHITECTURE.md](./ARCHITECTURE.md) · [AI-USAGE.md](./AI-USAGE.md)

Task tags per the AI policy: **[ME]** hand-written/decided by Raziel · **[AI]** delegated, line-by-line reviewed · **[ME+AI]** paired.

> **Amendment 2026-07-17 (AI-USAGE v1.1):** [ME] implementation tags in §5 are reinterpreted — AI drafts all components including the novel cores; Raziel's [ME] obligation shifts to line-by-line review, the merge bar, and one hand-rebuilt core per phase (Phase 0 pick: NBI parser or DMS converter). [ME] *decision* tags (schema sign-off, budget, gate reviews, metric definitions) are unchanged. Weeks 2–4 implementation is being executed as a single AI pass delivered in reviewable batches; the weekly structure below remains the review/merge order.

## 1. Toolchain & one-time setup (Mac)

| Item | Pin | Note |
|---|---|---|
| .NET SDK | **10 LTS** ([supported to Nov 2028](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)) | API, ingestion, tests |
| Node.js | **24 LTS** ("Krypton", [to Apr 2028](https://nodejs.org/en/about/previous-releases)) | web build, Phase 2 poller |
| Docker Desktop | current | compose stack |
| Azure CLI + Bicep, GitHub CLI | current | deploys, repo ops |
| tippecanoe | `brew install tippecanoe` | PMTiles builds |
| DuckDB CLI | current | Phase 1 analytics |
| React/Vite/MapLibre | latest stable at scaffold time | re-check pins at each phase gate |

**Accounts (personal only — GR-3):** GitHub repo `spansight` (public, MIT license); Azure subscription with **budget $50 + alert $40 created before any resource**; Swiftly API access requested day 1 (OI-3 — lead-time hedge).

**Data policy:** no bulk NBI data in git — raw files and Parquet live locally + Blob archive; the repo carries only small test fixtures (a few hundred rows).

## 2. Repository layout

```
spansight/
├── docs/                  # this docs set moves in (incl. AI-USAGE.md, ADRs)
├── infra/                 # Bicep: main.bicep + modules (postgres, aca, swa, storage, insights)
├── src/
│   ├── SpanSight.Core/            # domain: entities, condition logic, SNBI crosswalk
│   ├── SpanSight.Api/             # ASP.NET Core Web API + SignalR (Phase 2)
│   ├── SpanSight.Ingestion/       # CLI worker: download → parse → validate → load → tiles
│   └── tests/                     # Core.Tests, Api.Tests (Testcontainers), Ingestion.Tests
├── web/                   # React + TS + Vite + MapLibre
├── tools/                 # tippecanoe scripts, DuckDB scripts (Phase 1)
├── .github/workflows/     # ci.yml, deploy.yml (OIDC → Azure)
├── docker-compose.yml     # postgres+postgis, redis, otel, grafana (profile)
├── CLAUDE.md              # AI context (transparency artifact)
└── README.md              # incl. "How AI was used" section
```

## 3. Working conventions

- **Trunk-based**: short-lived branches → PR → `main`; `main` always deployable.
- **PRs even solo**: review discipline + `ai-assisted` label per AI-USAGE; PR template with AI checkbox.
- **Conventional commits**; ADR added for any decision that would surprise a reviewer.
- **CI gates every PR**: build, tests, lint/format, dependency + container scan.
- **Backlog**: GitHub Issues + Projects board, seeded from §5 (one issue per task, `phase-0` milestone) — [AI] generates the issue set from this doc, [ME] approves.

## 4. Environments

| Env | Where | Config |
|---|---|---|
| `local` | Docker Compose on Mac | user-secrets, seeded fixture data |
| `demo` | Azure (single env) | Bicep-provisioned; GitHub OIDC deploys; managed identity for API→PG (Entra auth) and API→Blob; EF Core migrations applied as a pipeline step before app rollout |

No staging tier — cost discipline; `local` + smoke tests carry that weight during the portfolio phase.

## 5. Phase 0 work breakdown (roadmap weeks 1–6)

### Week 1 — Foundations
- [AI] Scaffold solution, web app, compose stack, CI skeleton (build+test on PR).
- [ME] Azure subscription, budget $50/alert $40; [AI] Bicep baseline (RG, ACA env, PG Flexible B1ms + PostGIS, SWA, Blob, App Insights) with [ME] reviewing every resource.
- [ME] Request Swiftly access · download NBI 2025 snapshot + Coding Guide · study record layout.
- [ME+AI] CLAUDE.md + PR template + issue seeding.
- **Exit:** compose up green · CI green · `az deployment` from pipeline creates all demo resources · budget alert armed.

### Week 2 — Ingestion core (the hand-written heart)
- [ME] Schema design: `staging_legacy`, `core`, `quarantine` (+ provenance) — [AI] reviews.
- [ME] **NBI delimited parser + DMS→decimal coordinate converter** (first-of-a-kind: hand-written).
- [ME] Validation/quarantine rules (coord bounds, state mismatch, impossible values) + test cases; [AI] test harness + fixtures.
- [AI] CLI plumbing: config, logging, idempotent re-run wrapper (FR-0.1) — [ME] review.
- **Exit:** 2025 snapshot loads locally with QA counts · ≥70% coverage on parser/validators.

### Week 3 — API
- [ME] **First endpoint** `GET /api/bridges` (state/county/condition/type/year/bbox filters + pagination) with EF Core + NetTopologySuite mapping (first-of-a-kind).
- [AI] Remaining endpoints (bridge detail, lookup/decode tables, summary stats), DTOs, OpenAPI polish — [ME] line review.
- [AI] Testcontainers integration harness; [ME] the assertions for filter correctness.
- [ME+AI] Rate limiting, CORS, ProblemDetails, health checks.
- **Exit:** national-scale queries p95 < 300 ms locally ([ME] index/EXPLAIN pass).

### Week 4 — Web + tiles
- [ME] **First React+MapLibre component** (map + filter state), translating `design/mockup.html`.
- [AI] KPI strip, detail drawer, app chrome from the mockup; [ME] review.
- [ME+AI] Tile pipeline: core → GeoJSON export → tippecanoe → PMTiles (script in `tools/`).
- **Exit:** local SPA runs against local API + tiles and matches the mockup.

### Week 5 — Cloud
- [AI] Finalize Bicep + `deploy.yml` (OIDC, image build/push, EF migration step, ACA rollout, SWA deploy, tile upload to Blob) — [ME] approves each stage.
- [ME] Verify managed-identity auth end-to-end and one OTel trace browser → API → DB in App Insights.
- **Exit:** public demo URL live · CI smoke test (load map → filter → open bridge) green · spend check.

### Week 6 — Hardening + Phase 0 gate
- [AI] Data QA report page UI; [ME] the metric definitions (reject rates, coverage by state).
- [ME] Performance pass · [ME+AI] README, demo script, "How AI was used" section.
- [ME] **Phase gate review:** all FR-0.x done · GR checklist (§2 REQUIREMENTS) pass · budget within plan · docs current.
- **Exit:** Phase 0 demoable to a hiring manager; Phase 1 planning session.

## 6. Phases 1–3

Rolling-wave: each phase gets its own WBS at its gate, same [ME]/[AI] tagging (novel cores hand-written: deterioration model math in P1, outbox consumer in P2). Standing carry-overs: Swiftly access status (R-4 fallback if delayed), SWA Standard +$9 decision when applications begin, dependency pin review.

## 7. Definition of Done

**PR:** builds · tests green · lint clean · [ME] reviewed every line (AI or not) · `ai-assisted` labeled when applicable · docs/ADR updated if behavior or decisions changed.
**Phase:** exit criteria met · GR checklist pass · demo live from `main` · retro note (what to change next phase) appended to this doc.

## 8. Kickoff checklist (first session)

1. Create public repo `spansight` (MIT), push this docs set as the first commit.
2. Install toolchain (§1) · `az login` · create budget + alert.
3. Request Swiftly API access.
4. [AI] scaffold per Week 1 · [ME] review · first PR merged with CI green.

---

*Version pins verified 2026-07-12: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [Node.js releases](https://nodejs.org/en/about/previous-releases)*
