# SpanSight — Test Plan

v1.0 · 2026-07-29 · Author: Raziel Arias (authored in Cowork per AI-USAGE v1.2) · Status: formalizes practice already CI-enforced since Phase 0 — the gate-0 documentation exception, closed at gate 1 · Companions: [SDLC.md](./SDLC.md) · [REQUIREMENTS.md](./REQUIREMENTS.md) · [TRACEABILITY.md](./TRACEABILITY.md) · [AI-USAGE.md](./AI-USAGE.md)

This document describes the testing system the repo already runs, so the strategy is inspectable rather than folklore. Where a practice was learned mid-phase, the lesson that produced it is cited. Nothing here is aspirational: every gate named below fails a real build today.

## 1. Layers

| Layer | Tooling | What it must catch | Where it runs |
|---|---|---|---|
| Unit | xUnit (`src/tests/*`) | Parsing, validation, DMS conversion, classification, cohort/lookup mapping, codecs — including **golden tests against hand-computed fixtures** whose expected values are written down *before* the code runs (FR-1.3's fixture README pattern) | every PR (`dotnet` job) |
| Integration | xUnit + **Testcontainers against real PostGIS** (Redis joins in Phase 2) | Query correctness, filter semantics, pagination, ProblemDetails/404/429 paths, loader idempotency and convergence — nothing is mocked that the demo runs for real | every PR |
| Anti-drift | Golden suites that execute the **offline job's own SQL** and compare it to the C# it must agree with (`trends.sql` vs `ConditionClassifier`; `deterioration.sql` cohort lookups vs `NbiCohorts`, plus C#/TypeScript parity) | Two implementations of one rule silently diverging | every PR |
| End-to-end | Playwright, **`fallback`/`tiles` matrix** — the tiles leg builds a real PMTiles archive from the committed fixture with the production script and serves it over range requests on a separate origin | What the demo actually runs, including the 206/CORS path that three live-only bugs hid behind (gate-0 retro lesson 1; issue #34, closed P1-W6) | every PR (`e2e` job) |
| Accessibility | axe in the e2e suite — serious/critical fail the build (map canvas exempt); seven route/state combinations scanned, including **375 px** | WCAG 2.1 AA on UI chrome (NFR-7); the P1-W6 pass caught an invisible-header overflow no desktop viewport showed | every PR |
| Performance | `tools/perf/{perf-pass.sh,shapes.tsv}` — reproducible NFR-1 harness: all serving shapes at full national scale, plans captured via `auto_explain` from statements the API itself issued, latency measured in a separate uninstrumented pass | p95 < 300 ms (NFR-1); regressions like the unindexed snapshot-year probe (P1-W6, 12 shapes affected) | at phase gates + before application season |
| Publish invariants | Every offline job (`build-trends`, `build-deterioration`, `join-counties`) **refuses to write unless its invariants pass**; every loader reconciles against its manifest inside one transaction | A wrong aggregate reaching the serving database — the check is the tool, not a reviewer's eyeball | every [RAZIEL/CC] data publish |
| Live smoke | Post-deploy readiness + reachability; full live Playwright + axe on `run_e2e` dispatch | The deployed thing, on the real origins | every deploy / on dispatch |

## 2. Standing gates in CI

- **Coverage:** line coverage measured on every run (`coverlet.runsettings` + `tools/coverage-gate.py`); **< 70% scoped fails the build**, scoped to what NFR-5 names — Core parsing/validation/conversion/classification, the ingestion pipeline, the API querying path. The repo-wide number prints but does not gate. (P1 runs: 85–89% scoped.)
- **Dependency + container scanning:** NuGet audit (NU1903 + TreatWarningsAsErrors) · Trivy on the built API image (fixable HIGH/CRITICAL block) · `tools/npm-audit-gate.py` with a time-boxed accepted-risk allowlist — an allowlist entry past its review date fails the gate.
- **Lint/format:** `dotnet format --verify-no-changes`; `web/` lint + `tsc`.
- A red check blocks merge; `main` stays deployable (IMPLEMENTATION-PLAN §3).

## 3. Rules the suite must follow

1. **Fixtures only, never bulk data** (CLAUDE.md rule 4). Vintage fixtures are ~300 real rows per distinct published layout (SRS v1.4); PMTiles for the tiles e2e leg are *built in CI from fixtures*, never committed — a checked-in archive would let the tile schema drift with CI green.
2. **State your scale.** A test that asserts a property of fixture-scale data (a count, a floor crossing, a specific cohort's sufficiency) must either derive the expectation from the data it runs against or be marked fixture-only. This rule exists because four FR-1.3 e2e tests baked fixture-scale expectations into assertions that the live smoke also runs — red-on-publish, found at the pre-gate audit (open defect D-1, gate-1 note).
3. **Report test counts as `dotnet test --list-tests` cases**, and say so. The pre-gate audit found four RTM figures that matched no counting convention; numbers in evidence cells now name their convention.
4. **Mutation-verify the tests that matter.** A guard that cannot fail is decoration: revert the bug (or flip the boundary — the FR-1.3 floor fixture sits at 49/50/51) and confirm the named test fails. The P1-W6 passes applied this to a11y geometry, tiles assertions, region maps and the perf harness itself.
5. **Verification evidence lives in the RTM row** of the requirement it proves, updated in the delivering PR (gate-0 retro lesson 3).
6. **No keys, no spend in CI.** AI paths run against the deterministic stub provider; Phase 2 GTFS-RT tests run against committed synthetic fixtures (no raw Swiftly payloads in the public repo — NFR-8).

## 4. Ownership (AI-USAGE v1.2)

AI implements and self-reviews; merges are gated by green CI; Raziel's mastery is verified in the post-completion code study rather than pre-merge line review. Adversarial pre-merge review of a feature branch is standing practice for substantive PRs — during Phase 1 it caught a GR-6 caption bypass, a non-transactional loader write, and a perf harness measuring the wrong thing before any of them merged.

## 5. Phase 2 additions (entering with WBS §11)

Redis joins the Testcontainers matrix · stream failure-mode suite (poller crash, consumer crash + XAUTOCLAIM reclaim, hub restart + client re-sync — FR-2.2's ACs are test names) · recorded/synthetic protobuf fixtures for GTFS-RT decoding · SignalR reconnect e2e · alert-drill evidence for FR-2.4. The scale rule (§3.2) applies to live-feed tests from day one: vehicle counts vary by time of day and must never be exact-asserted.

## 6. Change log

| Version | Date | Change |
|---|---|---|
| v1.0 | 2026-07-29 | Initial document at gate 1 — formalizes the enforced-since-Phase-0 practice; adds the scale-statement and counting-convention rules from the P1-W6 pre-gate audit findings. |
