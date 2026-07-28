# Keyboard walkthrough — the NFR-7 pass, written down

Notes for the demo script (`[CW]`, IMPLEMENTATION-PLAN §10 W1) and for the NFR-7 verification the
SRS asks for: *"keyboard walkthrough in the demo script"* (§6). Written during the P1-W6 hardening
pass, 2026-07-28, against the app as it stands on that date.

This is the tab order as it actually is, not as it should be. Where a stop is awkward, that is
recorded rather than smoothed over — a demo that narrates a path nobody can walk is worse than no
demo.

## What the automated scan does and does not cover

`web/e2e/smoke.spec.ts` runs axe over seven route/state combinations at 1280px and seven more at
375px, and fails the build on any **serious or critical** violation. That is a real gate, and it is
not a keyboard walkthrough. axe cannot tell you that the tab order is sensible, that focus goes
somewhere useful after a route change, or that a region you can reach is a region you can
understand. Those are the paragraphs below.

Two things the scan explicitly excludes:

- **the map canvas** (`.maplibregl-map`), per NFR-7's "map canvas exempt where impractical";
- **`page-has-heading-one`**, which is *moderate* and therefore under the failure bar. It fires on
  all seven routes — the app has no `<h1>` anywhere; pages start at `<h2>` and the header's
  "SpanSight" is a styled `<div>`. Worth fixing, but it is a decision about which element becomes
  the document heading on each route, so it is recorded here rather than changed in a hardening PR.

## The shared prefix — paid on every route

From a fresh load, before any page content:

| Tab | Lands on |
|---:|---|
| 1 | **Explorer** |
| 2 | **Trends** |
| 3 | **Patterns** |
| 4 | **Rankings** |
| — | *Live Ops (Phase 2) is `disabled` and is skipped* |
| 5 | **Data QA** |
| 6 | the **Ask the map** textbox |
| — | *its submit button is disabled until something is typed, and is skipped* |
| 7 | the first control in `<main>` |

**There is no skip link.** Six stops before the content, on every route, every time. For the demo,
say so plainly and move on; for the backlog, a skip link is the single highest-value keyboard
change left in the app.

At a phone width the header wraps into three rows (fixed in P1-W6 — it used to lay the last three
controls out past the right edge of its own background, where they were white on white and
unreachable). The tab order is unchanged by the wrap.

## `/trends?level=state&fips=12`

7 Level · 8 State · 9 From · 10 To · 11 **the whole chart** · 12 footer link.

Stop 11 is the point worth narrating: `.trend-result` is one tab stop with nothing focusable inside
it, so a keyboard reader lands on the region and then uses ↓/↑ to read the 34 year rows. The region
is named "Condition over time for Florida", so a screen reader announces what was landed on before
the numbers start.

Note stop 8 changes shape: it is a `<select>` at `level=state` when `/api/lookups` answered, and a
text input at `level=county` or when the lookup failed. Do not script keystrokes that assume the
select.

## `/patterns?component=Deck&…`

7 Component · 8 Cohort · 9 **the matrix scroller** · 10 the methodology link · 11 footer link.

Stop 9 is one tab stop for the entire 10×10 grid; ←/→ scroll its columns. The outer
`.matrix-result` region is *not* a stop — only the inner group is, which is the right call: two
nested stops for one table would double the cost of skipping it.

Stop 10 opens in a new tab (`target="_blank"`) and the link text does not say so. Narrate it, and
put it on the backlog.

## `/rankings?groupBy=county&limit=10`

7 View · 8 Group by · 9 State · 10 Show · 11 **the result region** · 12–21 **one link per row** ·
22 Download CSV · 23 footer link.

Two things to know before scripting this one:

- **Stop 8 disappears** when View is set to "High traffic, Poor condition" — the grouping control is
  removed from the DOM, and the tab ring shortens by one underneath the narrator.
- **Stops 12–21 are the rows.** At `limit=10` that is ten stops; at 100 it is a hundred, with no way
  to bypass them and no way to reach the CSV link except through all of them. Demo at `limit=10`.
  A skip mechanism past a long ranking belongs on the backlog with the skip link.

Only the *county* ranking has row links. The state and cohort rankings have none — which is why
their scrolling table needed an explicit tab stop (P1-W6): with nothing focusable inside, a
horizontally scrolling region was unreachable by keyboard at narrow widths. It now has one, named
"Ranked groups".

## `/county/12086`

7 **the page region** ("Report card for Miami-Dade County") · 8 Show on the map · 9 Download as CSV ·
10 See this series as a chart *(only when a trend exists)* · 11 **condition table** ·
12 **history table** · 13 footer link.

Stops 11 and 12 are new in P1-W6. Neither table holds a link, so before the fix both scrolled with
no keyboard access at all on a narrow screen. They are named "Condition of this county's structures"
and "Condition over time for this county".

Stop 10 is conditional: an empty or retired-code county does not render it, so the ring is 12 long
there rather than 13. `/county/09001` — a Connecticut code NBI still publishes and TIGER no longer
carries — is the state to demo that with.

## `/` (the explorer) and the drawer

The filter rail is a normal sequence of labelled checkboxes and selects; the map canvas is exempt.
The drawer is opened by clicking a bridge — **there is no keyboard path onto the map to open it**,
which is the honest limit of the exempt-canvas position. The deep link is the keyboard route:
`/bridge/{state}/{structureNumber}` renders the same drawer directly, and Esc closes it (asserted in
the suite). Demo it that way.

## Backlog, in the order worth doing

1. A skip link past the six-stop header prefix.
2. An `<h1>` on every route (`page-has-heading-one`, all seven).
3. A bypass past a long ranking's row links.
4. "opens in a new tab" in the methodology link's accessible name.
5. A no-match route: an unknown path currently renders header, nav, footer and an empty `<main>` —
   no heading, no message. Static Web Apps rewrites every path to `index.html`, so it is reachable
   in production.
