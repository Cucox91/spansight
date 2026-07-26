#!/usr/bin/env bash
# tools/census/make-fixtures.sh — cut the committed Census fixtures from the real downloads.
#
# Usage:
#   tools/census/download.sh          # first — the fixtures are carved from the real files
#   tools/census/make-fixtures.sh
#
# Writes src/tests/fixtures/census/, which IS committed:
#   tl_<tiger>_us_county_sample.zip        a real ESRI Shapefile of six counties
#   acsdt5y<acs>-b01003_sample.dat         the matching ACS rows, same pipe-delimited shape
#
# Six counties, chosen so the fixture exercises the cases that actually break a county join rather
# than six arbitrary rows (`convert.sh --fixtures` is what CI runs):
#
#   06037  Los Angeles CA   largest population (9.8M) — the top of the range
#   12086  Miami-Dade FL    from the federal TIGER file, never a state DOT source (GR-1)
#   17031  Cook IL          a second large metro, ordinary case
#   48301  Loving TX        smallest population in the country (33) — the bottom of the range
#   72001  Adjuntas PR      a Puerto Rico municipio: in ACS, but outside the national total
#   60010  Eastern AS       American Samoa: has a boundary, has NO ACS row at all
#
# The last two are the point. ACS 5-year covers the 50 states + DC + PR, but not American Samoa,
# Guam, the Northern Marianas or the US Virgin Islands — so a full run has 3,235 boundaries against
# 3,222 population rows, and 13 counties legitimately match nothing. A fixture of six well-behaved
# mainland counties would let a regression that drops unmatched boundaries pass CI untouched.
#
# Deterministic: same inputs, same bytes out. Re-running must not churn the committed files, so the
# zip is built with a fixed timestamp and no extra attributes.
#
# Requires: duckdb (with spatial), zip, python3.

set -euo pipefail

cd "$(dirname "$0")/../.."

RAW_DIR="data/census/raw"
FIXTURE_DIR="src/tests/fixtures/census"

# Keep in sync with the comment block above.
GEOIDS="'06037','12086','17031','48301','72001','60010'"

for tool in duckdb zip python3; do
  command -v "$tool" >/dev/null 2>&1 || { echo "error: $tool not found on PATH." >&2; exit 1; }
done

BOUNDARY_ZIP="$(find "$RAW_DIR" -maxdepth 1 -name 'tl_*_us_county.zip' | sort | head -1)"
POP_FILE="$(find "$RAW_DIR" -maxdepth 1 -name 'acsdt5y*-b01003.dat' | sort | head -1)"
if [[ -z "$BOUNDARY_ZIP" || -z "$POP_FILE" ]]; then
  echo "error: no TIGER zip / ACS .dat under $RAW_DIR — run tools/census/download.sh first." >&2
  exit 1
fi

TIGER_VINTAGE="$(basename "$BOUNDARY_ZIP" | sed -n 's/^tl_\([0-9]\{4\}\)_us_county.*$/\1/p')"
ACS_VINTAGE="$(basename "$POP_FILE" | sed -n 's/^acsdt5y\([0-9]\{4\}\)-b01003.*$/\1/p')"

INNER_SHP="$(unzip -Z1 "$BOUNDARY_ZIP" | grep '\.shp$')"
VSI="/vsizip/$PWD/$BOUNDARY_ZIP/$INNER_SHP"

mkdir -p "$FIXTURE_DIR"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

BASE="tl_${TIGER_VINTAGE}_us_county_sample"

echo "==> boundaries  6 counties from $(basename "$BOUNDARY_ZIP")"
# Real geometry, not simplified: the fixture is the polygon side of a point-in-polygon join, and a
# generalized outline would quietly change which points fall inside it.
duckdb -c "INSTALL spatial; LOAD spatial; SET geometry_always_xy = true;
  COPY (SELECT * FROM ST_Read('$VSI') WHERE GEOID IN ($GEOIDS))
  TO '$STAGE/$BASE.shp' (FORMAT GDAL, DRIVER 'ESRI Shapefile');"

# -X drops extra file attributes and the fixed mtime kills the timestamp churn, so re-running this
# script produces byte-identical output and never shows up as a spurious diff.
find "$STAGE" -type f -exec touch -t 202607260000 {} +
(cd "$STAGE" && zip -q -X "$BASE.zip" "$BASE".shp "$BASE".shx "$BASE".dbf "$BASE".prj)
mv "$STAGE/$BASE.zip" "$FIXTURE_DIR/$BASE.zip"

echo "==> population  matching rows from $(basename "$POP_FILE")"
POP_OUT="$FIXTURE_DIR/acsdt5y${ACS_VINTAGE}-b01003_sample.dat"
python3 - "$POP_FILE" "$POP_OUT" <<'PY'
import sys

src, out = sys.argv[1:3]
wanted = {"06037", "12086", "17031", "48301", "72001"}   # 60010 has no ACS row — that is the point

kept = []
with open(src, encoding="utf-8") as fh:
    header = fh.readline().rstrip("\n")
    for line in fh:
        geo = line.split("|", 1)[0]
        county = geo.startswith("0500000US") and geo[9:] in wanted
        # Nation and two states, so a converter that forgot to filter by summary level at all shows
        # up as extra rows rather than as a silently larger population.
        rollup = geo == "0100000US" or geo in ("0400000US06", "0400000US48")
        # Decoys, and the reason this fixture is worth more than five county rows: other summary
        # levels (place-in-county, PUMA, ZCTA-ish) whose GEO_ID *ends* in one of the same five FIPS
        # codes. Filtering on a '0500000US' prefix excludes them; filtering on a trailing-5-chars
        # suffix — the tempting shortcut — silently double-counts these into the county totals.
        decoy = not county and geo[-5:] in wanted
        if county or rollup or decoy:
            kept.append(line.rstrip("\n"))

kept.sort()
with open(out, "w", encoding="utf-8") as fh:
    fh.write(header + "\n")
    for line in kept:
        fh.write(line + "\n")
print(f"    {len(kept)} rows ({sum(1 for k in kept if k.startswith('0500000US'))} counties)")
PY

echo "==> wrote:"
ls -l "$FIXTURE_DIR"
