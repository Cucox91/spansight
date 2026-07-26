# Condition trends — per-bridge history and county/state rollups (FR-1.2)

Turns the 34-vintage Parquet catalog into the two aggregates the site serves: a condition series per
structure, and Good/Fair/Poor counts per county and state per year.

```bash
tools/trends/build-trends.sh              # the real 1992–2025 catalog → data/trends/
tools/trends/build-trends.sh --fixtures   # the committed era fixtures (what CI runs)
```

Then publish to a database (RUNBOOK §10.3):

```bash
dotnet run --project src/SpanSight.Ingestion -- load-trends --file data/trends
```

Requires `duckdb` and `python3`. The method is [`trends.sql`](./trends.sql) — read that file, not
the shell script; the script only points the SQL at a source, checks the result, and records it.

**Everything here is a descriptive statistic of published ratings (GR-6).** No value is predicted,
smoothed, or filled in. A year FHWA did not publish a structure is a gap and stays a gap.

## What it produces

| Output | Rows (2026-07-26 run) | What it is |
|---|---|---|
| `bridge_series.parquet` / `.csv` | 1,039,109 | One row per structure: first/last year and a packed per-year rating string |
| `rollup.parquet` / `.csv` | 111,335 | County and state × year Good/Fair/Poor/Unrated counts |
| `manifest.json` | — | Run id, catalog SHA-256, per-year row accounting |

Parquet is the analytical artefact; the CSV twins are what `load-trends` reads, so the loader needs
no Parquet library and no `duckdb` binary on the machine doing a cloud publish. Both are written
from the same statement.

The packed series looks like this — one character per year from `first_year` to `last_year`:

```
01 | 000002 | 2010 | 2025 | 4 | 4.....44.......4
                                ↑     ↑↑       ↑
                             2010  2016 2017  2025
```

`0`–`9` is the lowest published rating that year · `U` means published with no numeric rating ·
`.` means not published that year. `SpanSight.Core.Analytics.ConditionSeriesCodec` is the only thing
that reads it, and the API expands it back into years before anything sees it.

## The two decisions that move the numbers

### Record type `1` only — this removes a 15% cliff that is not real

Item 5A distinguishes the route carried **on** the structure (`1`) from routes passing under or
alongside it (`2`, and `A`–`Z` for further routes). FHWA published all 28 record types through 2009
and only type `1` from 2010, so the raw row count falls off a cliff at the boundary:

| | 2009 | 2010 | change |
|---|---|---|---|
| All rows as published | 713,115 | 604,493 | **−15.2%** |
| Record type `1` only | 608,419 | 604,493 | −0.6% |

A trend built on raw rows would show 108,622 bridges vanishing in one year. Nothing vanished — the
export changed. The filter is what makes the series continuous across 2009/2010.

It is also correct on the merits, not just convenient. Non-type-1 records describe a *route*, not
the structure, and carry no structure condition: of the 18,036 rows in 1992 belonging to structures
with no type-1 record, **exactly 0** have a numeric rating in items 58/59/60/62. Keeping them would
double-count structures and manufacture "Unrated" observations from records that never claimed to
report condition.

### One row per structure per year, first-published wins

`(state, structure number)` is not quite unique even within type `1` — the published files contain
genuine collisions, two different structures sharing a number in one state (954 keys in 1995, 2 in
2025; e.g. VA `000000000001826` appears in county 093 and county 015). The job keeps whichever row
came first in the published file, which is reproducible from the source alone, and reports the
count per year in `manifest.json` rather than hiding it. Across all 34 vintages this drops **2,858**
rows.

### Gaps are gaps

16,641 structures are absent from at least one vintage inside their own span. Those years get `.`
and are omitted from the API response entirely. Nothing is carried forward.

## The 2026-07-26 full run

```
34 vintages 1992-2025  |  1,039,109 structures  |  20,649,259 observations
111,335 rollup rows (3,553 counties, 54 states)
16,641 structures have at least one gap year
excluded: 1,655,245 non-type-1 rows, 2,858 duplicate-key rows
```

Which reconciles exactly against the vintage catalog:

```
22,307,362 converted rows  −  1,655,245 non-type-1  −  2,858 duplicate-key  =  20,649,259
```

1,039,109 structures is larger than the 624,193 in the 2025 file because the series spans 34 years
and includes every structure that has since been retired, replaced or renumbered.

## How the SQL is kept honest

The Good/Fair/Poor rule exists twice: in `SpanSight.Core.Domain.ConditionClassifier`, which the API
and map use, and in `trends.sql`, which produces the published history. Two implementations of one
rule is a standing drift risk, so `ConditionTrendGoldenTests` runs **this exact SQL file** over the
era fixtures and compares it to the C# classifier row by row — all 1,500 fixture rows, both the
lowest rating and the class. CI runs it on every PR. Changing `>= 7` to `>= 8` in the SQL fails it.

Four structures are additionally pinned by hand, each for a different reason:

| Structure | Series | What it pins |
|---|---|---|
| `01`/`000002` | `4.....44.......4` | Deck falls 8→7→6 but superstructure sits at 4 — the *lowest* component governs, so the class never moves |
| `01`/`000005` | `5.....44.......4` | A genuine Fair → Poor transition |
| `01`/`000019` | `5.....55.......5` | A culvert: items 58/59/60 are `N` and item 62 governs — `N` is not zero |
| `01`/`0000000011070Z0` | `U` | All four items `N`: one Unrated observation, not a gap and not a Poor |

`build-trends.sh` additionally refuses to write anything unless five invariants hold: the state
rollup equals the rows it summarises, the class counts partition each total, county totals fit
inside their state, every packed series matches the span it claims, and expanding the series back
out reproduces the rollup counts. Each check returns the *violating rows*, so a failure names the
year or structure.

## Storage: why one packed row and not 34 skinny ones

Both were built and measured against the full national set:

| | Rows | Total size | Heap | Indexes |
|---|---|---|---|---|
| Packed, one row per structure | 1,039,109 | **228 MB** | 92 MB | 136 MB |
| Skinny, one row per observation | 20,640,984 | 1,737 MB | 986 MB | 751 MB |

7.6× smaller. The serving instance is a B1ms with 2 GB of RAM and a $50/month ceiling on the whole
subscription (NFR-2), and the skinny table alone exceeds that memory before `core.bridge` is even
considered.

The cost of the choice, stated plainly: the packed column **cannot be aggregated in SQL**. That is
exactly why the county and state rollups are stored relationally beside it — anything that needs to
count across bridges reads those. The packed column is only ever read one whole bridge at a time,
by the drawer, which is a single index seek either way.
