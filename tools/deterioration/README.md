# Deterioration patterns — cohort condition-transition matrices (FR-1.3)

Turns the 34-vintage Parquet catalog into transition matrices: how often bridges in a cohort were
published at one NBI condition rating in a given year and another the next.

```bash
tools/deterioration/build-deterioration.sh              # the real 1992–2025 catalog → data/deterioration/
tools/deterioration/build-deterioration.sh --fixtures    # the committed era fixtures (what CI runs)
tools/deterioration/build-deterioration.sh --synthetic   # the hand-computed fixture (what CI checks the arithmetic with)
```

Then publish to a database (RUNBOOK §10.4):

```bash
dotnet run --project src/SpanSight.Ingestion -- load-deterioration --file data/deterioration
```

Requires `duckdb` and `python3`. The method is [`deterioration.sql`](./deterioration.sql) and
[`docs/METHODOLOGY-DETERIORATION.md`](../../docs/METHODOLOGY-DETERIORATION.md) — read those, not the
shell script; the script only points the SQL at a source, checks the result, and records it.

> **This is a description of published history at cohort level, not a prediction (GR-6).** No chain
> is iterated, no steady state is solved, no future year is projected, and no figure is ever computed
> for an individual structure. The SRS calls FR-1.3 a "Markov-chain baseline"; what exists is the
> empirical transition table and nothing done with it.

## What it produces

| Output | Rows (2026-07-26 run) | What it is |
|---|---|---|
| `matrix_row.parquet` / `.csv` | 6,313 | One row per component × cohort × from-rating: the pair count that is every rate's denominator, plus the span of years the evidence comes from |
| `matrix_cell.parquet` / `.csv` | 27,842 | One row per **non-zero** cell: pairs that moved from a row's rating to each to-rating |
| `manifest.json` | — | Run id, catalog SHA-256, methodology version, sample-size floor, counts, and the two published diagnostics |

Parquet is the analytical artefact; the CSV twins are what `load-deterioration` reads, so the loader
needs no Parquet library and no `duckdb` binary on the machine doing a cloud publish.

The national all-cohorts matrix lives in the *same* two relations, marked by the reserved value
`All` in all three cohort dimensions at once. One relation means the sample-size floor, the API DTO
and the sum invariant are each a single code path — the national matrix physically cannot bypass the
floor check.

## The 2026-07-26 full run

```
33 year-pairs 1992-2025  |  19,537,768 structure pairs  |  49,988,580 component pairs
303 cohorts  |  6,313 matrix rows (40 national)  |  27,842 non-zero cells
sample-size floor n >= 50: 3,227 of 6,273 cohort rows (51.44%) render "insufficient data" — 37,596 pairs
Culvert            4,220,610 pairs   93.12% unchanged    5.00% declined   1.88% improved
Deck              15,145,980 pairs   91.03% unchanged    6.19% declined   2.78% improved
Substructure      15,315,921 pairs   91.62% unchanged    5.78% declined   2.59% improved
Superstructure    15,306,069 pairs   92.00% unchanged    5.65% declined   2.35% improved
```

Which reconciles with FR-1.2 by construction: both jobs keep the same population — record type `1`,
one row per identity per vintage by the SOURCE_ROW tie-break — so both describe **20,649,259
structure-years over 1,039,109 structures**. The whole computation runs in about 30 seconds over the
1.4 GB Parquet set.

## The four decisions that move the numbers

### Per component, never combined

Four independent matrix families: deck (item 58), superstructure (59), substructure (60), culvert
(62). A pair needs the *same* component rated in both years.

The governing lowest-of-components rating is deliberately **not** used as a basis. A governing rating
can change merely because a different component became the minimum, which would conflate a component
switch with an actual rating movement. FR-1.2's trend view stays governing-based on purpose — the two
views answer different questions and each says which.

The families are also never summed: 30,485 structure-years publish both a numeric item 62 and a
numeric 58/59/60, so an "any component" matrix would double-count 22,967 pair units.

### Consecutive vintages only, and gaps stay gaps

A pair is `(y, y+1)`. A structure absent from either year contributes nothing for that year; nothing
is interpolated or carried forward. Rating **improvements are retained exactly as published** (2.51%
of pairs) — this is a record of published history, and censoring improvements would misstate it.

The rule has a visible consequence worth knowing: 1992→1993 yields no Alabama or Oklahoma pairs at
all, because both states reformatted item 8 (Alabama's structure numbers go from 15 characters to 6),
so every structure ends one series and starts another. That is identity churn (methodology §6.4), not
bridges disappearing, and it is why no re-linking is attempted in v1.

### Cohort attributes come from year *y*

The from-year is the one whose rating is the matrix row, so the cohort is a function of the same
record. Item 43A differs across a pair on 0.70% of structure-pairs and 43B on 0.70%; requiring the two
years to agree would drop ~244k component pairs, and counting the pair under both cohorts would
double it. Either breaks the invariant that the cohort matrices sum to the national matrix exactly.

Two labelled buckets exist so nothing is silently dropped:

- **"Not published"** — item 43A or 43B carried no code at all (7,316 and 9,499 component pairs, all
  with from-years 1992–2014). Never folded into "Other": FHWA publishing nothing is not FHWA
  publishing code 0.
- **"Outside contiguous U.S."** — implemented as the *else branch* of the nine-region climate map, not
  a fixed list, so a territory FHWA has not published yet cannot vanish. 414,679 pairs today
  (PR 191,539 · AK 121,392 · HI 101,117 · GU 332 · VI 299). A state code that resolves to nothing at
  all **fails the build**.

### All 33 year-pairs are pooled, and every row says what it covers

Per year-pair, 63 of 1,320 national rows fall below the floor with a minimum n of 2, so pooling is
what makes the feature exist at all. Pooling hides *when* the evidence was published, so every row
carries its own span — first and last from-year, and how many distinct year-pairs contributed.

That is not decoration. 19,927 deck pairs sit inside the **Culvert** type group, 99.1% of them with a
from-year of 2008 or earlier, and from-ratings 3–9 all clear the floor. Without the span those cells
would render a floor-clearing rate labelled 1992–2025 from what is effectively 1992–2008 coding
practice. They are not suppressed — that would violate "reported, never silently dropped" — so the
build prints every above-floor row whose evidence spans five year-pairs or fewer, and the UI shows the
span beside the n.

## The sample-size floor is a property of the row

`n >= 50` per matrix **row**, because the row total is the denominator of all ten of its rates. A row
below it publishes its counts and its span but **no rate at all** — never a number a reader could take
for a percentage.

The floor is defined once, in `det_methodology` inside the SQL, and travels outward: the build script
writes it into `manifest.json`, the loader stamps it onto the run row, and the API reads it from there
and applies it. Nothing downstream re-derives it, so the number the UI states is the number the
matrices were judged by.

At full scale it suppresses **3,227 of 6,273 cohort rows (51.4%)** — but those rows hold only 37,596
of 49,988,580 pairs (0.075%). Half the rows go dark; a thousandth of the evidence does.

Every from-rating row of every national family clears the floor in this build (smallest: culvert
from-rating 1, n = 358). That is a fact about this build, **not a guarantee** — it is false at fixture
scale (21 of 28 rows) and per-year-pair (63 of 1,320), which is exactly why no test asserts it and why
the national matrix goes through the same floor code path as every cohort.

## The diagonal dominates, and the caption says so

91.03% of deck pairs show no change, and 92.00% / 91.62% / 93.12% for superstructure, substructure and
culvert. NBI structures are typically re-inspected on a ~24-month cycle and intermediate annual files
carry the last inspection forward, so many "no change" observations reflect **no new inspection**
rather than measured stability — off-diagonal rates therefore understate true annual change.

This is the dominant feature of every matrix, not a footnote, so the API computes each matrix's own
unchanged share and ships it in the caption, and the UI renders the caption next to the grid. It is
also why the view does not put the diagonal and the off-diagonal on one colour scale: at 91–93% that
draws one bright stripe and nine dark rows, which communicates nothing.

Segmenting on item 90 (date of inspection) to build a "new inspection only" variant is recorded as
future work in the methodology, not quietly omitted.

## How the SQL is kept honest

The cohort rule now exists three times: in `deterioration.sql`, in
`SpanSight.Core.Domain.Lookups.NbiCohorts` (which the API and UI label with), and in
`web/src/state/filters.ts` (which the filter rail uses). Two of those had already drifted apart with
nothing but a comment holding them together, so FR-1.3 made it a test:

- `DeteriorationGoldenTests` executes the job's own lookup relations and compares them to the C# on
  **every** published code — all 10 item-43A codes, all 23 item-43B codes and all 56 state codes — in
  both directions. `NbiCohortsTests` then pins the C# against the TypeScript.
- The same suite runs the published SQL over the hand-written synthetic fixture and asserts all 30
  cohort cells individually (`src/tests/fixtures/deterioration/README.md` states each one), plus the
  structures that must contribute *nothing*: a gap year, a component that changed families, a
  record-type-`2` route, and a structure published once.
- Mutation-verified 2026-07-26: moving Missouri from Ohio Valley to South, moving design code 22 out
  of Girder / Stringer, reversing the duplicate-key tie-break, and keying the cohort on `y+1` each
  fail the suite.

`build-deterioration.sh` additionally refuses to write anything unless seven invariants hold: no pair
falls into an unmapped cohort, every pair is consecutive with both ratings 0–9, every row total equals
the cells beneath it, the cohort matrices sum to the national matrix cell by cell, the national
sentinel is all-or-nothing, no published group is spelled `All`, and every row's span can describe its
own pairs. Each check returns the *violating rows*, so a failure names the cohort rather than just
disagreeing.

## Storage

6.9 MB in the serving database — 5.6 MB of cells and 1.3 MB of rows, against 228 MB for the FR-1.2
per-bridge series. Cells are stored **sparse** (non-zero only): a cohort-component populates 12 of its
100 cells at the median, and "never observed across 34 years" is a real published fact worth keeping
distinct from an observed zero. The API zero-fills to a full 10×10 grid on read, so a reader still
sees ten columns and can tell the two apart.

Rates are not stored. A rate is a division of two stored numbers, and storing it would only create
something that can disagree with them.

EXPLAIN at full scale (2026-07-26, local PostGIS, 6,313 rows / 27,842 cells): one cohort's rows
**0.102 ms**, its cells **0.054 ms**, the national matrix **0.045 ms** — all index scans on the
`(component, type_group, material_group, region, from_rating)` unique index. The cohort catalog
aggregate is a 3.1 ms hash aggregate over the whole 82-page row table, which is cheaper than any index
could make a full group-by. Against NFR-1's 300 ms p95, with room to spare.
