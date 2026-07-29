# SpanSight — 10-Minute Demo Script

v1.0 · 2026-07-29 · Author: Raziel Arias (authored in Cowork per AI-USAGE v1.2) · Goal G-4: a rehearsed walkthrough per completed phase · Audience: hiring manager / interviewer (P-1) · Companions: [DESIGN.md](./DESIGN.md) · [design/keyboard-walkthrough.md](./design/keyboard-walkthrough.md) · [TEST-PLAN.md](./TEST-PLAN.md)

**Setup (before the call):** open https://www.spansights.com in a fresh window · second tab on the GitHub repo · if the live site is unreachable, fall back to `docker compose up` + local fixture data and say so plainly. Timings assume ~9 minutes of content + buffer.

---

## 0:00 — Open on the map (45 s)

Load the site cold — the point is that it orients in seconds (G-1).

> "SpanSight is every bridge in the National Bridge Inventory — about 624,000 structures — on one map. Color is condition: Good, Fair, Poor, straight from published FHWA ratings. The tiles are pre-generated vector tiles served as static files, so this national view costs nearly nothing to serve and stays smooth."

Pan/zoom once. Point at the KPI strip recomputing.

## 0:45 — Filters are one shared predicate (75 s)

In the rail: **Florida → condition Poor → truss group → built before 1970**.

> "Every control drives one shared filter state — the map layer, the KPIs and the result set recompute from the same predicate; there's no Apply button and no way for the panels to disagree."

Click a bridge → detail drawer.

> "Raw NBI codes arrive decoded — no Item-43 numerology. And the URL is a deep link; refresh reproduces this exact view."

## 2:00 — Thirty-four years of history (90 s)

In the drawer, point at the **condition sparkline**.

> "Phase 1 added the full 1992–2025 history: 22.3 million published rows converted to Parquet, reduced offline with DuckDB, and only compact aggregates reach the serving database — the hosted Postgres stays on the cheapest tier. This structure's line is its published Good/Fair/Poor by year; years FHWA didn't publish are gaps, never interpolated."

Navigate to **/trends** (state level, Florida).

> "Same data rolled up: condition shares over time for any state or county. The method note travels with the numbers — it's served by the API, so no view can render a chart without it."

## 3:30 — Deterioration patterns, honestly framed (90 s)

Navigate to **/patterns**. Pick deck matrices, a populated cohort.

> "Year-over-year rating-transition frequencies by structure type, material and climate region — 50 million component pairs. Two things I want you to notice about the framing. Every rate below a sample floor of fifty renders as 'insufficient data', never a number. And the caption tells you that NBI re-inspects on roughly a two-year cycle, so the diagonal is inflated by carried-forward ratings — the methodology doc linked right there says exactly what this table can and cannot claim. Descriptive statistics of published ratings, cohort level only — never a prediction about a structure."

*(If matrices show their published empty state, say: "held back deliberately — the publish is sequenced behind a test fix, which is its own story about not shipping red" and move on.)*

## 5:00 — Rankings and the county report card (90 s)

Navigate to **/rankings** → worst-condition by county; then a **county report card** (Miami-Dade: `/county/12086`).

> "Every ranking serves its own definition — headline, sort rule, inclusion rule, what the floor excluded — because a ranking whose exclusions are invisible reads as exhaustive. The county card pulls it together: counts, condition shares, the trend series, and population served with the ACS vintage cited inline. Any of these exports to CSV, and the definition rides along as comment lines in the file."

## 6:30 — The data-quality story (60 s)

Navigate to **/qa**.

> "This page is why you can trust the rest. Ingestion quarantines rather than drops — every reject has a reason code. The Census join measured 99.99% point-in-polygon coverage, and the misses are itemized, including two bridges sitting exactly on a county line. My favorite: Connecticut retired its counties in 2022, but NBI still publishes the old codes — about 5,600 rows under codes the Census no longer recognizes. The page explains it instead of hiding it."

## 7:30 — Keyboard pass (45 s)

Run the short form of [design/keyboard-walkthrough.md](./design/keyboard-walkthrough.md): Tab through the filter rail → apply a filter by keyboard → open a result → Esc closes the drawer → tab into a rankings table region.

> "Accessibility is CI-enforced — axe fails the build on serious findings, across seven routes down to phone width."

## 8:15 — The engineering close (90 s)

Switch to the repo tab. Scroll the README, then docs/.

> "Everything you just saw deploys from `main` on merge: GitHub Actions with OIDC into Azure — no stored cloud credentials — Bicep for every resource, EF migrations in the pipeline, Playwright and axe against the live site. The docs set is the process: requirements with acceptance criteria, an RTM where every requirement links its evidence, ADRs for the decisions, runbooks written as things ran, and phase gates with retros.
>
> And the part I'm most often asked about: this was built AI-first, and that's documented, not hidden — AI-USAGE.md defines the policy, CLAUDE.md is committed as the transparency artifact, PRs carry an `ai-assisted` label, and my role is the architecture decisions, the credentials, and a structured post-completion code study. The interesting engineering problem of 2026 isn't whether to use AI — it's how to keep an AI-built system honest. That's what the invariant gates, the adversarial reviews and this documentation trail are for."

## 9:45 — Land it (15 s)

> "Phase 2 adds live transit positions over this map — Redis Streams, a transactional outbox, SignalR — the real-time patterns, instrumented end to end. Happy to go deeper on any layer."

---

## Fallback notes

- **Live site down:** compose stack + fixture data; the script survives with "national" numbers swapped for fixture counts — say which you're showing.
- **A view errors:** every Phase 1 view has a published empty state; read it aloud — designed degradation *is* a talking point.
- **Time compressed to 5 min:** run 0:00 → 2:00 → 3:30 → 8:15.

## Change log

| Version | Date | Change |
|---|---|---|
| v1.0 | 2026-07-29 | Initial script at gate 1 (G-4 for Phase 1; NFR-7 keyboard pass folded in via keyboard-walkthrough.md). |
