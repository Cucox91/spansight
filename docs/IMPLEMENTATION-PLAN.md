# SpanSight — Implementation Plan

v1.3 · 2026-07-29 (v1.2: 2026-07-24 · v1.1: 2026-07-17 · v1.0: 2026-07-12) · Name locked: **SpanSight** (repo `spansight`, public from day 1 — OQ-2 closed). Companion to [REQUIREMENTS.md](./REQUIREMENTS.md) · [ARCHITECTURE.md](./ARCHITECTURE.md) · [AI-USAGE.md](./AI-USAGE.md) · v1.3 adds the Phase 1 gate note (§9) and the Phase 2 WBS (§11).

Task tags per the AI policy: **[ME]** hand-written/decided by Raziel · **[AI]** delegated, line-by-line reviewed · **[ME+AI]** paired.

> **Amendment 2026-07-17 (AI-USAGE v1.1):** [ME] implementation tags in §5 are reinterpreted — AI drafts all components including the novel cores; Raziel's [ME] obligation shifts to line-by-line review, the merge bar, and one hand-rebuilt core per phase (Phase 0 pick: NBI parser or DMS converter). [ME] *decision* tags (schema sign-off, budget, gate reviews, metric definitions) are unchanged. Weeks 2–4 implementation is being executed as a single AI pass delivered in reviewable batches; the weekly structure below remains the review/merge order.

> **Amendment 2026-07-17 later same day (AI-USAGE v1.2):** the [ME]/[AI] split is removed entirely — AI executes every task in this WBS, including former [ME] items, gated by green CI + AI self-review. Raziel retains: architectural decisions (ADRs), credential/billing/account actions (Azure PAYG, portal consents, API keys into secrets), and a structured post-completion code study that replaces pre-merge line review and absorbs the hand-rebuild/AI-free reps. Tags below are kept as written for the historical record.

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

Rolling-wave: each phase gets its own WBS at its gate. *(The [ME]/[AI] tagging described in v1.0 is historical — under AI-USAGE v1.2, AI executes all tasks and the former hand-written-core obligation folds into the post-completion code study. Phase WBS tasks now carry **lane tags** instead: see §10.)* The Phase 1 WBS is **§10**; the Phase 2 WBS is **§11**. Standing carry-overs: ~~Swiftly access~~ (granted 2026-07-17, R-4 closed — key stays in the password manager until Phase 2), SWA Standard +$9 decision when applications begin, dependency pin review at each gate.

## 7. Definition of Done

**PR** *(per AI-USAGE v1.2)*: builds · tests green · lint clean · AI self-review against the policy hard rules · `ai-assisted` labeled when applicable · docs/ADR updated if behavior or decisions changed. *(v1.0's "[ME] reviewed every line" is superseded — mastery is verified in the post-completion code study.)*
**Phase:** exit criteria met · GR checklist pass · demo live from `main` · retro note (what to change next phase) appended to this doc (§9).

## 8. Kickoff checklist (first session)

1. Create public repo `spansight` (MIT), push this docs set as the first commit.
2. Install toolchain (§1) · `az login` · create budget + alert.
3. Request Swiftly API access.
4. [AI] scaffold per Week 1 · [ME] review · first PR merged with CI green.

## 9. Phase gate log

### Gate 0 — Phase 0 close · 2026-07-24 · **PASS** (waivers + sign-off items recorded)

Demo live at **https://www.spansights.com** (+ apex, both with valid certs; API + storage CORS verified from all three origins). Checklist per SDLC §3:

| # | Gate item | Outcome |
|---|---|---|
| 1 | Requirements met | FR-0.1–0.6 **Done** in the RTM with linked evidence. One waiver: FR-0.6 AC-2's container-scan CI job deferred to P1-W1 (NuGet audit already fails builds on known-vulnerable packages). |
| 2 | NFR spot-checks | NFR-1 ✓ (EXPLAIN 0.9–31 ms at 741k; smooth live) · NFR-2 ✓ alert armed by Bicep (spend figure: [RAZIEL] sign-off below) · NFR-3 ✓ (idempotency tests + the real 2026-07-19 mid-load kill converging on rerun) · NFR-4 ✓ with the scan waiver · NFR-5 **waiver** — coverage is exercised but unmeasured in CI; report + ≥70% gate is a P1-W1 task · NFR-6 ✓ P0 scope (API→DB traces in App Insights; browser leg = FR-2.4) · NFR-7 ✓ (axe in CI + live; keyboard walkthrough ships with the demo script, P1-W1) · NFR-8 ✓ (footer + README licenses). |
| 3 | Ground rules | GR-1…GR-7 checklist **pass** (repo + data-source inspection: externals remain FHWA/BTS/Census/NOAA/Miami-Dade only; personal accounts/hardware; portfolio-only posture; disclaimer footer live on all origins; GR-7 conduct = standing [RAZIEL] self-check). |
| 4 | Docs current | SRS → v1.3 (Phase 1 slice elaborated) · RTM → v1.1 (close-out + Phase 1 rows) · README "How AI was used" rewritten to v1.2 policy (was stale v1.1 language) · CLAUDE.md status refreshed · this retro. No new ADRs required (custom-domain mechanics live in RUNBOOK §8 + Bicep comments — below the surprise bar). **Exception:** TEST-PLAN.md (planned Week 2) never authored → P1-W1 task; testing practice itself is real and CI-enforced. |
| 5 | Demo live | ✓ from `main`, both hostnames, full live Playwright + axe green (2026-07-19); domain cutover re-verified 2026-07-24. |
| 6 | Budget | Alert armed ✓ (deploys with every run). Expected ~$18–21/mo Phases 0–1. Spend figure at gate: **[RAZIEL] record from portal** (sign-off item). |
| 7 | Next-phase WBS | §10 drafted; approved when this PR merges. Entry criteria for Phase 1 = this gate + §10. |

**[RAZIEL] sign-off items (non-blocking, record here when done):** portal spend figure for June–July · one App Insights request-trace spot-check (NFR-6) · confirm GitHub secret-push protection is on for the repo (NFR-4).

**Retro — what to change for Phase 1:**

1. **CI must see what the demo runs.** Both tiles-mode bugs (PRs #15/#16) escaped because CI's e2e runs the GeoJSON fallback. Add a tiles-mode e2e variant (tiny fixture-built PMTiles checked in) — P1 backlog, Claude Code lane.
2. **Don't claim what CI doesn't measure.** The ≥70% coverage figure lived in docs since Week 2 while CI never reported it. Land the coverage report before the number appears anywhere again.
3. **Close RTM rows in the delivering PR, not at the gate.** FR-0.5/0.6 shipped 07-19 but their rows sat "In progress" for five days. The maintenance rule already says this; follow it.
4. **Runbook-first held up.** Nine deploy runs each failed one layer deeper, and writing RUNBOOK §7/§8 *as* things ran made the fixes reusable (the domain cutover reused half of them). Keep writing ops docs in the same PR as the change.
5. **Lane discipline (standing, per Raziel 2026-07-24):** Cowork authors docs, plans, and gate artifacts; implementation tasks route to Claude Code explicitly. §10 tags every task with its lane so nothing is ambiguous.

**Phase 0.5 status at gate:** FR-AI.1 built dark, all tests green; cloud wiring is a queued Claude Code task; the flip + live-key smoke are [RAZIEL] steps in RUNBOOK §9. Runs in parallel with P1-W1 — it does not gate Phase 1.

### Gate 1 — Phase 1 close · 2026-07-29 · **PASS** (one open defect sequenced; sign-off items recorded)

All Phase 1 features live at **https://www.spansights.com** (trends, patterns, rankings, county report cards, census-join QA) built from 34 reconciled vintages; PRs #22–#38. Checklist per SDLC §3:

| # | Gate item | Outcome |
|---|---|---|
| 1 | Requirements met | FR-1.1–1.5 **Done** in the RTM with linked evidence; both gate-0 waivers discharged P1-W1 (coverage gate PR #22, container scan PR #23). **No new waivers.** The P1-W6 pre-gate RTM audit (2026-07-28, findings-only) is dispositioned in the RTM's gate-1 note: two provenance findings (FR-1.2 closed one PR early; FR-1.1's five-layout evidence completed in FR-1.2's PR) are recorded as discipline lapses, not status errors — both rows are factually Done on `main`; four test-count figures corrected in place with the counting convention now stated; the missing FR-1.5 migration named. |
| 2 | NFR spot-checks | NFR-1 ✓ re-verified P1-W6 — reproducible harness, all 28 serving shapes at national scale, worst p95 66.4 ms vs 300 ms target; one regression (unindexed snapshot-year probe) found and fixed in the same pass · NFR-2 ✓ alert armed; spend: see item 6 · NFR-3 ✓ three convergent publish loaders (trends/matrices/county-join), each transactional and manifest-reconciled · NFR-4 ✓ three scan surfaces gated with a time-boxed allowlist (one entry, review 2026-10-31) · NFR-5 ✓ coverage gate live since P1-W1 (scoped 85–89% across Phase 1 vs the 70% bar) · NFR-6 ✓ P0 scope unchanged (browser leg lands with FR-2.4; trace spot-check = sign-off item below) · NFR-7 ✓ axe across seven route/state combinations incl. 375 px; keyboard walkthrough documented (`docs/design/keyboard-walkthrough.md`) and folded into the demo script · NFR-8 ✓ Census provenance (vintage, licence, citation) in committed catalogs; Swiftly obligations remain Phase 2-gated (FR-2.6). |
| 3 | Ground rules | GR-1…GR-7 **pass** — externals still FHWA/BTS/Census/NOAA only (Miami-Dade enters with Phase 2); GR-6 posture *strengthened*: method notes, methodology version and sample-size floors are served by the API with every analytic, so no view can render a number bare; the deterioration UI has no per-structure surface, asserted by test. |
| 4 | Docs current | SRS → **v1.5** (Phase 2 slice elaborated; v1.4 header-lag corrected) · RTM → **v1.5** (audit dispositions + Phase 2 rows) · **TEST-PLAN.md delivered** (gate-0 exception closed) · **DEMO-SCRIPT.md delivered** (G-4 + the NFR-7 walkthrough) · METHODOLOGY-DETERIORATION v1.1 · RUNBOOK §§10.3–10.6 written as-run · CLAUDE.md + README refreshed. Known doc debts queued as P2-W0 chores (§11): RUNBOOK §10.5 field names; `join-counties.sh` connection-string redaction; the Connecticut claim in `tools/trends/trends.sql`. |
| 5 | Demo live | ✓ from `main`, all Phase 1 surfaces live and verified from the public API (trends FL 2025: 13,070 = 7,705 G / 5,005 F / 360 P, partition exact; county join 99.9926% with provenance). **Exception, deliberate:** `/patterns` shows its published empty state — the FR-1.3 matrices publish (RUNBOOK §10.4) is **held** behind open defect D-1 (below) so the live e2e suite cannot go red on publish. |
| 6 | Budget | Alert armed ✓. Portal shows **$0 current and projected — promotional credits still active**; the first fully-billed month lands after the credit window closes (~mid-August). Consumption-based spend to be recorded then: **[RAZIEL] sign-off item**. Forecast unchanged: ~$18–21/mo for the Phase 0–1 footprint, ~$30–40 when the Phase 2 poller runs 24/7. |
| 7 | Next-phase WBS | §11 drafted below; approved when this PR merges. Entry criteria for Phase 2 = this gate + §11. **Windows migration: NO-GO (Raziel, 2026-07-28)** — development stays on the Mac; the bootstrap + migration guide remain archived in `planning/` if circumstances ever change. |

**Open defect D-1 (sequenced, not waived):** four FR-1.3 e2e tests assert fixture-scale properties (e.g. the Culvert cohort under-floor at 10 pairs) that are false against a national database, and the post-deploy smoke runs that suite live — so the §10.4 matrices publish would turn the smoke red. Fix is the first P2-W0 task; **§10.4 stays unexecuted until it merges** (the UI's empty state is designed and live). This ordering is deliberate: a red smoke that "everyone knows about" is how broken windows start.

**[RAZIEL] sign-off items (non-blocking, record here when done):** first post-promo spend figure (portal → Cost Management → Cost analysis once credits lapse) · one App Insights request-trace spot-check — portal → `rg-spansight-demo` → the App Insights resource → *Transaction search* → open any recent `GET /api/...` → confirm the PostgreSQL dependency appears under the request (NFR-6 P0 scope) · GitHub → repo *Settings → Advanced Security* → confirm **Secret scanning + Push protection = Enabled** (NFR-4) · re-run the §10.5 county join against the demo with the documented `PGCONN` (libpq) form so provenance carries a demo-measured job id (current figures are correct; the build was measured locally against an identical inventory) · §10.4 matrices publish **after D-1 merges**.

**Retro — what to change for Phase 2:**

1. **Ops docs must carry real values or the command that fetches them.** Two publishes failed on paste-ready blocks: a literal `<your-upn-truncated-to-63>` (28P01) and an Npgsql-format string handed to a libpq consumer (`invalid connection option "Host"`). Runbook blocks now state both connection formats and point at the pinned principal name; a verify command that names a wrong field (`bridgesUnderRetiredCodes`) fails silently under `jq` — verify commands are part of the tested surface.
2. **A schema isn't shipped until its grants are.** The first trends publish 500'd because `analytics` had no read grant for the API principal. Standing fix: `ALTER DEFAULT PRIVILEGES` for both DDL principals, applied 2026-07-26; any future schema addition must state its grant plan in the delivering PR.
3. **Stacked PRs need a base check at merge time.** PR #28 merged into its stacked base after the base's PR had already merged — 926 lines of UI stranded off `main` until a follow-up PR landed them. Before merging any follow-up PR, confirm its base is `main`.
4. **Adversarial pre-merge review stays.** It caught a GR-6 bypass (un-floored percentages in the caption), a non-transactional loader, a tautological invariant, and a perf harness timing its own logging — each before merge. The cost is real; the catches were load-bearing.
5. **Tests must state their scale, and publishes must be sequenced with the tests that watch them.** D-1 exists because fixture-scale expectations sat in a suite the live smoke also runs, while the data they assumed hadn't shipped yet. New rule in TEST-PLAN §3: derive expectations from the data under test or mark the test fixture-only.

*Kept from gate 0 and still true: close RTM rows in the delivering PR (the audit found two provenance lapses — the rule stands); runbook-first; lane discipline. The findings-only pre-gate audit (#37) earned a permanent slot in W6 of every phase.*

**Phase 0.5 status at gate:** unchanged by design — wiring merged (PR #21), feature dark, no key set. Enablement waits on the abuse-hardening change-control item (rate limits beyond the built-ins, human verification, injection review), authored in Cowork when Raziel schedules it; it does not gate Phase 2.

## 10. Phase 1 work breakdown (roadmap weeks 7–12)

**Lane tags:** **[CC]** = Claude Code session (implementation in-repo) · **[CW]** = Cowork session (docs/planning artifacts, by PR) · **[RAZIEL]** = human-only (credentials, billing, account actions, sign-offs). AI executes [CC]/[CW] per AI-USAGE v1.2; every task lands by PR with green CI.

**Environment:** Phase 1 runs on the **Mac** (decision 2026-07-24); the Windows migration ([bootstrap delivered 07-22]) is deferred to the P1→P2 seam, where it gets a go/no-go decision. All heavy data work stays local per ADR-005; **no new Azure resources this phase** (aggregates live in the existing PG; Parquet archive → existing Blob, cool tier).

### W0 (parallel with W1) — Phase 0.5 flip *(does not gate Phase 1)*
- [CC] `infra/ai-flip` PR: `UserSecretsId` on the API project, ACA secret + `Ai__*` env wiring in Bicep, `deploy.yml` secret/variable passthrough — contract in RUNBOOK §9; ships dark (flag still off).
- [RAZIEL] RUNBOOK §9 steps: Anthropic key (spend-capped) → GitHub secret, `AI_ENABLED` variable, deploy dispatch, live-key smoke on www.spansights.com.
- [CC] On green smoke: RTM FR-AI.1 → Done; CLAUDE.md status line.

### W1 (wk 7) — Carry-overs + vintage foundations
- [CW] `docs/TEST-PLAN.md` — formalize the existing strategy (xUnit + Testcontainers + Playwright/axe + live smoke), coverage policy, tiles-mode gap plan (gate 0 exception close-out).
- [CW] Demo script (10-min walkthrough, G-4) including the NFR-7 keyboard pass.
- [CC] Coverage report + ≥70% threshold on parser/API core in `ci.yml` (NFR-5 waiver close).
- [CC] Container-scan job (e.g. Trivy) on the built API image (FR-0.6 AC-2 waiver close).
- [CC] FR-1.1 start: vintage download/convert tooling + normalized Parquet schema + catalog manifest; fixtures for ≥3 eras. [RAZIEL] runs the bulk 1992–2025 download locally (multi-GB, stays out of git).
- **Exit:** waivers closed in CI · ≥3 vintages converting clean with reconciliation.

### W2 (wk 8) — FR-1.1 complete + FR-1.5 staging
- [CC] All 34 vintages → Parquet; per-vintage row-count reconciliation report; rejects itemized; Blob cool-tier archive script; DuckDB catalog entry point (`tools/`).
- [CC] FR-1.5: TIGER county boundaries + ACS population staging with provenance (vintage + license recorded).
- **Exit:** FR-1.1 ACs met · catalog reproducible from clean checkout · join keys ready.

### W3 (wk 9) — FR-1.2 condition trends
- [CC] DuckDB trend job (per-bridge year × G/F/P via the Phase 0 classifier) → aggregate tables + EF migration; API endpoints + tests; drawer sparkline + trends view; EXPLAIN pass (NFR-1).
- **Exit:** FR-1.2 ACs met · golden tests green vs ≥3 hand-checked bridges.

### W4 (wk 10) — FR-1.3 deterioration patterns ✅ **Done 2026-07-26**
- [CC] Transition-frequency job (type group × material × NOAA climate region) with sample-size floors; unit tests vs hand-computed fixtures; cohort-level UI with adjacent disclaimer.
- [CW] `docs/METHODOLOGY-DETERIORATION.md` (assumptions, limitations, GR-6 framing) — reviewed with the feature PR.
- **Exit:** FR-1.3 ACs met · methodology doc merged with the feature, not after it. **Both met** — RTM FR-1.3 → Done; the doc merged in the feature PR at v1.1, carrying eight corrections and six decisions found by measuring the full 34-vintage set before writing code (SDLC §4 change-log rows; no SRS edit required). Publish procedure: RUNBOOK §10.4.

### W5 (wk 11) — FR-1.4 + FR-1.5 surface
- [CC] Rankings + deep-linkable county report card; CSV export with golden-file tests (PDF stays Could); join-coverage metric on the QA page; population-served figures with ACS citation.
- **Exit:** FR-1.4/1.5 ACs met.

### W6 (wk 12) — Hardening + gate 1
- [CC] Perf/EXPLAIN pass over all new query shapes; axe over new UI; tiles-mode e2e variant if the backlog allows.
- [CW] RTM close-out in the delivering PRs (retro lesson 3) · gate 1 retro · SRS Phase 2 slice · Phase 2 WBS (§11).
- [RAZIEL] Spend check · gate sign-off · **Windows migration go/no-go for Phase 2**.
- **Exit:** gate 1 checklist pass · Phase 2 entry criteria set.

## 11. Phase 2 work breakdown (roadmap weeks 13–18)

**Lane tags as §10.** **Environment: the Mac, permanently** — Windows migration declined at this seam (gate 1, item 7). New this phase: the ACA workload gains an always-on poller + Redis sidecar (ADR-006-B/OI-1), moving expected spend from ~$18–21 to **~$30–40/mo** (NFR-2 headroom holds; alert stays $40 — expect it to fire occasionally and treat that as the system working). The Swiftly key leaves the password manager for user-secrets (local) and GitHub/ACA secrets (cloud) — **[RAZIEL] only**, never CI, never the repo (NFR-8 §10). GTFS-RT fixtures in the public repo must be synthetic or minimized-derived — no raw feed dumps (NFR-8 §4.vi).

### W0 (immediately, before any publish) — Carry-overs from gate 1
- [CC] `fix/deterioration-e2e-scale` — **D-1**: make the four fixture-coupled FR-1.3 e2e assertions derive their expectations from the data under test (TEST-PLAN §3 scale rule); prove by running the suite against a national database locally.
- [RAZIEL] After D-1 merges: §10.4 matrices publish → `/patterns` live check; §10.5 demo-measured county-join re-run (PGCONN).
- [CC] Docs sweep chore PR: RUNBOOK §10.5 verify fields (`rowsUnderRetiredCodes`/`structuresUnderRetiredCodes`), `join-counties.sh` redacts the connection string on failed ATTACH, Connecticut claim in `tools/trends/trends.sql`, and an ADR-lite note (or FR-0.3 change-control flag) for the `/api/stats/summary` record-type question the census join surfaced.
- **Exit:** D-1 closed · all four Phase 1 publishes live with demo-measured provenance · doc debts cleared.

### W1 (wk 13) — Poller foundations
- [CC] Compose gains the poller service + Redis Streams config; Node/TS scaffold per ADR-007 (pins recorded); GTFS-RT protobuf decode against committed synthetic fixtures; normalized position schema (vehicle, route, timestamp, lon/lat) documented.
- [RAZIEL] Swiftly key → local user-secrets/`.env` from the password manager; confirm the 180 req/15 min budget against the agency feed list.
- **Exit:** local poller decodes fixtures → XADD to compose Redis · zero keys in CI.

### W2 (wk 14) — FR-2.1 complete
- [CC] Poll loop within the documented rate budget; backoff + reconnect; malformed-entity quarantine with reasons; stream write tests; runs against the live feed **locally only** ([RAZIEL] supervises first live run).
- **Exit:** FR-2.1 ACs met · sustained local run clean · nothing deployed yet.

### W3 (wk 15) — FR-2.2 outbox
- [CC] .NET consumer group: transactional event + outbox persist; dispatcher; failure-mode suite (poller crash → stream persists; consumer crash → XAUTOCLAIM reclaim without loss; duplicate delivery converges) — Testcontainers PG + Redis.
- **Exit:** FR-2.2 ACs met · every documented failure mode is a passing test name.

### W4 (wk 16) — FR-2.3 live layer + FR-2.6 terms
- [CC] SignalR hub + SPA live vehicle layer (reconnect + snapshot re-sync); geofence event for the Poor-condition demo scenario (GR-6 wording — proximity described, nothing predicted); ships **flag-off**.
- [CW] FR-2.6 end-user terms page content mirroring the Swiftly license obligations (§6/§4.vi/§10/§14) — [RAZIEL] reviews against the license text.
- **Exit:** live layer green behind the flag · terms content approved.

### W5 (wk 17) — FR-2.4 observability + cloud enablement
- [CC] OTel browser leg (closes the NFR-6 gap) + poller/consumer instrumentation (stream lag, poll latency); dashboards; SLOs + alert rules by Bicep; alert drill executed with [RAZIEL], recorded in RUNBOOK.
- [CC] Bicep: poller container + Redis sidecar in ACA — deployed **dark** (poller off until W6 flip).
- **Exit:** one W3C trace browser → API → DB in App Insights · dashboards live · infra deployed dark.

### W6 (wk 18) — FR-2.5, go-live + gate 2
- [CC] NOAA weather overlay (no key; viewport alerts; zero-alert and API-down states explicit) · hardening: axe over new UI, perf pass over live paths, e2e for reconnect.
- [RAZIEL] Swiftly key → GitHub/ACA secrets · terms page live → **public flip of the live layer** (FR-2.6 gates FR-2.3) · spend check (~$30–40 expected).
- [CW] Gate-2 artifacts: retro · SRS Phase 3 picks + slice · Phase 3 WBS (§12).
- **Exit:** gate 2 checklist pass · live map public with terms · Phase 3 entry criteria set.

---

*Version pins verified 2026-07-12: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [Node.js releases](https://nodejs.org/en/about/previous-releases)*
