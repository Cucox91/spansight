# Synthetic transition fixture (FR-1.3 AC-3)

Fourteen structures, three vintages, thirty rows — hand-written, and every expected matrix cell in
this file was computed by hand *before* the job was run against it.

`synthetic_vintages.csv` is read directly as the job's `nbi_source`:

```bash
tools/deterioration/build-deterioration.sh --synthetic
```

That works because `tools/deterioration/deterioration.sql` names every column it reads and never does
`SELECT *` on the source, so eleven columns are enough to stand in for the 150-column vintage Parquet.

## Why this exists as well as the era fixtures

FR-1.2 established a two-halves discipline: the *published* SQL is golden-tested against the committed
era fixtures, and a separate hand-written fixture checks the loader and the API, so a bug in the job
cannot produce an expectation the API tests then agree with.

FR-1.3 needs a third thing, because the era fixtures structurally cannot reach it. They are the first
300 rows of five real vintages — **all Alabama**, all record type `1`, 300 distinct identity keys each,
and only one consecutive year-pair (2016→2017). So they can exercise exactly one of the ten climate
buckets, cannot reach material group "Other" (codes 0 and 9 never appear), and make both the
record-type filter and the duplicate-key tie-break no-ops. This file covers what they cannot, at a size
where the whole matrix is checkable by eye.

Every row total here is **1 or 2**, far below the n ≥ 50 sample-size floor. That is deliberate: this
fixture pins the *arithmetic*, and the floor's two branches are pinned elsewhere — by the era fixtures
(exactly one cohort row clears it) and by `src/tests/fixtures/deterioration-aggregates/`, which sits at
49, 50 and 51 on purpose.

## What each structure pins

| Structure | State | Vintages | Pins |
|---|---|---|---|
| `S01` | NY | 2000, 2001, 2002 | An ordinary three-year history: two consecutive pairs from one structure, and components that move independently (deck 7→7→6, superstructure 7→6→6, substructure flat at 7). |
| `S02` | NY | 2000, 2001 | An **improvement retained as observed** — deck 4→8 after a rehabilitation (§3.3). Censoring it would misstate published history. |
| `S03` | NY | 2000, **2002** | A **gap**. Published in 2000 and 2002, absent from 2001. Contributes **no pair at all**: gaps are never bridged (§3.1). |
| `S04` | NY | 2000, 2001 | A **family flip** — a culvert record in 2000 (58/59/60 = `N`, item 62 = 6) recoded in 2001 (deck/super/sub rated, item 62 = `N`). Contributes **nothing to any family**, because a pair needs the *same* component rated in both years (§3.2). A governing-rating implementation would invent a 6→7 observation here. |
| `S05` | CA | 2000, 2001 | A second **climate region** (West) — the era fixtures only reach Southeast. |
| `S06` | HI | 2000, 2001 | The **"Outside contiguous U.S."** bucket, reached through the else branch of the region map rather than a hard-coded territory list (§5). |
| `S07` | TX | 2000, 2001 | Material group **"Other" via code 9** (Aluminum, wrought iron or cast iron) and region South. |
| `S08` | TX | 2000, 2001 | Material group **"Other" via code 0** — with `S07`, proves codes 0 and 9 land in the same group. Type group differs, so they stay separate cohorts. |
| `S09` | NY | 2000, 2001 | Items 43A **and** 43B blank → the labelled **"Not published"** bucket in both dimensions. Reported, never dropped and never folded into "Other" (§5). |
| `S10` | NY | 2000, 2001 | **Record type `2` only.** Contributes nothing. Its ratings are 9s in a cohort `S01` also populates, so deleting `WHERE RECORD_TYPE_005A = '1'` shows up as a spurious 9→9 cell (§2). |
| `S11` | CA | 2000, 2001 | A **duplicate identity key** — two type-`1` rows per year with the same (state, structure number) and different ratings. The row published first wins, so the pair is 5→4; reversing the tie-break gives 9→8 and keeping both gives four pairs instead of three (§2). |
| `S12` | NY | 2000, 2001 | **Cohort attributes change across the pair** (Steel/Truss→Concrete/Girder). The pair is counted **once**, under year `y` — the from-year whose rating is the matrix row (§5). Keying on `y+1` moves it to a different cohort. |
| `S13` | NY | 2001 only | A structure with a single published year: no pair, and no crash. |
| `S14` | NY | 2000, 2001 | Ratings `N`, lowercase `n`, and blank alongside a rated item 62. Only a culvert pair results — none of the three is coerced to a number (§2 "never imputed"). |

## The expected output, in full

**31 component pairs** over **11 structure pairs**, in **8 cohorts**, across 2 year-pairs (2000→2001,
2001→2002), spanning 2000–2002. By family: Deck 10, Superstructure 10, Substructure 10, Culvert 1.

### Cohort matrices — every non-zero cell

| Cohort (type × material × region) | Component | Move | Pairs |
|---|---|---|---|
| Girder / Stringer × Concrete × Northeast | Deck | 7→7 | 1 |
| Girder / Stringer × Concrete × Northeast | Deck | 7→6 | 1 |
| Girder / Stringer × Concrete × Northeast | Superstructure | 7→6 | 1 |
| Girder / Stringer × Concrete × Northeast | Superstructure | 6→6 | 1 |
| Girder / Stringer × Concrete × Northeast | Substructure | 7→7 | 2 |
| Truss / Arch × Steel × Northeast | Deck | 4→8 | 1 |
| Truss / Arch × Steel × Northeast | Deck | 6→6 | 1 |
| Truss / Arch × Steel × Northeast | Superstructure | 5→5 | 1 |
| Truss / Arch × Steel × Northeast | Superstructure | 6→6 | 1 |
| Truss / Arch × Steel × Northeast | Substructure | 5→5 | 1 |
| Truss / Arch × Steel × Northeast | Substructure | 6→6 | 1 |
| Girder / Stringer × Prestressed concrete × West | Deck | 6→5 | 1 |
| Girder / Stringer × Prestressed concrete × West | Deck | 5→4 | 1 |
| Girder / Stringer × Prestressed concrete × West | Superstructure | 6→6 | 1 |
| Girder / Stringer × Prestressed concrete × West | Superstructure | 5→4 | 1 |
| Girder / Stringer × Prestressed concrete × West | Substructure | 6→6 | 1 |
| Girder / Stringer × Prestressed concrete × West | Substructure | 5→4 | 1 |
| Other × Timber × Outside contiguous U.S. | Deck / Super / Sub | 7→7 | 1 each |
| Truss / Arch × Other × South | Deck | 5→5 | 1 |
| Truss / Arch × Other × South | Superstructure | 5→4 | 1 |
| Truss / Arch × Other × South | Substructure | 4→4 | 1 |
| Other × Other × South | Deck | 3→2 | 1 |
| Other × Other × South | Superstructure | 3→3 | 1 |
| Other × Other × South | Substructure | 3→3 | 1 |
| Not published × Not published × Northeast | Deck / Super / Sub | 6→6 | 1 each |
| Culvert × Concrete × Northeast | Culvert | 6→6 | 1 |

29 cohort matrix rows, 30 non-zero cohort cells.

### The national context matrix — the exact sum of the cohorts

| Component | Move | Pairs | | Component | Move | Pairs |
|---|---|---|---|---|---|---|
| Deck | 3→2 | 1 | | Substructure | 3→3 | 1 |
| Deck | 4→8 | 1 | | Substructure | 4→4 | 1 |
| Deck | 5→4 | 1 | | Substructure | 5→4 | 1 |
| Deck | 5→5 | 1 | | Substructure | 5→5 | 1 |
| Deck | 6→5 | 1 | | Substructure | 6→6 | 3 |
| Deck | 6→6 | 2 | | Substructure | 7→7 | 3 |
| Deck | 7→6 | 1 | | Superstructure | 3→3 | 1 |
| Deck | 7→7 | 2 | | Superstructure | 5→4 | 2 |
| Culvert | 6→6 | 1 | | Superstructure | 5→5 | 1 |
| | | | | Superstructure | 6→6 | 4 |
| | | | | Superstructure | 7→6 | 1 |
| | | | | Superstructure | 7→7 | 1 |

15 national matrix rows, 21 non-zero national cells. Deck 10 + Superstructure 10 + Substructure 10 +
Culvert 1 = **31**, matching the cohort total pair for pair — which is `det_check_national_is_the_sum`.

Totals: **44 matrix rows** (29 cohort + 15 national) and **51 non-zero cells** (30 + 21).

### Spans worth noting

Only `S01` spans two year-pairs, so exactly two cohort rows carry `year_pairs_observed = 2`
(Girder / Stringer × Concrete × Northeast, deck from-rating 7 and substructure from-rating 7); every
other row is a single year-pair. Superstructure in that cohort splits into two rows —
from-rating 7 in 2000 and from-rating 6 in 2001 — which is what makes the row-level span meaningful
rather than a property of the cohort as a whole.
