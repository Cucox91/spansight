# NBI vintage pipeline (FR-1.1)

Converts the FHWA annual National Bridge Inventory ASCII files (1992–2025) into one normalized
Parquet file per vintage, so DuckDB can read the whole history as a single relation. The historical
set never enters the serving Postgres — that stays small and cheap (ADR-005, NFR-2).

```
tools/vintages/download.sh            # fetch vintages into data/vintages/raw/ (gitignored)
tools/vintages/convert.sh             # normalize every downloaded vintage → Parquet
tools/vintages/convert.sh 1992 2010   # …or just these years
tools/vintages/convert.sh --fixtures  # the committed 300-row layout fixtures (what CI runs)
tools/vintages/archive-to-blob.sh     # upload the Parquet set to Blob cool tier (RUNBOOK §10)

duckdb -init tools/vintages/catalog.sql -c "SELECT * FROM nbi_bridges_per_year"   # query it
```

All 34 vintages (1992–2025) are converted and reconciled — see
[the full run](#the-full-run--3434-2026-07-26).

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
| `src/tests/fixtures/vintages/nbi_<year>.txt` | **yes** | 300 real rows per published layout (1992, 2010, 2016, 2017, 2025) — what CI converts |
| `tools/vintages/catalog.json` | **yes** | the reconciliation record (~1 KB per vintage) |
| `tools/vintages/catalog.sql` | **yes** | the DuckDB entry point — views over the Parquet set (AC-4) |

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

**Three record layouts, five published column counts.** Eras are identified by signature columns,
never by year:

| Era | Signature | Years | Columns |
|---|---|---|---|
| `TenYearRule` | `STATUS_WITH_10YR_RULE` | 1992–2009, 2012–2018 | 134 (135 in 2016, 137 in 2017–18) |
| `SufficiencyRating` | `STATUS` + `SUFFICIENCY_RATING`, no `BRIDGE_CONDITION` | 2010–2011 | 133 |
| `PerformanceMeasures` | `BRIDGE_CONDITION` | 2019–2025 | 123 |

**The era sequence is not monotonic.** The 10-year-rule columns are present 1992–2009, *vanish* for
2010–2011, and *come back* for 2012–2018 before the performance-measures layout drops them for good
in 2019. Any "the 10-year rule ended in year N" cutoff would be wrong for seven vintages, which is
exactly why `VintageYearEra` pins all 34 years from the real file headers rather than deriving them
from a rule. Converting a file whose era contradicts the year it is declared as fails loudly and
writes nothing.

**`CAT10` / `CAT23` / `CAT29` — the same three columns under FHWA's older names.** 2016 appends
`CAT10`; 2017–2018 append all three. They are the quantities that 2019+ publishes as
`BRIDGE_CONDITION`, `LOWEST_RATING` and `DECK_AREA`, which was verified rather than assumed
(2026-07-26, against the real 2017/2018/2019 files):

| Column | 2019+ name | Evidence |
|---|---|---|
| `CAT10` | `BRIDGE_CONDITION` | agrees with FHWA's Good/Fair/Poor rule over items 58/59/60/62 on **299,947 of 299,947** rows carrying condition data |
| `CAT23` | `LOWEST_RATING` | agrees with `min(058, 059, 060, 062)` on **299,947 of 299,947** |
| `CAT29` | `DECK_AREA` | same distribution and identical maximum (284,739); median ratio to `STRUCTURE_LEN_MT_049 × DECK_WIDTH_MT_052` of exactly 1.0000, so the same square metres |

They are carried **as themselves** — the Parquet stays a faithful copy of published text, so nothing
is silently renamed. The coalescing into one continuous 2016–2025 series happens once, visibly, in
`catalog.sql` (`nbi_unified`).

**The population moves at 2009→2010.** Row counts run 713,115 (2009) → 604,493 (2010): FHWA changed
what the national single-file export contains at the same boundary the layout changed. This is a
real discontinuity in the published series, not a conversion defect. Any trend crossing it is
comparing two populations and must say so (GR-6).

**2015 ships a file named after 2016.** The 2015 zip contains `slubkin_992016-20160125163323.txt`
and the 2016 zip contains `slubkin_992016-20170106140351.txt` — both carry `992016` in the name. They
are genuinely different vintages: different SHA-256, different column counts (134 vs 135), and
inspection dates that peak a year apart. The pipeline keys off the download year, never the inner
filename.

**The 10-year rule.** FHWA notes that vintages carrying `STATUS_WITH_10YR_RULE` / `STATUS_NO_10YR_RULE`
"apply a cancelled agency policy known as the 10-year rule", which excluded bridges built or
reconstructed in the prior ten years from deficiency status. Those columns are preserved as
published, but **any deficiency-status comparison across that boundary is comparing two different
definitions.** FR-1.2/FR-1.3 must state this where it shows a trend (GR-6).

## The normalized schema

One row per structure record. Columns are the union across all 34 vintages — a column exists
if *any* vintage had it, and is NULL for vintages that lacked it — so every Parquet file has
identical columns in identical order and the catalog reads as one relation.

- **4 provenance columns**: `VINTAGE_YEAR`, `SOURCE_FILE`, `SOURCE_SHA256`, `SOURCE_ROW`
  (the physical line, so any row traces back to its source file).
- **142 published columns** in canonical order (`SpanSight.Core/Vintages/VintageSchema.cs`),
  all `VARCHAR`: an NBI code like `01` is not the number 1, and `N` is not a missing value.
  The last three are `CAT10`, `CAT23` and `CAT29`, which only 2016–2018 carry.
- **4 typed condition columns**: `DECK_COND_058_NUM`, `SUPERSTRUCTURE_COND_059_NUM`,
  `SUBSTRUCTURE_COND_060_NUM`, `CULVERT_COND_062_NUM` — `TINYINT`, single digits only, `N` and
  blank become NULL. The published codes are kept alongside them untouched.

150 columns total. Good/Fair/Poor is **not** computed here: FR-1.2 replays the Phase 0
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
with the converted count. To re-check every vintage at once, as a query:

```bash
duckdb -init tools/vintages/catalog.sql -c "SELECT * FROM nbi_reconciliation"
duckdb -init tools/vintages/catalog.sql -c "SELECT bool_and(reconciles) FROM nbi_reconciliation"
```

### The full run — 34/34, 2026-07-26

Every vintage 1992–2025, converted on the dev Mac from the files recorded in `catalog.json`.
Generated from the manifest; numbers only.

<!-- Regenerate with: duckdb -init tools/vintages/catalog.sql -c "SELECT * FROM nbi_reconciliation" -->

| Vintage | Era | Source rows | Converted | Rejected | Reject % | Parquet |
|---:|---|---:|---:|---:|---:|---:|
| 1992 | TenYearRule | 666,206 | 666,206 | 0 | 0.0000% | 35.3 MB |
| 1993 | TenYearRule | 668,433 | 668,433 | 0 | 0.0000% | 35.8 MB |
| 1994 | TenYearRule | 670,876 | 670,876 | 0 | 0.0000% | 35.9 MB |
| 1995 | TenYearRule | 680,661 | 680,661 | 0 | 0.0000% | 37.4 MB |
| 1996 | TenYearRule | 679,060 | 679,060 | 0 | 0.0000% | 37.4 MB |
| 1997 | TenYearRule | 683,045 | 683,045 | 0 | 0.0000% | 39.5 MB |
| 1998 | TenYearRule | 683,860 | 683,860 | 0 | 0.0000% | 40.3 MB |
| 1999 | TenYearRule | 688,415 | 688,415 | 0 | 0.0000% | 41.0 MB |
| 2000 | TenYearRule | 691,059 | 691,059 | 0 | 0.0000% | 41.3 MB |
| 2001 | TenYearRule | 694,940 | 694,940 | 0 | 0.0000% | 41.6 MB |
| 2002 | TenYearRule | 697,005 | 697,005 | 0 | 0.0000% | 41.4 MB |
| 2003 | TenYearRule | 699,903 | 699,903 | 0 | 0.0000% | 42.0 MB |
| 2004 | TenYearRule | 703,534 | 703,534 | 0 | 0.0000% | 42.7 MB |
| 2005 | TenYearRule | 706,753 | 706,753 | 0 | 0.0000% | 43.1 MB |
| 2006 | TenYearRule | 709,613 | 709,613 | 0 | 0.0000% | 43.9 MB |
| 2007 | TenYearRule | 715,434 | 715,434 | 0 | 0.0000% | 44.4 MB |
| 2008 | TenYearRule | 717,822 | 717,822 | 0 | 0.0000% | 44.7 MB |
| 2009 | TenYearRule | 713,115 | 713,115 | 0 | 0.0000% | 44.9 MB |
| 2010 | SufficiencyRating | 604,493 | 604,493 | 0 | 0.0000% | 39.1 MB |
| 2011 | SufficiencyRating | 605,103 | 605,103 | 0 | 0.0000% | 39.5 MB |
| 2012 | TenYearRule | 607,380 | 607,380 | 0 | 0.0000% | 40.9 MB |
| 2013 | TenYearRule | 607,751 | 607,751 | 0 | 0.0000% | 40.1 MB |
| 2014 | TenYearRule | 610,749 | 610,749 | 0 | 0.0000% | 40.2 MB |
| 2015 | TenYearRule | 611,845 | 611,845 | 0 | 0.0000% | 41.5 MB |
| 2016 | TenYearRule | 614,387 | 614,387 | 0 | 0.0000% | 41.9 MB |
| 2017 | TenYearRule | 615,002 | 615,002 | 0 | 0.0000% | 44.2 MB |
| 2018 | TenYearRule | 616,096 | 616,096 | 0 | 0.0000% | 44.6 MB |
| 2019 | PerformanceMeasures | 617,084 | 617,084 | 0 | 0.0000% | 42.7 MB |
| 2020 | PerformanceMeasures | 618,456 | 618,456 | 0 | 0.0000% | 43.0 MB |
| 2021 | PerformanceMeasures | 619,622 | 619,622 | 0 | 0.0000% | 43.2 MB |
| 2022 | PerformanceMeasures | 620,669 | 620,669 | 0 | 0.0000% | 43.3 MB |
| 2023 | PerformanceMeasures | 621,581 | 621,581 | 0 | 0.0000% | 43.4 MB |
| 2024 | PerformanceMeasures | 623,218 | 623,217 | **1** | 0.0002% | 43.5 MB |
| 2025 | PerformanceMeasures | 624,193 | 624,193 | 0 | 0.0000% | 43.9 MB |
| **34 vintages** | | **22,307,363** | **22,307,362** | **1** | **0.000004%** | **1,408 MB** |

**The one reject, in full.** 2024 line 459,987 — a Pennsylvania Turnpike ramp whose
`OTHR_STATE_STRUC_NO_099` field holds the literal value `1sDPG, 2sDIB`. The embedded comma splits
the line into 124 fields where the header declares 123, shifting every later field by one: read
naively it would have stored `BRIDGE_CONDITION = '42'` and `DECK_AREA = '6'`. This is precisely the
case the field-count check exists for — the row is rejected with `row_field_count_mismatch` rather
than guessed at, because there is no way to know where the extra comma was inserted without
inventing the answer. It is the only such row in 22.3 million.

The 0.30% reject rate of the Phase 0 snapshot is **not** the baseline for this table: that load
used the 2025 *all-records* file and applied semantic validators (coordinates, build years). This
pipeline reads the *highway-bridges-only* export and rejects only rows that cannot become a record
at all, so near-zero is the expected result and 0.000004% is what the run produced.

## The DuckDB entry point (AC-4)

`tools/vintages/catalog.sql` is the sanctioned way to read the catalog. Load it and the whole
1992–2025 history is one relation, so any published aggregate can be re-derived by someone who has
just cloned the repo and holds the Parquet set — no database, no application code:

```bash
duckdb -init tools/vintages/catalog.sql                                        # interactive
duckdb -init tools/vintages/catalog.sql -c "SELECT * FROM nbi_bridges_per_year"
```

Run it from the repository root — the view definitions use repo-relative paths.

| View | What |
|---|---|
| `nbi` | every vintage as one relation, exactly as converted |
| `nbi_unified` | `nbi` plus `BRIDGE_CONDITION_ALL` / `LOWEST_RATING_ALL` / `DECK_AREA_ALL`, coalescing the 2016–2018 `CAT*` spellings into one continuous series |
| `nbi_manifest` | `catalog.json` as a table, so provenance joins to data |
| `nbi_bridges_per_year` | the sample aggregate: one row per vintage |
| `nbi_reconciliation` | per-vintage source/converted/rejected with a `reconciles` flag |

## Reproducing from a clean checkout (AC-4)

```bash
git clone https://github.com/Cucox91/spansight && cd spansight
brew install duckdb
tools/vintages/download.sh          # ~1.6 GB of zips from FHWA; re-runnable, skips what it has
tools/vintages/convert.sh           # → data/vintages/parquet/nbi_<year>.parquet + catalog.json
duckdb -init tools/vintages/catalog.sql -c "SELECT * FROM nbi_bridges_per_year"
```

Archiving the finished Parquet set to Blob storage is `tools/vintages/archive-to-blob.sh`; the
operator procedure is docs/RUNBOOK.md §10.

Both scripts are re-runnable: `download.sh` skips files already present (so an interrupted bulk
download resumes by running it again), and `convert.sh` rewrites its outputs from the raw files.
Nothing but `catalog.json` is committed — no bulk NBI data in git (CLAUDE.md rule 4).
