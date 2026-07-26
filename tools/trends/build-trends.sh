#!/usr/bin/env bash
# tools/trends/build-trends.sh — FR-1.2 AC-1: build the condition-history aggregates.
#
# Usage:
#   tools/trends/build-trends.sh              # the real 1992-2025 Parquet catalog
#   tools/trends/build-trends.sh --fixtures   # the committed era fixtures (fast; used by CI)
#   tools/trends/build-trends.sh --out <dir>  # write somewhere else
#
# The method is tools/trends/trends.sql — read that file, not this one, to see how a condition
# series is derived. This script only points the SQL at a source, runs it, checks it, and records
# what it did.
#
# Outputs (gitignored — aggregates are rebuildable, and bulk data never enters git):
#   <out>/bridge_series.parquet   one row per structure: first/last year + packed per-year ratings
#   <out>/rollup.parquet          county and state x year Good/Fair/Poor/Unknown counts
#   <out>/manifest.json           run id, catalog hash, row counts, dedup and gap statistics
#   <out>/bridge_series.csv       the same two relations as CSV — the loader's input
#   <out>/rollup.csv
#
# Parquet is the analytical artefact (DuckDB reads it directly; FR-1.3 builds on it). The CSV twins
# exist so `load-trends` stays a plain .NET reader with no Parquet dependency and no duckdb binary
# on the machine doing the cloud publish. Same rows, written in the same statement.
#
# The manifest's runId and catalogSha256 are what the loader stamps onto every row it writes, so a
# number on the site can always be traced back to the job that produced it (NFR-3).
#
# Requires: duckdb (brew install duckdb), python3.

set -euo pipefail

cd "$(dirname "$0")/../.."

PARQUET_GLOB="data/vintages/parquet/nbi_*.parquet"
CATALOG="tools/vintages/catalog.json"
OUT_DIR="data/trends"
FIXTURES=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --fixtures)
      FIXTURES=1
      PARQUET_GLOB="data/vintages/fixtures-out/parquet/nbi_*.parquet"
      OUT_DIR="data/trends/fixtures-out"
      shift
      ;;
    --out) OUT_DIR="$2"; shift 2 ;;
    --help|-h) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
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

# shellcheck disable=SC2086  # deliberate glob expansion to count inputs
if ! compgen -G "$PARQUET_GLOB" >/dev/null; then
  echo "error: no vintage Parquet matching $PARQUET_GLOB" >&2
  if (( FIXTURES )); then
    echo "  build them first: tools/vintages/convert.sh --fixtures" >&2
  else
    echo "  build them first: tools/vintages/convert.sh   (see tools/vintages/README.md)" >&2
  fi
  exit 1
fi

mkdir -p "$OUT_DIR"

# Provenance. The run id is derived from the clock; the catalog hash pins the exact input set, so
# two runs over the same Parquet produce byte-identical aggregates with different run ids — which is
# what makes the loader's convergence check meaningful.
RUN_ID="trends-$(date -u +%Y%m%dT%H%M%SZ)"
if (( FIXTURES )); then
  CATALOG_SHA="fixtures"
elif command -v sha256sum >/dev/null 2>&1; then
  CATALOG_SHA="$(sha256sum "$CATALOG" | cut -d' ' -f1)"          # GNU (Linux)
else
  CATALOG_SHA="$(shasum -a 256 "$CATALOG" | cut -d' ' -f1)"      # BSD (macOS)
fi

echo "==> source   $PARQUET_GLOB"
echo "    run id   $RUN_ID"
echo "    catalog  ${CATALOG_SHA:0:16}…"

# A temp *directory*, not `mktemp -t <name>`: BSD mktemp accepts a suffix-less template but GNU
# mktemp requires X's in it, so the -t form fails on Linux (and the version that appended ".sql"
# to the result also orphaned the file mktemp had actually created).
INIT_DIR="$(mktemp -d)"
INIT="$INIT_DIR/init.sql"
trap 'rm -rf "$INIT_DIR"' EXIT
{
  echo "CREATE OR REPLACE VIEW nbi_source AS SELECT * FROM read_parquet('$PARQUET_GLOB');"
  cat tools/trends/trends.sql
} > "$INIT"

# One DuckDB session: define, check, then write. The checks run *before* the outputs are written so
# a failing invariant never leaves a half-published aggregate behind.
echo "==> checking invariants"
CHECKS="$(duckdb -noheader -list -init "$INIT" -c "
  SELECT 'state totals',       count(*) FROM trend_check_state_totals
  UNION ALL SELECT 'class partition',    count(*) FROM trend_check_class_partition
  UNION ALL SELECT 'county within state',count(*) FROM trend_check_county_within_state
  UNION ALL SELECT 'series shape',       count(*) FROM trend_check_series_shape
  UNION ALL SELECT 'series vs rollup',   count(*) FROM trend_check_series_vs_rollup;" 2>/dev/null)"

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
(( FAILED )) && { echo "error: aggregates not written — invariants failed above." >&2; exit 1; }

echo "==> writing aggregates to $OUT_DIR"
duckdb -init "$INIT" -c "
  CREATE OR REPLACE TABLE series_out AS
    SELECT * FROM trend_bridge_series ORDER BY state_code, structure_number;
  CREATE OR REPLACE TABLE rollup_out AS
    SELECT * FROM trend_rollup ORDER BY level, fips, vintage_year;
  COPY series_out TO '$OUT_DIR/bridge_series.parquet' (FORMAT PARQUET, COMPRESSION ZSTD);
  COPY rollup_out TO '$OUT_DIR/rollup.parquet' (FORMAT PARQUET, COMPRESSION ZSTD);
  COPY series_out TO '$OUT_DIR/bridge_series.csv' (FORMAT CSV, HEADER true);
  COPY rollup_out TO '$OUT_DIR/rollup.csv' (FORMAT CSV, HEADER true);
" >/dev/null

# Statistics worth recording rather than discovering later: how much the dedup rule actually
# removed, and how gappy the series are. Both are reported, never silently applied.
echo "==> recording $OUT_DIR/manifest.json"
duckdb -noheader -list -init "$INIT" -c "
  COPY (
    WITH raw AS (SELECT VINTAGE_YEAR AS y, count(*) AS n FROM nbi_source GROUP BY 1),
    on_structure AS (
      SELECT VINTAGE_YEAR AS y, count(*) AS n FROM nbi_source WHERE RECORD_TYPE_005A = '1' GROUP BY 1
    ),
    kept AS (SELECT vintage_year AS y, count(*) AS n FROM trend_structure_year GROUP BY 1)
    SELECT raw.y AS vintage_year,
           raw.n AS source_rows,
           on_structure.n AS record_type_1_rows,
           kept.n AS structures_kept,
           on_structure.n - kept.n AS dropped_as_duplicate_key
    FROM raw JOIN on_structure USING (y) JOIN kept USING (y) ORDER BY 1
  ) TO '$OUT_DIR/per-year.json' (FORMAT JSON, ARRAY true);
" >/dev/null

python3 - "$OUT_DIR" "$RUN_ID" "$CATALOG_SHA" "$PARQUET_GLOB" <<'PY'
import json, os, subprocess, sys

out_dir, run_id, catalog_sha, source_glob = sys.argv[1:5]

def q(sql):
    r = subprocess.run(["duckdb", "-noheader", "-list", "-c", sql],
                       capture_output=True, text=True)
    if r.returncode:
        sys.exit(r.stderr.strip())
    return r.stdout.strip()

series = f"read_parquet('{out_dir}/bridge_series.parquet')"
rollup = f"read_parquet('{out_dir}/rollup.parquet')"

bridges, first_year, last_year, total_obs, gappy = q(
    f"SELECT count(*), min(first_year), max(last_year), sum(observed_years), "
    f"sum(CASE WHEN observed_years <> last_year - first_year + 1 THEN 1 ELSE 0 END) FROM {series}"
).split("|")
rollup_rows, counties, states = q(
    f"SELECT count(*), count(DISTINCT fips) FILTER (level='county'), "
    f"count(DISTINCT fips) FILTER (level='state') FROM {rollup}"
).split("|")

per_year = json.load(open(os.path.join(out_dir, "per-year.json")))
os.remove(os.path.join(out_dir, "per-year.json"))

manifest = {
    "schemaVersion": 1,
    "runId": run_id,
    "catalogSha256": catalog_sha,
    "source": source_glob,
    "firstYear": int(first_year),
    "lastYear": int(last_year),
    "vintages": len(per_year),
    "bridges": int(bridges),
    "observations": int(total_obs),
    "bridgesWithGaps": int(gappy),
    "rollupRows": int(rollup_rows),
    "counties": int(counties),
    "states": int(states),
    "perYear": per_year,
}
with open(os.path.join(out_dir, "manifest.json"), "w") as fh:
    json.dump(manifest, fh, indent=2)
    fh.write("\n")

dropped = sum(r["dropped_as_duplicate_key"] for r in per_year)
non_type1 = sum(r["source_rows"] - r["record_type_1_rows"] for r in per_year)
print(f"    {manifest['vintages']} vintages {manifest['firstYear']}-{manifest['lastYear']}  |  "
      f"{manifest['bridges']:,} structures  |  {manifest['observations']:,} observations")
print(f"    {manifest['rollupRows']:,} rollup rows ({manifest['counties']:,} counties, "
      f"{manifest['states']} states)")
print(f"    {manifest['bridgesWithGaps']:,} structures have at least one gap year "
      f"(absent from a vintage — never interpolated)")
print(f"    excluded: {non_type1:,} non-type-1 rows (routes under/alongside, no structure "
      f"condition), {dropped:,} duplicate-key rows")
PY

echo "==> Done. Load it into the serving database with:"
echo "    dotnet run --project src/SpanSight.Ingestion -- load-trends --file $OUT_DIR"
