#!/usr/bin/env bash
# tools/census/join-counties.sh — FR-1.5 AC-2: join bridges to county polygons and measure the gap.
#
# Usage:
#   tools/census/join-counties.sh                       # the serving database + the real TIGER/ACS set
#   tools/census/join-counties.sh --fixtures            # committed hand-placed points + the 6-county fixture
#   tools/census/join-counties.sh --out <dir>           # write somewhere else
#   tools/census/join-counties.sh --connection <conn>   # libpq string for the bridge side (real mode)
#
# The method is tools/census/county-join.sql — read that file, not this one, to see how a bridge is
# assigned to a county and what a miss means. This script only points the SQL at its three sources,
# refuses to write anything while an invariant is violated, records what it did, and prints the
# coverage figures FR-1.5 AC-2 requires to be published rather than discovered later.
#
# Outputs (gitignored — aggregates are rebuildable, and bulk data never enters git):
#   <out>/county.parquet        one row per TIGER county: name, areas, ACS population + vintage
#   <out>/miss.parquet          one row per unmatched structure: reason, nearest county, distance
#   <out>/disagreement.parquet  one row per (published code, containing polygon) pair that disagrees
#   <out>/manifest.json         run id, catalog hash, method version, coverage counts, diagnostics
#   <out>/county.csv            the same three relations as CSV — the loader's input
#   <out>/miss.csv
#   <out>/disagreement.csv
#
# In real mode the bridge side is read from core.bridge in the serving database, because AC-2 asks
# for the share of the bridges a reader is actually served and because the WGS84 point there was
# produced by the one tested DMS decoder (county-join.sql §0 explains both). The connection string is
# taken from --connection, else $SPANSIGHT_CONNECTION, else the local compose default; it is a local
# development credential and no secret belongs in this file (NFR-4).
#
# Nothing here corrects a published county code. This measures how often the coordinate and the code
# disagree and publishes that gap; item 3 remains the county the product reports (GR-6).
#
# Requires: duckdb (brew install duckdb), python3. Real mode also needs the serving database loaded.

set -euo pipefail

cd "$(dirname "$0")/../.."

CATALOG="tools/census/catalog.json"
OUT_DIR="data/census/join"
MODE="real"
COUNTY_PARQUET="data/census/parquet/counties.parquet"
POPULATION_PARQUET="data/census/parquet/county_population.parquet"
FIXTURE_POINTS="src/tests/fixtures/census-join/bridge_points.csv"
CONNECTION="${SPANSIGHT_CONNECTION:-host=localhost port=5432 dbname=spansight user=spansight password=spansight}"

usage() { sed -n '2,31p' "$0" | sed 's/^# \{0,1\}//'; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --fixtures)
      MODE="fixtures"
      COUNTY_PARQUET="data/census/fixtures-out/parquet/counties.parquet"
      POPULATION_PARQUET="data/census/fixtures-out/parquet/county_population.parquet"
      OUT_DIR="data/census/join/fixtures-out"
      shift
      ;;
    --out) OUT_DIR="$2"; shift 2 ;;
    --connection) CONNECTION="$2"; shift 2 ;;
    --help|-h) usage; exit 0 ;;
    *) echo "error: unrecognised argument '$1'." >&2; usage >&2; exit 2 ;;
  esac
done

for tool in duckdb python3; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "error: $tool not found on PATH." >&2
    [[ "$tool" == "duckdb" ]] && echo "  install: brew install duckdb" >&2
    exit 1
  }
done

for required in "$COUNTY_PARQUET" "$POPULATION_PARQUET"; do
  [[ -f "$required" ]] || {
    echo "error: $required not found." >&2
    if [[ "$MODE" == "fixtures" ]]; then
      echo "  build it first: tools/census/convert.sh --fixtures" >&2
    else
      echo "  build it first: tools/census/download.sh && tools/census/convert.sh" >&2
    fi
    exit 1
  }
done

# The bridge side. Fixture mode reads a committed CSV of hand-placed points so the golden tests and
# CI need no database at all; real mode reads the serving table through DuckDB's postgres scanner.
# Both produce the identical six-column contract county-join.sql declares, which is what lets the
# same SQL be tested and run.
if [[ "$MODE" == "fixtures" ]]; then
  [[ -f "$FIXTURE_POINTS" ]] || { echo "error: $FIXTURE_POINTS not found." >&2; exit 1; }
  SOURCE_DESCRIPTION="$FIXTURE_POINTS"
  BRIDGE_SQL="CREATE OR REPLACE TABLE bridge_source AS
    SELECT CAST(state_code AS VARCHAR) AS state_code,
           CAST(structure_number AS VARCHAR) AS structure_number,
           CAST(record_type AS VARCHAR) AS record_type,
           CAST(county_code AS VARCHAR) AS county_code,
           CAST(lon AS DOUBLE) AS lon,
           CAST(lat AS DOUBLE) AS lat
    FROM read_csv('$FIXTURE_POINTS', all_varchar=true);"
else
  SOURCE_DESCRIPTION="core.bridge (serving database)"
  # ST_X/ST_Y run server-side so the geometry never has to cross the wire in a PostGIS-specific
  # binary format the scanner would have to understand.
  BRIDGE_SQL="INSTALL postgres; LOAD postgres;
    ATTACH '$CONNECTION' AS pg (TYPE postgres, READ_ONLY);
    CREATE OR REPLACE TABLE bridge_source AS
    SELECT * FROM postgres_query('pg', 'SELECT state_code, structure_number, record_type, county_code,
                                        ST_X(location) AS lon, ST_Y(location) AS lat FROM core.bridge');"
fi

mkdir -p "$OUT_DIR"

RUN_ID="county-join-$(date -u +%Y%m%dT%H%M%SZ)"
if [[ "$MODE" != "real" ]]; then
  CATALOG_SHA="$MODE"
elif command -v sha256sum >/dev/null 2>&1; then
  CATALOG_SHA="$(sha256sum "$CATALOG" | cut -d' ' -f1)"          # GNU (Linux)
else
  CATALOG_SHA="$(shasum -a 256 "$CATALOG" | cut -d' ' -f1)"      # BSD (macOS)
fi

echo "==> bridges  $SOURCE_DESCRIPTION"
echo "    counties $COUNTY_PARQUET"
echo "    run id   $RUN_ID"
echo "    catalog  ${CATALOG_SHA:0:16}…"

# A temp *directory*, not `mktemp -t <name>`: BSD mktemp accepts a suffix-less template but GNU
# mktemp requires X's in it, so the -t form fails on Linux (the portability fix the sibling jobs
# needed). The spatial extension does not autoload and the axis order has to be pinned, so the
# prelude is part of the generated init rather than an assumption about the caller's session.
INIT_DIR="$(mktemp -d)"
INIT="$INIT_DIR/init.sql"
trap 'rm -rf "$INIT_DIR"' EXIT
{
  echo "INSTALL spatial; LOAD spatial; SET geometry_always_xy = true;"
  echo "$BRIDGE_SQL"
  echo "CREATE OR REPLACE TABLE county_source AS
        SELECT GEOID AS geoid, STATEFP AS statefp, COUNTYFP AS countyfp, NAME AS name,
               NAMELSAD AS namelsad, ALAND AS aland, AWATER AS awater,
               TIGER_VINTAGE AS tiger_vintage, geom
        FROM read_parquet('$COUNTY_PARQUET');"
  echo "CREATE OR REPLACE TABLE population_source AS
        SELECT GEOID AS geoid, POP_EST AS pop_est, POP_MOE AS pop_moe,
               ACS_VINTAGE AS acs_vintage, ACS_PERIOD AS acs_period, ACS_TABLE AS acs_table
        FROM read_parquet('$POPULATION_PARQUET');"
  # The spatial join is evaluated by several views; materialising it once keeps a national run to a
  # single pass over 741k points instead of one per consumer.
  cat tools/census/county-join.sql
  echo "CREATE OR REPLACE TABLE cj_assignment_mat AS SELECT * FROM cj_assignment;"
  echo "CREATE OR REPLACE VIEW cj_assignment AS SELECT * FROM cj_assignment_mat;"
} > "$INIT"

# The checks run *before* any output is written, so a failing invariant never leaves a half-published
# join behind for the loader to pick up.
echo "==> checking invariants"
# stderr is kept, not discarded. The sibling jobs send it to /dev/null, and the failure mode that
# hides is nasty: a SQL error makes the command substitution fail, `set -e` ends the script, and the
# operator sees "checking invariants" followed by nothing at all — no error, no exit message, and an
# exit status they have to go looking for. A job that cannot evaluate its own invariants has to say
# so as loudly as one that violates them.
CHECK_ERR="$INIT_DIR/checks.err"
if ! CHECKS="$(duckdb -noheader -list -init "$INIT" -c "
  SELECT 'assignment is unique',      count(*) FROM cj_check_assignment_unique
  UNION ALL SELECT 'assigned + missed reconciles', count(*) FROM cj_check_reconciles
  UNION ALL SELECT 'coverage totals',   count(*) FROM cj_check_coverage_totals
  UNION ALL SELECT 'sign convention',   count(*) FROM cj_check_sign_convention
  UNION ALL SELECT 'over-long code',    count(*) FROM cj_check_overlong_code
  UNION ALL SELECT 'county key shape',  count(*) FROM cj_check_county_key
  UNION ALL SELECT 'miss reasons',      count(*) FROM cj_check_miss_reason
  UNION ALL SELECT 'disagreements resolve', count(*) FROM cj_check_disagreement_resolves;" 2>"$CHECK_ERR")"; then
  echo "error: the invariant queries did not run — nothing was written." >&2
  grep -v '^-- Loading resources from' "$CHECK_ERR" | sed 's/^/    /' >&2
  exit 1
fi

# An empty result is not a pass. If the query returned no rows the loop below would never execute,
# FAILED would stay 0, and the job would write its outputs having checked nothing.
[[ -n "$CHECKS" ]] || {
  echo "error: the invariant queries returned no rows — nothing was checked, nothing written." >&2
  exit 1
}

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
(( FAILED )) && { echo "error: join not written — invariants failed above." >&2; exit 1; }

echo "==> writing join to $OUT_DIR"
duckdb -init "$INIT" -c "
  CREATE OR REPLACE TABLE county_out AS SELECT * FROM cj_county_out ORDER BY county_fips;
  CREATE OR REPLACE TABLE miss_out AS
    SELECT * FROM cj_miss ORDER BY state_code, structure_number, record_type;
  CREATE OR REPLACE TABLE disagreement_out AS
    SELECT * FROM cj_disagreement ORDER BY nbi_county_fips, county_fips;
  COPY county_out       TO '$OUT_DIR/county.parquet'       (FORMAT PARQUET, COMPRESSION ZSTD);
  COPY miss_out         TO '$OUT_DIR/miss.parquet'         (FORMAT PARQUET, COMPRESSION ZSTD);
  COPY disagreement_out TO '$OUT_DIR/disagreement.parquet' (FORMAT PARQUET, COMPRESSION ZSTD);
  COPY county_out       TO '$OUT_DIR/county.csv'       (FORMAT CSV, HEADER true);
  COPY miss_out         TO '$OUT_DIR/miss.csv'         (FORMAT CSV, HEADER true);
  COPY disagreement_out TO '$OUT_DIR/disagreement.csv' (FORMAT CSV, HEADER true);
" >/dev/null

echo "==> recording $OUT_DIR/manifest.json"
duckdb -noheader -list -init "$INIT" -c "
  COPY (SELECT * FROM cj_coverage) TO '$OUT_DIR/coverage.json' (FORMAT JSON, ARRAY true);
  COPY (SELECT * FROM cj_method)   TO '$OUT_DIR/method.json'   (FORMAT JSON, ARRAY true);
  COPY (SELECT * FROM cj_diagnostic_miss_distance ORDER BY bucket)
    TO '$OUT_DIR/miss-distance.json' (FORMAT JSON, ARRAY true);
  COPY (SELECT * FROM cj_diagnostic_disagreement_by_state ORDER BY bridges DESC)
    TO '$OUT_DIR/disagreement-by-state.json' (FORMAT JSON, ARRAY true);
  COPY (SELECT * FROM cj_diagnostic_retired_codes ORDER BY bridges DESC)
    TO '$OUT_DIR/retired-codes.json' (FORMAT JSON, ARRAY true);
  COPY (SELECT * FROM cj_diagnostic_population_absent ORDER BY state_fips)
    TO '$OUT_DIR/population-absent.json' (FORMAT JSON, ARRAY true);
" >/dev/null

python3 - "$OUT_DIR" "$RUN_ID" "$CATALOG_SHA" "$SOURCE_DESCRIPTION" "$COUNTY_PARQUET" <<'PY'
import json, os, subprocess, sys

out_dir, run_id, catalog_sha, source, county_parquet = sys.argv[1:6]

def q(sql):
    r = subprocess.run(["duckdb", "-noheader", "-list", "-c", sql], capture_output=True, text=True)
    if r.returncode:
        sys.exit(r.stderr.strip())
    return r.stdout.strip()

def take(name):
    path = os.path.join(out_dir, name)
    with open(path) as fh:
        value = json.load(fh)
    os.remove(path)
    return value

coverage = take("coverage.json")[0]
method = take("method.json")[0]
miss_distance = take("miss-distance.json")
disagreement_by_state = take("disagreement-by-state.json")
retired_codes = take("retired-codes.json")
population_absent = take("population-absent.json")

# Counted off the written Parquet rather than from the views, so the manifest describes the artefact
# the loader will actually read. The loader re-checks these against the CSVs and refuses a mismatch.
counties, counties_without_population = q(
    f"SELECT count(*), count(*) FILTER (population IS NULL) "
    f"FROM read_parquet('{out_dir}/county.parquet')").split("|")
misses = q(f"SELECT count(*) FROM read_parquet('{out_dir}/miss.parquet')")
disagreements, disagreement_bridges, disagreement_structures = q(
    f"SELECT count(*), coalesce(sum(bridges), 0), coalesce(sum(structures), 0) "
    f"FROM read_parquet('{out_dir}/disagreement.parquet')").split("|")

bridges = int(coverage["bridges"])
matched = int(coverage["matched"])
structures = int(coverage["structures"])
structures_matched = int(coverage["structures_matched"])

manifest = {
    "schemaVersion": 1,
    "runId": run_id,
    "catalogSha256": catalog_sha,
    "source": source,
    "countySource": county_parquet,
    # The published constants, carried with the numbers they governed (county-join.sql §0).
    "methodVersion": method["method_version"],
    "containmentPredicate": method["containment_predicate"],
    "nearestSearchDegrees": float(method["nearest_search_degrees"]),
    "minStateAgreementShare": float(method["min_state_agreement_share"]),
    "bridges": bridges,
    "matched": matched,
    "unmatched": int(coverage["unmatched"]),
    "structures": structures,
    "structuresMatched": structures_matched,
    "agree": int(coverage["agree"]),
    "differentCountySameState": int(coverage["different_county_same_state"]),
    "differentState": int(coverage["different_state"]),
    "countyNotPublished": int(coverage["county_not_published"]),
    "counties": int(counties),
    "countiesWithoutPopulation": int(counties_without_population),
    "misses": int(misses),
    "disagreements": int(disagreements),
    "disagreementBridges": int(disagreement_bridges),
    "disagreementStructures": int(disagreement_structures),
    "missDistance": miss_distance,
    "disagreementByState": disagreement_by_state,
    "retiredCodes": retired_codes,
    "populationAbsent": population_absent,
}
with open(os.path.join(out_dir, "manifest.json"), "w") as fh:
    json.dump(manifest, fh, indent=2)
    fh.write("\n")

def pct(part, whole):
    return f"{100.0 * part / whole:.4f}%" if whole else "n/a"

print(f"    {bridges:,} served rows  |  {matched:,} matched ({pct(matched, bridges)})  |  "
      f"{int(coverage['unmatched']):,} quarantined")
print(f"    {structures:,} record-type-1 structures  |  {structures_matched:,} matched "
      f"({pct(structures_matched, structures)})")
print(f"    {manifest['counties']:,} counties published  |  "
      f"{manifest['countiesWithoutPopulation']} with no ACS population row")
print(f"    cross-check against item 3: {manifest['agree']:,} agree "
      f"({pct(manifest['agree'], matched)})  |  "
      f"{manifest['differentCountySameState']:,} another county of the same state  |  "
      f"{manifest['differentState']:,} another state  |  "
      f"{manifest['countyNotPublished']:,} no code published")
for row in miss_distance:
    print(f"    miss  {row['bucket']:<36} {int(row['misses']):>6,}")
if retired_codes:
    retired_bridges = sum(int(r["bridges"]) for r in retired_codes)
    retired_structures = sum(int(r["structures"]) for r in retired_codes)
    print(f"    {len(retired_codes)} published county code(s) are absent from the county source, "
          f"covering {retired_bridges:,} served rows ({retired_structures:,} record-type-1 structures).")
    print("      Against the national boundary file that means a retired code — those disagree")
    print("      because the code names a county TIGER no longer publishes, not because the")
    print("      coordinate is wrong. Against a fixture it also catches current counties the")
    print("      fixture simply does not contain (county-join.sql §5).")
print("    ^ a disagreement is a measurement, never a correction: item 3 stays the county the")
print("      product reports, and this job publishes how often the coordinate says otherwise (GR-6).")
PY

echo "==> Done. Load it into the serving database with:"
echo "    dotnet run --project src/SpanSight.Ingestion -- load-county-join --file $OUT_DIR"
