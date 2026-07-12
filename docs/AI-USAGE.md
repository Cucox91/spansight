# SpanSight — AI Usage Policy

v1.0 · 2026-07-12 · Author: Raziel Arias · **This file ships with the repo on purpose** — it is part of the portfolio, not metadata about it.

## Why this document exists

This is a portfolio project: its job is to prove *my* engineering ability. AI is used throughout — openly, under the rules below — because a documented, disciplined AI workflow is itself a skill hiring teams screen for. The failure modes this policy prevents: code I can't defend in an interview, and an unrealistic "no AI" posture.

## Principles

1. **Engineer of record.** Every architectural decision is mine. AI is a sparring partner; ADRs record my calls and my reasoning.
2. **The merge bar.** I review every AI-generated line before commit. *If I couldn't rewrite it, I don't merge it.* Every line in this repo is one I can explain on a whiteboard.
3. **Novel by hand, repetition by AI.** The first implementation of anything new to me is hand-written; AI accelerates the copies.
4. **Both directions.** AI also reviews *my* hand-written code (code review, security review). Assistance and verification flow both ways.
5. **Stay interview-ready.** One AI-free rep per week (kata or small feature, coded cold) — live-coding rounds don't allow assistants.

## Division of labor

| Work | Who | Examples |
|---|---|---|
| Architecture, ADRs, trade-off decisions | **Me** (AI as debate partner) | Postgres vs MSSQL (ADR-001), hosting (ADR-006-B) |
| First-of-a-kind implementations | **Me**, AI reviews after | First EF Core spatial mapping, first endpoint, outbox core, SNBI crosswalk mapper, first React+MapLibre component |
| Test cases for core logic | **Me** (choosing assertions = understanding) | Crosswalk mapping rules, geofence math, quarantine rules |
| Nth-of-a-kind code | **AI**, I review line-by-line | Additional endpoints, DTOs, EF configurations |
| Scaffolding & boilerplate | **AI**, I review | Test harnesses, Bicep templates, CI YAML, CSS polish, one-off data scripts |
| Docs drafting, research, analysis | **AI**, I direct and approve | This docs set (drafted with Claude in Cowork; decisions mine) |
| Learning | **AI as tutor** | "Explain outbox failure modes until I can explain them back" |

## Tool map

| Tool | Used for |
|---|---|
| Claude Code (CLI/IDE) | In-repo implementation: multi-file changes, running tests, refactors |
| Claude Cowork | Planning, documentation, research, analysis (the entire design phase) |
| Chat (Claude or equivalent) | Concept learning, rubber-ducking |
| AI code/security review | Second pair of eyes on hand-written code before PR merge |

Tool brands are interchangeable; the workflow is the point.

## Transparency artifacts (what an interviewer can inspect)

- This policy, committed at the repo root's `docs/`.
- `CLAUDE.md` — project context given to AI tools.
- PRs labeled `ai-assisted` where AI wrote a meaningful share; commit messages note the split when relevant.
- README section "How AI was used" linking here.
- ADRs — the human decision trail.

## Employer hygiene (extends GR-3/GR-4)

Personal AI accounts and personal hardware only. No employer code, data, documents, or non-public knowledge ever enters a prompt. No AI work on employer time or devices.

## The interview answer this policy backs up

> "I use AI like a senior pair. I own the architecture and hand-write the first implementation of anything novel; I delegate repetition; nothing merges that I can't explain or rewrite. It's documented — the policy, labeled PRs, and the ADRs are in the repo. Even my requirements and hosting decisions were debated with AI and recorded as ADRs, because that's the workflow teams actually run now."

## Anti-patterns this policy forbids

Letting AI choose the architecture · merging code I can't whiteboard · hiding AI use · leading with "built with AI" instead of leading with the product · skipping the weekly no-AI rep while actively interviewing.
