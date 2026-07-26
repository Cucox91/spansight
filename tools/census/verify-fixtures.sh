#!/usr/bin/env bash
# tools/census/verify-fixtures.sh — FR-1.5 AC-1: assert the staged Census outputs are actually usable.
#
# Usage:
#   tools/census/convert.sh --fixtures && tools/census/verify-fixtures.sh    # what CI runs
#
# convert.sh already fails loudly on the things it can see for itself — feature counts, malformed
# GEOIDs, join-key coverage. This asserts the two properties that matter downstream and that a
# silent regression would otherwise carry all the way to W5:
#
#   1. the boundaries really do answer point-in-polygon, on real coordinates
#   2. the population really is keyed by 5-digit FIPS *as text*, leading zero intact
#
# There is no .NET here on purpose: the Census pipeline is DuckDB end to end (convert.sh explains
# why), so its tests are too. Requires: duckdb with spatial.

set -euo pipefail

cd "$(dirname "$0")/../.."

OUT_DIR="data/census/fixtures-out/parquet"
COUNTIES="$OUT_DIR/counties.parquet"
POPULATION="$OUT_DIR/county_population.parquet"

for f in "$COUNTIES" "$POPULATION"; do
  [[ -f "$f" ]] || { echo "error: $f missing — run tools/census/convert.sh --fixtures first." >&2; exit 1; }
done

fail() { echo "FAIL: $1" >&2; exit 1; }

check() {
  local what="$1" expected="$2" sql="$3" actual
  actual="$(duckdb -noheader -list -c "LOAD spatial; SET geometry_always_xy = true; $sql")"
  if [[ "$actual" != "$expected" ]]; then
    fail "$what — expected '$expected', got '$actual'"
  fi
  echo "  ok  $what = $actual"
}

echo "==> point-in-polygon"

# Real coordinates in four of the six fixture counties. If the geometry were dropped, reprojected
# into the wrong axis order, or written without a CRS, these stop matching — which is exactly the
# class of bug that is invisible in a row count.
check "downtown LA is in 06037" "06037" \
  "SELECT GEOID FROM read_parquet('$COUNTIES') WHERE ST_Contains(geom, ST_Point(-118.2437, 34.0522));"

check "Miami is in 12086" "12086" \
  "SELECT GEOID FROM read_parquet('$COUNTIES') WHERE ST_Contains(geom, ST_Point(-80.1637, 25.7876));"

check "the Chicago Loop is in 17031" "17031" \
  "SELECT GEOID FROM read_parquet('$COUNTIES') WHERE ST_Contains(geom, ST_Point(-87.6298, 41.8781));"

# Longitude sign is the classic NBI trap — item 017 is published unsigned and west-positive, so a
# missed negation puts every American bridge in Asia. A positive-longitude twin of the LA point
# must match nothing.
check "no county at the mirrored +118 longitude" "0" \
  "SELECT count(*) FROM read_parquet('$COUNTIES') WHERE ST_Contains(geom, ST_Point(118.2437, 34.0522));"

check "no county in the mid-Atlantic" "0" \
  "SELECT count(*) FROM read_parquet('$COUNTIES') WHERE ST_Contains(geom, ST_Point(-40.0, 30.0));"

echo "==> FIPS keying"

# '06037' must survive as text. If any step ever casts it, this returns 6037 and the join to NBI
# state/county codes — which are also text — silently matches nothing.
check "GEOIDs are 5-character text" "true" \
  "SELECT bool_and(length(GEOID) = 5) FROM read_parquet('$POPULATION');"

check "the leading zero on 06037 survived" "06037" \
  "SELECT GEOID FROM read_parquet('$POPULATION') WHERE POP_EST = 9808667;"

check "boundaries and population agree on GEOID text" "5" \
  "SELECT count(*) FROM read_parquet('$COUNTIES') c JOIN read_parquet('$POPULATION') p USING (GEOID);"

echo "==> summary-level filter"

# The fixture deliberately carries 280 non-county rows whose GEO_ID ends in one of the same five
# FIPS codes (see make-fixtures.sh). A suffix-based filter would pull them in; the prefix one does
# not. 5, not 285, is the whole point of this assertion.
check "only county summary-level rows were kept" "5" \
  "SELECT count(*) FROM read_parquet('$POPULATION');"

echo "==> ACS jam values"

# ACS encodes "not available" as large negative sentinels. A population of -555555555 stored as a
# number ruins any average that touches it, so those must be NULL, never negative.
check "no negative populations survived" "0" \
  "SELECT count(*) FROM read_parquet('$POPULATION') WHERE POP_EST < 0 OR POP_MOE < 0;"

check "Loving County TX kept its real population of 33" "33" \
  "SELECT POP_EST FROM read_parquet('$POPULATION') WHERE GEOID = '48301';"

echo "==> all checks passed"
