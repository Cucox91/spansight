#!/usr/bin/env bash
# tools/deterioration/build-deterioration.sh — FR-1.3 AC-1: build the condition-transition matrices.
#
# Usage:
#   tools/deterioration/build-deterioration.sh              # the real 1992-2025 Parquet catalog
#   tools/deterioration/build-deterioration.sh --fixtures    # the committed era fixtures (used by CI)
#   tools/deterioration/build-deterioration.sh --synthetic   # the hand-computed synthetic fixture
#   tools/deterioration/build-deterioration.sh --out <dir>   # write somewhere else
#
# The method is tools/deterioration/deterioration.sql — read that file, not this one, to see how a
# transition matrix is derived. This script only points the SQL at a source, checks the result,
# records what it did, and prints the diagnostics the methodology says must be published rather than
# discovered later.
#
# Outputs (gitignored — aggregates are rebuildable, and bulk data never enters git):
#   <out>/matrix_row.parquet   one row per (component x cohort x from-rating): the denominator + span
#   <out>/matrix_cell.parquet  one row per non-zero cell: the numerators
#   <out>/manifest.json        run id, catalog hash, methodology version, sample floor, row counts
#   <out>/matrix_row.csv       the same two relations as CSV — the loader's input
#   <out>/matrix_cell.csv
#
# Everything produced here is a descriptive statistic of published NBI ratings at cohort level
# (GR-6). Nothing is predicted, no chain is iterated, no steady state is solved, and no figure is
# ever computed for an individual structure.
#
# Requires: duckdb (brew install duckdb), python3.

set -euo pipefail

cd "$(dirname "$0")/../.."

CATALOG="tools/vintages/catalog.json"
OUT_DIR="data/deterioration"
MODE="real"
PARQUET_GLOB="data/vintages/parquet/nbi_*.parquet"
SYNTHETIC_CSV="src/tests/fixtures/deterioration/synthetic_vintages.csv"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --fixtures)
      MODE="fixtures"
      PARQUET_GLOB="data/vintages/fixtures-out/parquet/nbi_*.parquet"
      OUT_DIR="data/deterioration/fixtures-out"
      shift
      ;;
    --synthetic)
      MODE="synthetic"
      OUT_DIR="data/deterioration/synthetic-out"
      shift
      ;;
    --out) OUT_DIR="$2"; shift 2 ;;
    --help|-h) sed -n '2,29p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "error: unrecognised argument '$1'." >&2; exit 2 ;;
  esac
done

for tool in duckdb python3; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "error: $tool not found on PATH." >&2
    [[ "$tool" == "duckdb" ]] && echo "  install: brew install duckdb" >&2
    exit 1
  }
done

# The source line the SQL expects. Every column deterioration.sql reads is named explicitly, which is
# what lets the synthetic mode hand it a twelve-column CSV instead of the 150-column Parquet.
if [[ "$MODE" == "synthetic" ]]; then
  [[ -f "$SYNTHETIC_CSV" ]] || { echo "error: $SYNTHETIC_CSV not found." >&2; exit 1; }
  SOURCE_DESCRIPTION="$SYNTHETIC_CSV"
  SOURCE_SQL="CREATE OR REPLACE VIEW nbi_source AS SELECT * REPLACE (
      CAST(VINTAGE_YEAR AS SMALLINT) AS VINTAGE_YEAR, CAST(SOURCE_ROW AS INTEGER) AS SOURCE_ROW)
    FROM read_csv('$SYNTHETIC_CSV', all_varchar=true);"
else
  # shellcheck disable=SC2086  # deliberate glob expansion to count inputs
  if ! compgen -G "$PARQUET_GLOB" >/dev/null; then
    echo "error: no vintage Parquet matching $PARQUET_GLOB" >&2
    if [[ "$MODE" == "fixtures" ]]; then
      echo "  build them first: tools/vintages/convert.sh --fixtures" >&2
    else
      echo "  build them first: tools/vintages/convert.sh   (see tools/vintages/README.md)" >&2
    fi
    exit 1
  fi
  SOURCE_DESCRIPTION="$PARQUET_GLOB"
  SOURCE_SQL="CREATE OR REPLACE VIEW nbi_source AS SELECT * FROM read_parquet('$PARQUET_GLOB');"
fi

mkdir -p "$OUT_DIR"

# Provenance. The run id is derived from the clock; the catalog hash pins the exact input set, so two
# runs over the same Parquet produce identical matrices under different run ids — which is what makes
# the loader's convergence check meaningful.
RUN_ID="deterioration-$(date -u +%Y%m%dT%H%M%SZ)"
if [[ "$MODE" != "real" ]]; then
  CATALOG_SHA="$MODE"
elif command -v sha256sum >/dev/null 2>&1; then
  CATALOG_SHA="$(sha256sum "$CATALOG" | cut -d' ' -f1)"          # GNU (Linux)
else
  CATALOG_SHA="$(shasum -a 256 "$CATALOG" | cut -d' ' -f1)"      # BSD (macOS)
fi

echo "==> source   $SOURCE_DESCRIPTION"
echo "    run id   $RUN_ID"
echo "    catalog  ${CATALOG_SHA:0:16}…"

# A temp *directory*, not `mktemp -t <name>`: BSD mktemp accepts a suffix-less template but GNU mktemp
# requires X's in it, so the -t form fails on Linux (the same portability fix build-trends.sh needed).
INIT_DIR="$(mktemp -d)"
INIT="$INIT_DIR/init.sql"
trap 'rm -rf "$INIT_DIR"' EXIT
{
  echo "$SOURCE_SQL"
  cat tools/deterioration/deterioration.sql
} > "$INIT"

# The checks run *before* the outputs are written, so a failing invariant never leaves a half-published
# set of matrices behind. An unmapped cohort code is a hard stop, not a warning: it means FHWA
# published something this grouping has never seen, and a matrix missing a slice is worse than no
# matrix (methodology §5, §8).
echo "==> checking invariants"
CHECKS="$(duckdb -noheader -list -init "$INIT" -c "
  SELECT 'unmapped cohort code',   count(*) FROM det_check_unmapped
  UNION ALL SELECT 'pair shape',            count(*) FROM det_check_pair_shape
  UNION ALL SELECT 'row totals vs cells',   count(*) FROM det_check_row_totals
  UNION ALL SELECT 'national = sum of cohorts', count(*) FROM det_check_national_is_the_sum
  UNION ALL SELECT 'sentinel is whole',     count(*) FROM det_check_sentinel_is_whole
  UNION ALL SELECT 'reserved value free',   count(*) FROM det_check_reserved_value
  UNION ALL SELECT 'row span consistent',   count(*) FROM det_check_span;" 2>/dev/null)"

FAILED=0
while IFS='|' read -r name violations; do
  [[ -z "$name" ]] && continue
  if [[ "$violations" == "0" ]]; then
    echo "    ok  $name"
  else
    echo "    FAIL  $name — $violations violating row(s)" >&2
    FAILED=1
  fi
done <<< "$CHECKS"
(( FAILED )) && { echo "error: matrices not written — invariants failed above." >&2; exit 1; }

echo "==> writing matrices to $OUT_DIR"
duckdb -init "$INIT" -c "
  CREATE OR REPLACE TABLE row_out AS
    SELECT * FROM det_matrix_row
    ORDER BY component, type_group, material_group, region, from_rating;
  CREATE OR REPLACE TABLE cell_out AS
    SELECT * FROM det_matrix_cell
    ORDER BY component, type_group, material_group, region, from_rating, to_rating;
  COPY row_out  TO '$OUT_DIR/matrix_row.parquet'  (FORMAT PARQUET, COMPRESSION ZSTD);
  COPY cell_out TO '$OUT_DIR/matrix_cell.parquet' (FORMAT PARQUET, COMPRESSION ZSTD);
  COPY row_out  TO '$OUT_DIR/matrix_row.csv'  (FORMAT CSV, HEADER true);
  COPY cell_out TO '$OUT_DIR/matrix_cell.csv' (FORMAT CSV, HEADER true);
" >/dev/null

echo "==> recording $OUT_DIR/manifest.json"
duckdb -noheader -list -init "$INIT" -c "
  COPY (SELECT * FROM det_diagnostic_movement ORDER BY component)
    TO '$OUT_DIR/movement.json' (FORMAT JSON, ARRAY true);
  COPY (
    SELECT component, type_group, material_group, region, from_rating,
           row_total, first_from_year, last_from_year, year_pairs_observed
    FROM det_diagnostic_narrow_span
  ) TO '$OUT_DIR/narrow-span.json' (FORMAT JSON, ARRAY true);
  COPY (
    SELECT m.sample_floor, m.methodology_version,
           (SELECT min(from_year) FROM det_pair)                     AS first_year,
           (SELECT max(to_year)   FROM det_pair)                     AS last_year,
           (SELECT count(DISTINCT from_year) FROM det_pair)          AS year_pairs,
           (SELECT count(*) FROM (SELECT DISTINCT state_code, structure_number, from_year FROM det_pair)) AS structure_pairs,
           (SELECT count(*) FROM det_pair)                           AS component_pairs,
           (SELECT count(DISTINCT (type_group, material_group, region)) FROM det_pair) AS cohorts,
           d.matrix_rows, d.rows_below_floor, d.pairs_below_floor
    FROM det_methodology m, det_diagnostic_floor d
  ) TO '$OUT_DIR/totals.json' (FORMAT JSON, ARRAY true);
" >/dev/null

python3 - "$OUT_DIR" "$RUN_ID" "$CATALOG_SHA" "$SOURCE_DESCRIPTION" <<'PY'
import json, os, subprocess, sys

out_dir, run_id, catalog_sha, source = sys.argv[1:5]

def q(sql):
    r = subprocess.run(["duckdb", "-noheader", "-list", "-c", sql], capture_output=True, text=True)
    if r.returncode:
        sys.exit(r.stderr.strip())
    return r.stdout.strip()

rows = f"read_parquet('{out_dir}/matrix_row.parquet')"
cells = f"read_parquet('{out_dir}/matrix_cell.parquet')"

# Counted off the written Parquet rather than from the views, so the manifest describes the artefact
# the loader will actually read. The loader re-checks these against the CSVs and refuses a mismatch.
cohort_rows, national_rows = q(
    f"SELECT count(*) FILTER (type_group <> 'All'), count(*) FILTER (type_group = 'All') FROM {rows}").split("|")
cohort_cells, national_cells = q(
    f"SELECT count(*) FILTER (type_group <> 'All'), count(*) FILTER (type_group = 'All') FROM {cells}").split("|")

def take(name):
    path = os.path.join(out_dir, name)
    with open(path) as fh:
        value = json.load(fh)
    os.remove(path)
    return value

totals = take("totals.json")[0]
movement = take("movement.json")
narrow_span = take("narrow-span.json")

manifest = {
    "schemaVersion": 1,
    "runId": run_id,
    "catalogSha256": catalog_sha,
    "source": source,
    # The published methodology constants, carried with the numbers they governed (methodology §7).
    "methodologyVersion": totals["methodology_version"],
    "sampleFloor": int(totals["sample_floor"]),
    "firstYear": int(totals["first_year"]),
    "lastYear": int(totals["last_year"]),
    "yearPairs": int(totals["year_pairs"]),
    "structurePairs": int(totals["structure_pairs"]),
    "componentPairs": int(totals["component_pairs"]),
    "cohorts": int(totals["cohorts"]),
    "matrixRows": int(cohort_rows) + int(national_rows),
    "cohortMatrixRows": int(cohort_rows),
    "nationalMatrixRows": int(national_rows),
    "cells": int(cohort_cells) + int(national_cells),
    "cohortCells": int(cohort_cells),
    "nationalCells": int(national_cells),
    "rowsBelowFloor": int(totals["rows_below_floor"]),
    "pairsBelowFloor": int(totals["pairs_below_floor"]),
    "movement": movement,
    "narrowSpanRows": narrow_span,
}
with open(os.path.join(out_dir, "manifest.json"), "w") as fh:
    json.dump(manifest, fh, indent=2)
    fh.write("\n")

print(f"    {manifest['yearPairs']} year-pairs {manifest['firstYear']}-{manifest['lastYear']}  |  "
      f"{manifest['structurePairs']:,} structure pairs  |  {manifest['componentPairs']:,} component pairs")
print(f"    {manifest['cohorts']:,} cohorts  |  {manifest['matrixRows']:,} matrix rows "
      f"({manifest['nationalMatrixRows']} national)  |  {manifest['cells']:,} non-zero cells")
floor, total_rows = manifest["sampleFloor"], manifest["cohortMatrixRows"]
below = manifest["rowsBelowFloor"]
share = (100.0 * below / total_rows) if total_rows else 0.0
print(f"    sample-size floor n >= {floor} (methodology {manifest['methodologyVersion']}): "
      f"{below:,} of {total_rows:,} cohort rows ({share:.2f}%) render \"insufficient data\" — "
      f"{manifest['pairsBelowFloor']:,} pairs")
for m in movement:
    print(f"    {m['component']:<15} {int(m['pairs']):>12,} pairs  "
          f"{m['pct_unchanged']:>6}% unchanged  {m['pct_declined']:>5}% declined  {m['pct_improved']:>5}% improved")
print("    ^ the unchanged share is inflated by the ~24-month inspection cycle: intermediate annual")
print("      files carry the last inspection forward, so many \"no change\" rows are no new inspection")
print("      (methodology §6.1 — this caveat ships with every matrix the UI renders).")
if narrow_span:
    print(f"    {len(narrow_span):,} rows clear the floor on <= 5 year-pairs of evidence — reported, not")
    print("      suppressed; every row displays its own span so a pooled label cannot mislead (§6.8).")
PY

echo "==> Done. Load it into the serving database with:"
echo "    dotnet run --project src/SpanSight.Ingestion -- load-deterioration --file $OUT_DIR"
