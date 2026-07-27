# Deterioration aggregate fixture (FR-1.3)

Two cohorts, eight matrix rows, fourteen cells — hand-written, not generated.

The DuckDB job that computes real matrices is golden-tested separately, against the era fixtures and
against `src/tests/fixtures/deterioration/`. This fixture exists for the other half: the **loader and
the API**. It is written by hand *on purpose*, so that a bug in the job cannot produce an expectation
the API tests then agree with. The two halves check each other — the discipline FR-1.2 established in
`src/tests/fixtures/trends/README.md`.

## What it pins: the sample-size floor, on both sides

The published floor is **n ≥ 50** per matrix row (`manifest.json` carries it, the loader stamps it on
the run, the API applies it). Every other test would pass with an off-by-one in that comparison, so
the Deck cohort sits on all three sides of it at once:

| Component | Cohort | From-rating | Row total | Floor verdict |
|---|---|---|---|---|
| Deck | Girder / Stringer × Steel × Northeast | 5 | **51** | above — rates published |
| Deck | Girder / Stringer × Steel × Northeast | 7 | **50** | exactly at the floor — `>=`, so rates published |
| Deck | Girder / Stringer × Steel × Northeast | 6 | **49** | below — `sufficient: false`, every rate `null` |
| Culvert | Culvert × Concrete × Southeast | 6 | **10** | below — a second component, so a filter that ignores `component` shows up |

A `> 50` comparison instead of `>= 50` fails on from-rating 7; a `>= 49` fails on from-rating 6.

## The cells, and the rates they must produce

| From | To | Pairs | Expected rate |
|---|---|---|---|
| 5 | 4 | 1 | 2.0% |
| 5 | 5 | 50 | 98.0% |
| 6 | 5 | 4 | **null** (row below floor) |
| 6 | 6 | 45 | **null** (row below floor) |
| 7 | 6 | 10 | 20.0% |
| 7 | 7 | 40 | 80.0% |

Counts are always served, including for the suppressed row: the floor removes the *rate*, never the
evidence (methodology §4). Every row is returned zero-filled to ten to-ratings, so an observed zero
stays distinguishable from a cell the data never produced — from-rating 5 must come back with cells
`0,1,2,3,6,7,8,9` all at `0` pairs and `0.0%`.

Rows 0–4, 8 and 9 have no data at all. They are still returned, with `rowTotal: 0`,
`sufficient: false` and every rate `null` — an omitted row is indistinguishable from a rendering bug.

## The national sentinel

The `All / All / All` rows duplicate the cohort numbers exactly, because these two cohorts *are* the
whole fixture population per component. That makes the partition invariant checkable here too: summing
the non-sentinel cells per (component, from, to) must equal the sentinel cell. It also means a bug
that reads the sentinel when a cohort was asked for — or vice versa — cannot hide behind different
totals in the Deck family; the `Culvert` family is where the two differ in shape, since its only
cohort is `Culvert × Concrete × Southeast`.

Unchanged share: Deck 135 of 150 diagonal pairs = **90.0%**, Culvert 10 of 10 = **100.0%**. The API
computes the cadence caption from the matrix it is returning, so those are the numbers the caption
must carry.
