# SpanSight — AI Usage Policy

v1.1 · amended 2026-07-17 (v1.0: 2026-07-12) · Author: Raziel Arias · **This file ships with the repo on purpose** — it is part of the portfolio, not metadata about it.

## Why this document exists

This is a portfolio project: its job is to prove *my* engineering ability. AI is used throughout — openly, under the rules below — because a documented, disciplined AI workflow is itself a skill hiring teams screen for. The failure modes this policy prevents: code I can't defend in an interview, and an unrealistic "no AI" posture.

## Principles

1. **Engineer of record.** Every architectural decision is mine. AI is a sparring partner; ADRs record my calls and my reasoning.
2. **The merge bar.** I review every AI-generated line before commit. *If I couldn't rewrite it, I don't merge it.* Every line in this repo is one I can explain on a whiteboard.
3. **AI implements, I master before merge** *(amended 2026-07-17; v1.0 read "novel by hand, repetition by AI")*. AI writes first drafts across the codebase — including first-of-a-kind cores — to hit demo-readiness during application season. Ownership moved from authorship to mastery: I trace every code path, run and step through it, and hold it to the merge bar. To keep that mastery honest, **each phase I pick at least one core component and rebuild it by hand from a blank file** (Phase 0 pick: the NBI parser or the DMS converter), diffing my rebuild against the merged version.
4. **Both directions.** AI also reviews *my* hand-written code (code review, security review). Assistance and verification flow both ways.
5. **Stay interview-ready.** One AI-free rep per week (kata or small feature, coded cold) — live-coding rounds don't allow assistants. The phase-core rebuild in principle 3 counts toward this.

## Division of labor (v1.1)

| Work | Who | Examples |
|---|---|---|
| Architecture, ADRs, trade-off decisions | **Me** (AI as debate partner) | Postgres vs MSSQL (ADR-001), hosting (ADR-006-B), AI features (ADR-008) |
| Implementation — all components, novel cores included | **AI drafts**, I review line-by-line to the merge bar | NBI parser, DMS converter, schema, endpoints, React/MapLibre components |
| Mastery reps | **Me** | One hand-rebuilt core per phase + weekly AI-free kata |
| Test design for core logic | **AI proposes**, I approve/extend the assertions | Crosswalk rules, quarantine rules, geofence math |
| Scaffolding & boilerplate | **AI**, I review | Test harnesses, Bicep templates, CI YAML, CSS polish, one-off data scripts |
| Docs drafting, research, analysis | **AI**, I direct and approve | This docs set (drafted with Claude in Cowork; decisions mine) |
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

> "This project is AI-built under my direction, and the repo says so out loud. I own the architecture — every ADR is my call — and every line merged passed my review with one bar: if I can't rewrite it, it doesn't merge. To keep that claim honest I rebuild one core component per phase by hand and do a weekly no-AI rep, because live-coding rounds don't allow assistants. That's the workflow modern teams actually run, and I can show you the policy, the labeled PRs, and the decision trail."

## Anti-patterns this policy forbids

Letting AI choose the architecture · merging code I can't whiteboard · hiding AI use · leading with "built with AI" instead of leading with the product · skipping the weekly no-AI rep or the phase-core rebuild while actively interviewing.

## Change log

| Version | Date | Change |
|---|---|---|
| v1.0 | 2026-07-12 | Initial policy: novel-by-hand / repetition-by-AI split, merge bar, transparency artifacts. |
| v1.1 | 2026-07-17 | Principle 3 amended: AI drafts all implementation including novel cores, to reach a demoable product during application season. Compensating controls added: per-phase hand-rebuilt core, weekly AI-free rep retained, merge bar unchanged, interview answer rewritten to match reality. Division-of-labor table updated. |
