# Census join fixture — hand-placed bridge points (FR-1.5 AC-2)

`bridge_points.csv` is the bridge side of `tools/census/county-join.sql` in fixture mode. Every
coordinate was placed by hand against the real six-county TIGER fixture
(`tools/census/make-fixtures.sh` → `tools/census/convert.sh --fixtures`) and verified with
`ST_Within` / `ST_Intersects` / `ST_Distance_Sphere` before being committed. The county side is real
Census geometry; only the points are synthetic.

It exists because the real data cannot reach these branches. The national run has 741,131 points
against 3,235 counties, but a golden test needs a source small enough to hand-compute and shaped to
hit **every** outcome the SQL can produce — including the two that are vanishingly rare nationally (a
point exactly on a county line: 2 structures; a point with no county within the search radius: 4).

## The 16 rows and what each one pins

| # | Key | Point falls in | Outcome | Why the row exists |
|---|-----|----------------|---------|--------------------|
| 1–3 | `06/LA-000{1,2,3}/1` | 06037 Los Angeles | `agree` | The ordinary case, three times, so a single-row bug cannot pass. |
| 4–5 | `12/MD-000{1,2}/1` | 12086 Miami-Dade | `agree` | Florida geography from the **federal** TIGER file, never a state DOT source (GR-1). |
| 6 | `17/CK-0001/1` | 17031 Cook | `agree` | |
| 7 | `17/CK-0001/2` | 17031 Cook | `agree` | Same structure number, **record type 2** — a route *under* the structure. Separates the `structures` denominator (record type 1 only, what FR-1.2/FR-1.3 mean by "bridge") from the `bridges` denominator (every served row). Also proves the assignment key is the full natural key: keyed on `(state, structure_number)` alone this row would collide with #6. |
| 8 | `17/CK-0002/1` | 17031 Cook | `agree` | |
| 9 | `48/LV-0001/1` | 48301 Loving | `agree` | The smallest county in the country (population 33) — the one fixture county whose ACS margin of error is not null. |
| 10 | `72/AJ-0001/1` | 72001 Adjuntas | `agree` | Puerto Rico: in the ACS file, outside the published national total. |
| 11 | `48/LV-0002/1` county `389` | 48301 Loving | `different_county_same_state` | Published code 48389 (Reeves County, a real neighbour), coordinate in Loving. |
| 12 | `17/CK-0003/1` county blank | 17031 Cook | `county_not_published` | Item 3 blank. Pins that `nullif(trim(…),'')` runs *before* padding — `lpad('', 3, '0')` is `'000'`, itself a real published county code, so padding first would turn an absence into a claim. |
| 13 | `12/XS-0001/1` county `086` | 06037 Los Angeles | `different_state` | Published 12086, coordinate in California. The fourth cross-check kind. |
| 14 | `48/BD-0001/1` | — (touches 48301) | miss, `on_county_boundary` | An exact vertex of the Loving County ring. `ST_Within` false, `ST_Intersects` true — the boundary case the containment predicate deliberately quarantines instead of assigning. |
| 15 | `12/OF-0001/1` | — | miss, `outside_all_county_polygons` | Offshore east of Miami-Dade. Nearest county 12086 at **9,543 m** — the "1 km to 10 km" bucket. |
| 16 | `15/PC-0001/1` | — | miss, `outside_all_county_polygons` | Mid-Pacific. Nothing within the 2° search radius, so the nearest county and distance stay **null** rather than being fabricated. |

**No point is placed in 60010 Eastern District (American Samoa), deliberately.** It is the one
fixture county with a TIGER boundary and no ACS population row, and leaving it empty makes it carry
*both* absences a county report card has to render without inventing anything: no published
population, and no published structures. That also matches the real data — American Samoa has never
appeared in a published NBI vintage — so the fixture is not manufacturing a case the national run
does not have.

## Expected results — the numbers the golden tests assert

Coverage (`cj_coverage`):

| Field | Value |
|---|---|
| `bridges` | 16 |
| `matched` | 13 |
| `unmatched` | 3 |
| `structures` (record type 1) | 15 |
| `structures_matched` | 12 |
| `agree` | 10 |
| `different_county_same_state` | 1 |
| `different_state` | 1 |
| `county_not_published` | 1 |

Published relations: **6** counties — **1** without a population row and **1** with no structure
assigned to it, both of which are 60010; **3** misses; **3** disagreement pairs covering **3**
structures.

The sign-convention guard has margin: 12 of the 13 matched rows land in the state item 1 published
(row 13 is the deliberate `different_state` case), which is 92.3% against a 90% floor.

Miss distance buckets: one row each in `on a county boundary`, `1 km to 10 km`, and
`no county within the search radius`.

## Two limits of this fixture, stated so they are not mistaken for results

**The boundary case here touches one county, not two.** The six fixture counties are on four
different land masses and share no borders, so row 15 sits on a single polygon's ring. Nationally the
same reason code is produced by points on a *shared* line, where `ST_Intersects` returns two counties
and choosing between them is the thing the predicate refuses to do. The reason code and the code path
are identical; only `touching_counties` differs (1 here, 2 nationally).

**"Absent from the boundary file" is scoped to the boundary file supplied.**
`cj_diagnostic_retired_codes` reports published county codes the county source does not carry. Given
the national TIGER file that means a genuinely retired code — the eight Connecticut counties NBI item
3 still publishes after Census replaced them with nine planning regions. Given this six-county
fixture it also catches 48389 and 15003, which are perfectly current counties that simply are not in
the fixture. The view's meaning is exact either way; only the interpretation of *why* a code is
missing depends on the source, and the tests assert the count, not the interpretation.

## Regenerating

The points are committed, not generated — there is nothing to re-run. To rebuild the county side and
re-verify the whole fixture end to end:

```bash
tools/census/convert.sh --fixtures
tools/census/join-counties.sh --fixtures
```
