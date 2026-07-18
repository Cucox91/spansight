# SpanSight — AI Usage Policy

v1.2 · amended 2026-07-17 (v1.1: 2026-07-17 · v1.0: 2026-07-12) · Author: Raziel Arias · **This file ships with the repo on purpose** — it is part of the portfolio, not metadata about it.

## Why this document exists

This is a portfolio project: its job is to prove *my* engineering ability. AI is used throughout — openly, under the rules below — because a documented, disciplined AI workflow is itself a skill hiring teams screen for. The failure modes this policy prevents: code I can't defend in an interview, and an unrealistic "no AI" posture.

## Principles

1. **Engineer of record.** Every architectural decision is mine. AI is a sparring partner; ADRs record my calls and my reasoning.
2. **The quality gate** *(amended 2026-07-17, v1.2; v1.1 required pre-merge line review)*. Merges are gated by green CI (build, full test suite, lint/format, scans) plus AI self-review against this policy's hard rules. My deep review moves to a **structured post-completion code study** (principle 3) — the public record (PRs, commits, this policy) says plainly which is which.
3. **AI builds, I study to mastery** *(amended 2026-07-17, v1.2; v1.1 required mastery before merge)*. AI implements the entire product — including former [ME]-tagged tasks — to reach a working demo with AI capabilities as fast as quality allows. When the build completes, I work through the codebase end to end (trace, run, step through, annotate) until I can explain and rewrite any part of it. The per-phase hand-rebuild and weekly AI-free rep from v1.1 fold into that study plan rather than gating the build.
4. **Both directions.** AI reviews its own diffs (code review, security review) before every PR; CI is the impartial gate. Verification is never skipped because implementation was AI's.
5. **Hard boundaries that survive any policy version.** Credentials, billing, and account actions stay human: API keys and secrets never enter the repo or a prompt; Azure billing/subscription changes are mine; cloud resources only via `infra/` Bicep (CLAUDE.md hard rules).

## Division of labor (v1.2)

| Work | Who | Examples |
|---|---|---|
| Architecture, ADRs, trade-off decisions | **Me** (AI as debate partner) | Postgres vs MSSQL (ADR-001), hosting (ADR-006-B), AI features (ADR-008) |
| Implementation — everything, including former [ME] tasks | **AI**, gated by CI + AI self-review | Parser, schema, endpoints, SPA, tiles, tests, IaC, deploy workflow |
| Post-completion mastery | **Me** | Structured code study of the finished product; hand-rebuild reps folded in |
| Credentials, billing, account actions | **Me** (never AI, never in-repo) | Azure PAYG/budget consent, portal/OIDC auth, API keys into .env/secrets |
| Docs drafting, research, analysis | **AI**, I direct and approve | This docs set (drafted with Claude; decisions mine) |
| Learning | **AI as tutor** | "Explain outbox failure modes until I can explain them back" |

## Tool map

| Tool | Used for |
|---|---|
| Claude Code (CLI/IDE) | In-repo implementation: multi-file changes, running tests, refactors |
| Claude Cowork | Planning, documentation, research, analysis — and, per v1.1, full implementation passes delivered as reviewable batches |
| Chat (Claude or equivalent) | Concept learning, rubber-ducking |
| AI code/security review | Second pair of eyes on hand-written code before PR merge |

Tool brands are interchangeable; the workflow is the point.

## Transparency artifacts (what an interviewer can inspect)

- This policy, committed at the repo root's `docs/`, with its change log below.
- `CLAUDE.md` — project context given to AI tools.
- PRs labeled `ai-assisted` where AI wrote a meaningful share; from 2026-07-17 that is most implementation PRs, and the label plus commit messages say so plainly.
- README section "How AI was used" linking here.
- ADRs — the human decision trail.

## Employer hygiene (extends GR-3/GR-4)

Personal AI accounts and personal hardware only. No employer code, data, documents, or non-public knowledge ever enters a prompt. No AI work on employer time or devices.

## The interview answer this policy backs up

> "This project is AI-built under my direction, and the repo says so out loud — including the moment I moved from line-by-line pre-merge review to letting AI build the full demo, which the policy's change log records honestly. I own the architecture — every ADR is my call. Quality is enforced by CI on every PR: full test suite including integration tests against real PostGIS, lint, scans. Once the build completed I studied the codebase end to end until I could rewrite any part of it, and I can walk you through any component right now. What you're seeing is the real trade-off modern teams face — velocity through AI with the human owning decisions, verification, and understanding — and the whole decision trail is public."

## Anti-patterns this policy forbids

Letting AI choose the architecture · presenting the code as understood before the study is actually done · hiding AI use · leading with "built with AI" instead of leading with the product · skipping verification (tests/CI) because AI wrote the code · letting AI touch credentials, billing, or out-of-Bicep cloud state.

## Change log

| Version | Date | Change |
|---|---|---|
| v1.0 | 2026-07-12 | Initial policy: novel-by-hand / repetition-by-AI split, merge bar, transparency artifacts. |
| v1.1 | 2026-07-17 | Principle 3 amended: AI drafts all implementation including novel cores, to reach a demoable product during application season. Compensating controls added: per-phase hand-rebuilt core, weekly AI-free rep retained, merge bar unchanged, interview answer rewritten to match reality. Division-of-labor table updated. |
| v1.2 | 2026-07-17 | Full AI implementation: [ME]/[AI] task split removed; AI executes all build work including former [ME] items. Pre-merge line review replaced by CI + AI self-review gate; Raziel's mastery moves to a structured post-completion code study (hand-rebuild and AI-free reps fold into it). Hard boundaries made explicit: credentials, billing, and account actions stay human; secrets never in repo or prompts; Azure only via Bicep. Goal: working demo with AI capabilities, then study. |
