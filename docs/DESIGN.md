# SpanSight — Visual Design Spec

v0.1 · 2026-07-12 · Living reference: [`design/mockup.html`](./design/mockup.html) (open in a browser — real MapLibre basemap, working filters/drawer, sample data)

## Design principles

Quiet, government-adjacent professionalism: the map is the product, chrome stays out of the way. Condition color is the only loud element. Every screen answers "what's the state of these bridges?" in under 5 seconds (G-1).

## Design tokens

| Token | Value | Use |
|---|---|---|
| `--ink` | `#1a2733` | Primary text |
| `--steel` | `#2f4a63` | Brand, header |
| `--accent` | `#0e7490` | Links, actions, focus |
| `--bg` / `--panel` | `#f4f6f8` / `#fff` | App background / surfaces |
| `--line` / `--muted` | `#dde4ea` / `#5c6b7a` | Borders / secondary text |
| `--good` / `--fair` / `--poor` | `#2e7d32` / `#e6a817` / `#c62828` | Condition semantics (FHWA Good/Fair/Poor) |
| Type | system-ui stack, 14px base | No webfont cost |
| Radius / shadow | 10px / `0 1px 3px rgba(26,39,51,.12)` | Cards, drawer |

Condition colors are also encoded non-chromatically (labels + rating numbers) for color-blind users.

## Layout (Explorer, P0)

Header 52px (brand · tabs · search) → left filter rail 250px → map canvas with floating KPI cards (top) and legend (bottom-left) → right detail drawer 330px (off-canvas, slides on point click) → footer disclaimer strip (GR-6 text + basemap attribution, always visible).

## Screen inventory

| Screen | Phase | Status |
|---|---|---|
| Map Explorer | P0 | Mocked (this file) |
| Bridge detail drawer | P0 | Mocked |
| Data QA report | P0 | Table + reject-reason chart; reuse KPI card pattern |
| Analytics (trends, report cards) | P1 | Tab stubbed; sparkline placeholder in drawer shows direction |
| Live Ops (vehicles + geofence events) | P2 | Tab stubbed; adds live layer + event feed panel right side |

## Interaction notes

- Filters apply instantly (no Apply button); KPIs and map recompute together — one predicate, one source of truth.
- Drawer: click point → open; Esc or × → close; deep-linkable by bridge ID in the real build (`/bridge/{id}`).
- Disabled tabs advertise later phases instead of hiding them — the roadmap is part of the demo.
- States to design in React build: map loading skeleton, zero-results empty state (mockup shows KPI "–"), API-error toast, basemap-offline fallback (mocked).

## Accessibility (NFR-7)

Keyboard: all filter controls native inputs; drawer closable via Esc; focus ring uses `--accent`. Contrast: token pairs meet WCAG 2.1 AA on text; map canvas exempt. Screen readers: KPI cards get `aria-live="polite"` in the real build.

## Mockup → React mapping

`FilterRail`, `KpiStrip`, `BridgeMap` (MapLibre wrapper), `BridgeDrawer`, `AppShell` (header/footer). Sample-data predicate becomes the API query-string builder; MapLibre `setData` becomes tile-layer + filtered overlay strategy per ADR-002.
