# County join aggregates — the loader's input fixture (FR-1.5)

The three CSVs and the manifest `tools/census/join-counties.sh --fixtures` produces from the 16
hand-placed points in `../census-join/`. Committed because the loader and the API integration suite
need a published join without a DuckDB run, and because Parquet cannot enter git (`.gitignore`).

`runId` is pinned to `county-join-fixture` and `source` rewritten to the points file, so the fixture
is byte-stable: the job stamps a wall-clock run id, and re-running it must not churn a committed
file. Everything else is exactly what the job wrote.

## What it contains

| File | Rows | Notes |
|---|---|---|
| `county.csv` | 6 | The census fixture counties. `60010` has no population, no MOE and no ACS vintage; `48301` is the one row with a published margin of error. |
| `miss.csv` | 3 | One `on_county_boundary`, two `outside_all_county_polygons` — one of those with a null nearest county, because nothing lay inside the search radius. |
| `disagreement.csv` | 3 | One pair per non-agreeing kind. The `county_not_published` row has an empty `nbi_county_fips` **and** an empty `nbi_fips_in_tiger`: null, not false, because with no published code the question does not apply. |

## Coverage the manifest declares

16 bridges · 13 matched · 3 unmatched · 15 record-type-1 structures · 12 of those matched ·
10 agree with item 3 · 1 each of `different_county_same_state`, `different_state`,
`county_not_published` · 6 counties, 1 without a population row.

The loader re-checks each row count against this manifest and refuses a mismatch, and separately
refuses a `miss.csv` that does not account for every unmatched bridge — so editing one file here
without the others fails the load rather than publishing a coverage figure that hides structures.

## Regenerating

```bash
tools/census/convert.sh --fixtures
tools/census/join-counties.sh --fixtures
cp data/census/join/fixtures-out/{county,miss,disagreement}.csv \
   data/census/join/fixtures-out/manifest.json src/tests/fixtures/census-join-aggregates/
```

Then re-pin `runId` to `county-join-fixture` and `source` to
`src/tests/fixtures/census-join/bridge_points.csv` in the copied manifest.
