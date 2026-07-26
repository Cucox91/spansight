# NBI vintage pipeline (FR-1.1)

Converts the FHWA annual National Bridge Inventory ASCII files (1992–2025) into one normalized
Parquet file per vintage, so DuckDB can read the whole history as a single relation. The historical
set never enters the serving Postgres — that stays small and cheap (ADR-005, NFR-2).

```
tools/vintages/download.sh            # fetch vintages into data/vintages/raw/ (gitignored)
tools/vintages/convert.sh             # normalize every downloaded vintage → Parquet
tools/vintages/convert.sh 1992 2010   # …or just these years
tools/vintages/convert.sh --fixtures  # the committed 300-row era fixtures (what CI runs)
```

Requires `dotnet`, `duckdb` (`brew install duckdb`) and `python3`.

## Shape

`download.sh` → `dotnet run -- convert-vintage` → `duckdb` → Parquet + `catalog.json`.

The .NET CLI does the parse and normalization, so every era quirk below lives in tested C#
(`SpanSight.Core/Vintages/`) rather than in a shell pipeline; DuckDB does the columnar step,
because that is what it is for. This mirrors `tools/build-tiles.sh`, where .NET exports an
intermediate and the specialist tool produces the artifact.

Outputs, per vintage:

| Path | Committed? | What |
|---|---|---|
| `data/vintages/raw/<year>/` | no | source zip, extracted `.txt`, `source.json` provenance |
| `data/vintages/parquet/nbi_<year>.parquet` | no | the normalized vintage |
| `data/vintages/rejects/<year>.csv` | no | itemized rejects with reason codes |
| `tools/vintages/catalog.json` | **yes** | the reconciliation record (~1 KB per vintage) |

## What the FHWA files actually look like

Verified 2026-07-25 against the real national files for 1992, 2010 and 2025.

**Which download.** Each year publishes several packagings. This pipeline uses the *national
single-file delimited* export, because it is the only one that exists for all 34 years. Its URL
slug changes partway through the range:

| Years | URL |
|---|---|
| 1992–2017 | `https://www.fhwa.dot.gov/bridge/nbi/<year>hwybronlyonefile.zip` |
| 2018–2025 | `https://www.fhwa.dot.gov/bridge/nbi/<year>hwybronefiledel.zip` |

The "all records" variant (`<year>allstatesallrecsdel.zip`) is deliberately **not** used even where
it exists (2013+): it includes non-highway records, so mixing it in would move the population
mid-series and quietly corrupt every trend. Note this means the vintage series is a different
population from the Phase 0 snapshot, which was loaded from the 2025 all-records file — the two are
not expected to have equal row counts.

**Inner filenames are not predictable.** 1992 ships `fluna_991992-20160919110712.txt`, 2010 ships
`2010_highwaybridgesonly_onefile.txt`, 2025 ships `2025HwyBridgesDelimitedAllStates.txt`. The
download script extracts whatever single `.txt` the zip contains rather than guessing a name.

**The published dialect is violated by the published data.** FHWA documents these as "comma
separated, and the text qualifier is a single quote". But apostrophes appear *unescaped inside*
qualified text — `'O'NEAL ROAD'` in 1992, `'MOORE'S MILL CREEK'` in 2010. Any parser that treats
the single quote as a real quote character mis-splits those rows; DuckDB's CSV sniffer refuses the
1992 file outright because of it.

So the qualifier is treated as decoration, not structure: split on every comma, then strip a
balanced pair of surrounding quotes and the fixed-width padding the older eras keep inside it
(`'BUCK CREEK              '` → `BUCK CREEK`). That is only safe if no field value ever contains a
comma, which was checked across all **1,894,892** data rows of the three sampled national files —
zero rows disagreed with their header's field count. If a future vintage breaks that, the row's
field count stops matching and it is **rejected**, not silently mis-parsed.

**Three record layouts.** Eras are identified by signature columns, never by year:

| Era | Signature | Sampled | Columns |
|---|---|---|---|
| `TenYearRule` | `STATUS_WITH_10YR_RULE` | 1992 | 134 |
| `SufficiencyRating` | `STATUS` + `SUFFICIENCY_RATING`, no `BRIDGE_CONDITION` | 2010 | 133 |
| `PerformanceMeasures` | `BRIDGE_CONDITION` | 2025 | 123 |

Only 1992, 2010 and 2025 are *pinned* to an era in `VintageYearEra`, because only those have been
verified against the real file. Other years are classified from the file itself and the detected
era is recorded in the catalog; the full 1992–2025 run pins the rest. Converting a file whose era
contradicts the year it is declared as fails loudly and writes nothing.

**The 10-year rule.** FHWA notes that vintages carrying `STATUS_WITH_10YR_RULE` / `STATUS_NO_10YR_RULE`
"apply a cancelled agency policy known as the 10-year rule", which excluded bridges built or
reconstructed in the prior ten years from deficiency status. Those columns are preserved as
published, but **any deficiency-status comparison across that boundary is comparing two different
definitions.** FR-1.2/FR-1.3 must state this where it shows a trend (GR-6).

## The normalized schema

One row per structure record. Columns are the union across every sampled vintage — a column exists
if *any* vintage had it, and is NULL for vintages that lacked it — so every Parquet file has
identical columns in identical order and the catalog reads as one relation.

- **4 provenance columns**: `VINTAGE_YEAR`, `SOURCE_FILE`, `SOURCE_SHA256`, `SOURCE_ROW`
  (the physical line, so any row traces back to its source file).
- **139 published columns** in canonical order (`SpanSight.Core/Vintages/VintageSchema.cs`),
  all `VARCHAR`: an NBI code like `01` is not the number 1, and `N` is not a missing value.
- **4 typed condition columns**: `DECK_COND_058_NUM`, `SUPERSTRUCTURE_COND_059_NUM`,
  `SUBSTRUCTURE_COND_060_NUM`, `CULVERT_COND_062_NUM` — `TINYINT`, single digits only, `N` and
  blank become NULL. The published codes are kept alongside them untouched.

147 columns total. Good/Fair/Poor is **not** computed here: FR-1.2 replays the Phase 0
`ConditionClassifier` over these columns so there is one classifier, not two that can drift.

An unknown column in a source file is a hard error, not a silent drop — a new NBI item is a
deliberate schema change, so add it to `VintageSchema.Columns` and re-convert earlier vintages so
the catalog stays one relation.

## Rejects (AC-2)

Nothing is dropped: every data row lands in the Parquet or in `data/vintages/rejects/<year>.csv`,
with counts reconciling in the catalog. Reason codes follow the Phase 0 quarantine style:

| Code | Meaning |
|---|---|
| `row_field_count_mismatch` | field count disagrees with the header — the line cannot be split |
| `missing_key_field` | state code and/or structure number blank — the row has no identity |

Deliberately narrow. The vintage Parquet is a faithful normalized copy of published data, so only
rows that cannot become a record at all are rejected. Semantic screening (implausible coordinates,
impossible build years) stays out — those rows are still published history, and discarding them
here would silently change every downstream statistic. FR-1.2 applies the Phase 0 validator when it
replays the classifier.

## Reconciling (AC-2)

`catalog.json` records, per vintage: source URL + zip SHA-256 + extracted-file SHA-256 + download
date, rows in source, rows converted, rows rejected (by reason), source column count, which
superset columns that vintage lacked, Parquet row count and size, conversion timestamp and tool
version.

`convert.sh` fails if `converted + rejected != source rows`, or if the Parquet row count disagrees
with the converted count. To re-check by hand:

```bash
duckdb -c "SELECT VINTAGE_YEAR, count(*) FROM read_parquet('data/vintages/parquet/nbi_*.parquet') GROUP BY 1 ORDER BY 1;"
python3 -c "import json;[print(k, v['rowsInSource'], v['rowsConverted'], v['rowsRejected']) for k,v in json.load(open('tools/vintages/catalog.json'))['vintages'].items()]"
```

## Reproducing from a clean checkout (AC-4)

```bash
git clone https://github.com/Cucox91/spansight && cd spansight
brew install duckdb
tools/vintages/download.sh          # ~1.6 GB of zips from FHWA; re-runnable, skips what it has
tools/vintages/convert.sh           # → data/vintages/parquet/nbi_<year>.parquet + catalog.json
duckdb -c "SELECT VINTAGE_YEAR, count(*) FROM read_parquet('data/vintages/parquet/nbi_*.parquet') GROUP BY 1 ORDER BY 1;"
```

Both scripts are re-runnable: `download.sh` skips files already present (so an interrupted bulk
download resumes by running it again), and `convert.sh` rewrites its outputs from the raw files.
Nothing but `catalog.json` is committed — no bulk NBI data in git (CLAUDE.md rule 4).
