#!/usr/bin/env bash
# tools/census/convert.sh — FR-1.5 AC-1: convert the staged Census sources to analysis-ready Parquet.
#
# Usage:
#   tools/census/convert.sh              # both sources, from data/census/raw/
#   tools/census/convert.sh --fixtures   # the committed fixtures (fast; what CI runs)
#
# Shape: DuckDB does all of it. Unlike the NBI vintages — where the .NET CLI owns the parse because
# every era quirk needs tested C# — Census publishes two clean, stable, well-documented formats, so
# there is nothing here for a domain layer to own. Adding one would be two implementations of a
# shapefile read (ADR-005 keeps analytics in DuckDB for exactly this reason).
#
# Produces, under data/census/parquet/ (gitignored):
#   counties.parquet            one row per county / county equivalent, with a GEOMETRY column in
#                               lon/lat WGS84 — the polygon side of the W5 point-in-polygon join
#   county_population.parquet   one row per county, keyed by the 5-digit GEOID, carrying the ACS
#                               estimate *and* its margin of error
# and adds the conversion + reconciliation record to tools/census/catalog.json (committed).
#
# Bridges gain nothing here. This is staging only: the spatial join, the join-coverage metric and
# the population-served figures are W5 (FR-1.5 AC-2/AC-3). Nothing in this script reads bridge data.
#
# Requires: duckdb (brew install duckdb) with the spatial extension, python3, unzip.

set -euo pipefail

cd "$(dirname "$0")/../.."

RAW_DIR="data/census/raw"
OUT_DIR="data/census"
FIXTURE_DIR="src/tests/fixtures/census"
CATALOG="tools/census/catalog.json"
TOOL_VERSION="1.0.0"

usage() { sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//'; }

FIXTURES=0
for arg in "$@"; do
  case "$arg" in
    --fixtures) FIXTURES=1 ;;
    --help|-h) usage; exit 0 ;;
    *) echo "error: unrecognised argument '$arg'." >&2; usage >&2; exit 2 ;;
  esac
done

for tool in duckdb python3 unzip; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "error: $tool not found on PATH." >&2
    [[ "$tool" == "duckdb" ]] && echo "  install: brew install duckdb" >&2
    exit 1
  fi
done

# macOS ships bash 3.2 — no mapfile, no associative arrays. Everything below is 3.2-safe.
one_file() {
  local what="$1" pattern="$2" dir="$3" hint="$4"
  local found count
  found="$(find "$dir" -maxdepth 1 -name "$pattern" 2>/dev/null | sort)"
  count="$(printf '%s' "$found" | grep -c . || true)"
  if [[ "$count" != "1" ]]; then
    echo "error: expected exactly one $what matching '$pattern' under $dir/, found $count." >&2
    echo "  $hint" >&2
    exit 1
  fi
  printf '%s\n' "$found"
}

if (( FIXTURES )); then
  # Fixtures write to their own tree and never touch the real catalog — a CI run must not be able
  # to overwrite the provenance record for data it never downloaded.
  OUT_DIR="data/census/fixtures-out"
  CATALOG=""
  BOUNDARY_ZIP="$(one_file 'boundary fixture' 'tl_*_us_county_sample.zip' "$FIXTURE_DIR" 'the fixtures are committed — check out the repo cleanly')"
  POP_FILE="$(one_file 'population fixture' 'acsdt5y*-b01003_sample.dat' "$FIXTURE_DIR" 'the fixtures are committed — check out the repo cleanly')"
else
  BOUNDARY_ZIP="$(one_file 'TIGER county archive' 'tl_*_us_county.zip' "$RAW_DIR" 'run tools/census/download.sh (delete older vintages you no longer want)')"
  POP_FILE="$(one_file 'ACS 5-year B01003 file' 'acsdt5y*-b01003.dat' "$RAW_DIR" 'run tools/census/download.sh (delete older vintages you no longer want)')"
fi

# The vintage is whatever is on disk, read back off the filename, so the catalog can never claim a
# vintage that was not the one converted. tl_<YYYY>_us_county… and acsdt5y<YYYY>-b01003… .
TIGER_VINTAGE="$(basename "$BOUNDARY_ZIP" | sed -n 's/^tl_\([0-9]\{4\}\)_us_county.*$/\1/p')"
ACS_VINTAGE="$(basename "$POP_FILE" | sed -n 's/^acsdt5y\([0-9]\{4\}\)-b01003.*$/\1/p')"
if [[ -z "$TIGER_VINTAGE" || -z "$ACS_VINTAGE" ]]; then
  echo "error: could not read a vintage out of $(basename "$BOUNDARY_ZIP") / $(basename "$POP_FILE")." >&2
  exit 1
fi

# GDAL reads the shapefile straight out of the zip through its /vsizip/ virtual filesystem, so
# nothing is ever unpacked to disk — the .shp alone is 132 MB. The inner .shp must be named
# explicitly: handed a bare zip path GDAL silently opens layer 0, which is right today and wrong
# the day Census ships a second layer in the same archive. '\.shp$' so the two .shp.*.iso.xml
# metadata members do not match. Listed into a variable rather than piped into grep, because under
# `set -o pipefail` grep exiting early SIGPIPEs unzip and the pipeline fails intermittently.
ZIP_MEMBERS="$(unzip -Z1 "$BOUNDARY_ZIP")"
INNER_SHP="$(printf '%s\n' "$ZIP_MEMBERS" | grep '\.shp$' || true)"
if [[ "$(printf '%s\n' "$INNER_SHP" | grep -c . || true)" != "1" ]]; then
  echo "error: $BOUNDARY_ZIP holds $(printf '%s\n' "$INNER_SHP" | grep -c . || true) .shp members, expected 1:" >&2
  printf '%s\n' "$ZIP_MEMBERS" >&2
  exit 1
fi
VSI="/vsizip/$PWD/$BOUNDARY_ZIP/$INNER_SHP"

# `INSTALL` needs the network once (~29 MB from extensions.duckdb.org) and is a silent no-op against
# a warm ~/.duckdb/extensions cache; `LOAD` never touches the network. Both are spelled out in every
# duckdb call below because autoload does *not* fire for spatial — a bare ST_Point in a clean
# environment fails with "not in the catalog, but it exists in the spatial extension".
#
# geometry_always_xy pins the future DuckDB default: coordinates are read as [easting, northing]
# regardless of what the CRS says its axis order is. TIGER's NAD83 declares [latitude, longitude],
# so without this every ST_Transform prints a multi-line deprecation warning and the interpretation
# is one release away from flipping. NBI items 016/017, PMTiles and PostGIS are all lon/lat.
DUCKDB_PRELUDE="INSTALL spatial; LOAD spatial; SET geometry_always_xy = true;"

mkdir -p "$OUT_DIR/parquet"
COUNTIES_PARQUET="$OUT_DIR/parquet/counties.parquet"
POPULATION_PARQUET="$OUT_DIR/parquet/county_population.parquet"

echo "==> boundaries  $(basename "$BOUNDARY_ZIP")  (TIGER $TIGER_VINTAGE)"

# Read the layer metadata before reading a single feature. A shapefile missing its .prj sidecar
# still opens fine and its geometry silently defaults to OGC:CRS84 downstream — the reprojection
# below would then be a no-op that *asserts* a datum the data never had, which no test would catch.
# So the source CRS is checked, not assumed, and a surprise is a stop rather than a footnote.
META="$(duckdb -noheader -list -c "$DUCKDB_PRELUDE
  WITH m AS (SELECT unnest(layers) AS l FROM ST_Read_Meta('$VSI'))
  SELECT l.name, l.feature_count, coalesce(gf.crs.auth_name, '?') || ':' || coalesce(gf.crs.auth_code, '?')
  FROM m, unnest(m.l.geometry_fields) AS t(gf);")"

if [[ "$(printf '%s\n' "$META" | grep -c . || true)" != "1" ]]; then
  echo "error: expected one geometry layer in $BOUNDARY_ZIP, got:" >&2
  printf '%s\n' "$META" >&2
  exit 1
fi
LAYER_NAME="$(printf '%s' "$META" | cut -d'|' -f1)"
FEATURES_READ="$(printf '%s' "$META" | cut -d'|' -f2)"
SOURCE_CRS="$(printf '%s' "$META" | cut -d'|' -f3)"
if [[ "$SOURCE_CRS" != "EPSG:4269" ]]; then
  echo "error: $BOUNDARY_ZIP declares CRS $SOURCE_CRS, expected EPSG:4269 (NAD83 geographic)." >&2
  echo "  TIGER/Line has shipped NAD83 for the whole published series; a different CRS means the" >&2
  echo "  reprojection below is wrong, so this stops rather than writing mislabelled geometry." >&2
  exit 1
fi
echo "    layer $LAYER_NAME  $FEATURES_READ features  $SOURCE_CRS"

# Reproject once, here, at write time — the analysis artifact is the right place for it, and the
# alternative (ST_SetCRS, or leaving it NAD83 and hoping) is how a datum error survives to
# production. SpanSight stores points as geometry(Point, 4326) everywhere (SpanSightDbContext,
# LoadPipeline, BridgeRowValidator), so the staged polygons must arrive on the same reference.
#
# Be honest about what this actually moves: PROJ has no NAD83->WGS84 grid shift bundled, so it
# applies a ballpark (identity) transformation and the coordinates do not change at six decimal
# places. What changes is the *declared* CRS, which is the part that matters — DuckDB's binder
# refuses to compare an EPSG:4269 geometry with a WGS84 one, and that refusal is the safety net.
# The written GeoParquet carries no explicit crs key, which per GeoParquet 1.0 means OGC:CRS84:
# lon/lat WGS84, i.e. what PostGIS calls SRID 4326 and what MapLibre expects.
#
# Every FIPS-bearing column stays VARCHAR, exactly as the NBI vintages do: county code '037' is not
# the number 37, and the moment one becomes an integer the leading zero is gone and '06' + '037'
# joins as '637'. ALAND/AWATER are genuine quantities (square metres) and stay integers.
# INTPTLAT/INTPTLON are kept as published text — they are NAD83 like the source polygons were, so
# anything that wants them on WGS84 has to transform them the same way this does, deliberately.
echo "    writing $(basename "$COUNTIES_PARQUET")"
duckdb -c "$DUCKDB_PRELUDE
  COPY (
    SELECT
      GEOID, STATEFP, COUNTYFP, COUNTYNS, GEOIDFQ,
      NAME, NAMELSAD, LSAD, CLASSFP, MTFCC, FUNCSTAT,
      CSAFP, CBSAFP, METDIVFP,
      ALAND, AWATER, INTPTLAT, INTPTLON,
      CAST($TIGER_VINTAGE AS SMALLINT) AS TIGER_VINTAGE,
      ST_Transform(geom, '$SOURCE_CRS', 'EPSG:4326') AS geom
    FROM ST_Read('$VSI')
  ) TO '$COUNTIES_PARQUET' (FORMAT PARQUET, COMPRESSION ZSTD);"

echo "==> population  $(basename "$POP_FILE")  (ACS 5-year $ACS_VINTAGE, $(( ACS_VINTAGE - 4 ))-$ACS_VINTAGE)"
echo "    writing $(basename "$POPULATION_PARQUET")"

# The bulk summary file is pipe-delimited and holds every summary level at once — nation, states,
# counties, tracts, block groups: 616,690 rows for 3,222 counties. Counties are summary level 050,
# which the GEO_ID prefix '0500000US' encodes, so the filter is a prefix test and not a join.
#
# substr(GEO_ID, 10) drops that 9-character prefix and leaves the 5-digit FIPS *as text*, which is
# the whole zero-padding defence: the string '01001' can never lose its leading zero the way
# CAST('01' AS INT) || CAST('001' AS INT) would. The reconciliation below proves it — every GEOID
# on both sides is asserted to be 5 characters and to equal lpad(state,2,'0') || lpad(county,3,'0').
#
# Jam values: ACS encodes "not applicable"/"not available" as large negative sentinels
# (-555555555 here, and -666666666 / -999999999 in other tables, in estimate columns too). A plain
# CAST stores them as populations and a later average silently ruins itself, so anything negative
# becomes NULL and the count is recorded in the catalog rather than absorbed. Same TRY_CAST
# discipline as the vintage condition columns.
#
# The margin of error is carried, not discarded. ACS publishes survey estimates, not counts; a
# bridges-per-capita figure divides a near-census count by a sampled estimate, and GR-6 only
# permits that if the uncertainty travels with it (FR-1.5 AC-3).
duckdb -c "$DUCKDB_PRELUDE
  COPY (
    SELECT
      substr(GEO_ID, 10)                                    AS GEOID,
      GEO_ID                                                AS GEOIDFQ,
      CASE WHEN TRY_CAST(B01003_E001 AS BIGINT) >= 0 THEN TRY_CAST(B01003_E001 AS BIGINT) END AS POP_EST,
      CASE WHEN TRY_CAST(B01003_M001 AS BIGINT) >= 0 THEN TRY_CAST(B01003_M001 AS BIGINT) END AS POP_MOE,
      CAST($ACS_VINTAGE AS SMALLINT)                        AS ACS_VINTAGE,
      '$(( ACS_VINTAGE - 4 ))-$ACS_VINTAGE'                 AS ACS_PERIOD,
      'B01003'                                              AS ACS_TABLE
    FROM read_csv('$POP_FILE', delim='|', header=true, all_varchar=true)
    WHERE GEO_ID LIKE '0500000US%'
  ) TO '$POPULATION_PARQUET' (FORMAT PARQUET, COMPRESSION ZSTD);"

echo "==> reconciling"
python3 - "$CATALOG" "$COUNTIES_PARQUET" "$POPULATION_PARQUET" "$POP_FILE" \
         "$FEATURES_READ" "$TIGER_VINTAGE" "$ACS_VINTAGE" "$TOOL_VERSION" "$FIXTURES" <<'PY'
import datetime, json, os, subprocess, sys

(catalog_path, counties, population, pop_src,
 features_read, tiger_vintage, acs_vintage, tool_version, fixtures) = sys.argv[1:10]
features_read, fixtures = int(features_read), fixtures == "1"


def q(sql):
    """One scalar out of DuckDB. Spatial is loaded because the geometry column needs it."""
    out = subprocess.run(
        ["duckdb", "-noheader", "-list", "-c", "INSTALL spatial; LOAD spatial; " + sql],
        capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f"error: reconciliation query failed\n{out.stderr}")
    return out.stdout.strip()


C = f"read_parquet('{counties}')"
P = f"read_parquet('{population}')"
S = f"read_csv('{pop_src}', delim='|', header=true, all_varchar=true)"

boundaries = {
    "vintage": int(tiger_vintage),
    "featuresInSource": features_read,
    "rowsWritten": int(q(f"SELECT count(*) FROM {C}")),
    "distinctGeoids": int(q(f"SELECT count(DISTINCT GEOID) FROM {C}")),
    "distinctStateFips": int(q(f"SELECT count(DISTINCT STATEFP) FROM {C}")),
    "nullGeometry": int(q(f"SELECT count(*) FROM {C} WHERE geom IS NULL")),
    # The zero-padding assertion, run against every row rather than trusted: the published GEOID
    # must equal the two FIPS parts padded and concatenated as strings. This is the exact
    # expression the W5 bridge-side key has to use on NBI items 001/003.
    "geoidMalformed": int(q(
        f"SELECT count(*) FROM {C} WHERE length(GEOID) <> 5 "
        f"OR GEOID <> lpad(STATEFP, 2, '0') || lpad(COUNTYFP, 3, '0')")),
    "writtenCrs": q(f"SELECT ST_CRS(geom) FROM {C} LIMIT 1"),
    "bytes": os.path.getsize(counties),
    "file": os.path.basename(counties),
}

county_rows_in_source = int(q(f"SELECT count(*) FROM {S} WHERE GEO_ID LIKE '0500000US%'"))
published_us_total = q(f"SELECT B01003_E001 FROM {S} WHERE GEO_ID = '0100000US'")
# Puerto Rico's 78 municipios are in the county universe but outside the ACS national total, so the
# check has to exclude them. They are deliberately kept in the Parquet: NBI covers PR, and dropping
# them here would show up in W5 as a mysterious coverage gap rather than as a decision.
summed_states_only = int(q(
    f"SELECT coalesce(sum(POP_EST), 0) FROM {P} WHERE substr(GEOID, 1, 2) <> '72'"))

pop = {
    "vintage": int(acs_vintage),
    "period": f"{int(acs_vintage) - 4}-{acs_vintage}",
    "table": "B01003",
    "rowsInSource": int(q(f"SELECT count(*) FROM {S}")),
    "countyRowsInSource": county_rows_in_source,
    "rowsWritten": int(q(f"SELECT count(*) FROM {P}")),
    "distinctGeoids": int(q(f"SELECT count(DISTINCT GEOID) FROM {P}")),
    "geoidMalformed": int(q(f"SELECT count(*) FROM {P} WHERE length(GEOID) <> 5")),
    "estimateJamValues": int(q(f"SELECT count(*) FROM {P} WHERE POP_EST IS NULL")),
    "moeJamValues": int(q(f"SELECT count(*) FROM {P} WHERE POP_MOE IS NULL")),
    "publishedNationalTotal": int(published_us_total) if published_us_total else None,
    "summedCountiesExcludingPr": summed_states_only,
    "bytes": os.path.getsize(population),
    "file": os.path.basename(population),
}

# Key alignment between the two sources, which is the only thing that can quietly break here.
# Both vintages carry Connecticut as 9 planning regions and neither carries the 8 legacy counties;
# pairing a 2022+ ACS file with a pre-2023 TIGER vintage would leave every Connecticut estimate
# without a polygon. That is the failure this number exists to catch, so it is a hard stop.
join = {
    "populationWithoutBoundary": int(q(
        f"SELECT count(*) FROM {P} WHERE GEOID NOT IN (SELECT GEOID FROM {C})")),
    # The other direction is expected and benign: TIGER covers American Samoa, Guam, the Northern
    # Mariana Islands and the U.S. Virgin Islands; the ACS 5-year detailed tables do not.
    "boundaryWithoutPopulation": int(q(
        f"SELECT count(*) FROM {C} WHERE GEOID NOT IN (SELECT GEOID FROM {P})")),
    "boundaryWithoutPopulationStates": q(
        f"SELECT coalesce(string_agg(DISTINCT STATEFP, ',' ORDER BY STATEFP), '') FROM {C} "
        f"WHERE GEOID NOT IN (SELECT GEOID FROM {P})"),
}

problems = []
if boundaries["rowsWritten"] != boundaries["featuresInSource"]:
    problems.append(f"boundaries: {boundaries['featuresInSource']} features read but "
                    f"{boundaries['rowsWritten']} rows written")
if boundaries["distinctGeoids"] != boundaries["rowsWritten"]:
    problems.append("boundaries: GEOID is not unique")
if boundaries["nullGeometry"]:
    problems.append(f"boundaries: {boundaries['nullGeometry']} row(s) with NULL geometry")
if boundaries["geoidMalformed"]:
    problems.append(f"boundaries: {boundaries['geoidMalformed']} GEOID(s) are not "
                    "lpad(STATEFP,2) || lpad(COUNTYFP,3)")
if "4326" not in boundaries["writtenCrs"] and "CRS84" not in boundaries["writtenCrs"]:
    problems.append(f"boundaries: written CRS is {boundaries['writtenCrs']}, expected lon/lat WGS84")
if pop["rowsWritten"] != pop["countyRowsInSource"]:
    problems.append(f"population: {pop['countyRowsInSource']} county rows in source but "
                    f"{pop['rowsWritten']} written")
if pop["distinctGeoids"] != pop["rowsWritten"] or pop["geoidMalformed"]:
    problems.append("population: GEOID is not a unique 5-character key")
if join["populationWithoutBoundary"]:
    problems.append(f"join: {join['populationWithoutBoundary']} ACS county/counties have no TIGER "
                    f"polygon — TIGER {tiger_vintage} and ACS {acs_vintage} disagree about the "
                    "county universe (this is what a Connecticut-era mismatch looks like)")

# The national total is a free integrity check the publisher hands you: the 50 states + DC counties
# must sum to the published nation row. It cannot hold for a fixture, which is a sample of counties
# by construction, so there it is recorded and not enforced.
totals_agree = pop["publishedNationalTotal"] == pop["summedCountiesExcludingPr"]
pop["nationalTotalAgrees"] = totals_agree
if not fixtures and not totals_agree:
    problems.append(f"population: counties sum to {pop['summedCountiesExcludingPr']} but the "
                    f"published national row is {pop['publishedNationalTotal']}")

print(f"    boundaries: {boundaries['featuresInSource']} features = {boundaries['rowsWritten']} rows"
      f"  |  {boundaries['distinctStateFips']} state FIPS  |  CRS {boundaries['writtenCrs']}"
      f"  ({boundaries['bytes'] / 1e6:.1f} MB)")
print(f"    population: {pop['countyRowsInSource']} county rows of {pop['rowsInSource']} = "
      f"{pop['rowsWritten']} written  |  {pop['moeJamValues']} MoE jam value(s)"
      f"  ({pop['bytes'] / 1e6:.1f} MB)")
print(f"    national total: published {pop['publishedNationalTotal']} vs summed "
      f"{pop['summedCountiesExcludingPr']}  {'OK' if totals_agree else 'MISMATCH'}"
      f"{' (not enforced for fixtures)' if fixtures else ''}")
print(f"    join keys: {join['populationWithoutBoundary']} ACS row(s) without a polygon, "
      f"{join['boundaryWithoutPopulation']} polygon(s) without an ACS row"
      + (f" (state FIPS {join['boundaryWithoutPopulationStates']})"
         if join["boundaryWithoutPopulation"] else ""))

if problems:
    sys.exit("error: reconciliation failed\n  - " + "\n  - ".join(problems))

if not catalog_path:
    print("    --fixtures: catalog.json untouched")
    sys.exit(0)

catalog = {"schemaVersion": 1, "sources": {}}
if os.path.exists(catalog_path):
    catalog = json.load(open(catalog_path))

now = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
outputs = {
    "boundaries": boundaries,
    "population": pop,
    "join": join,
    "reconciles": True,
    "convertedUtc": now,
    "toolVersion": tool_version,
}
changed = catalog.get("outputs") != outputs
catalog["outputs"] = outputs
if changed:
    catalog["updatedUtc"] = now
with open(catalog_path, "w") as fh:
    json.dump(catalog, fh, indent=2)
    fh.write("\n")
print(f"    {catalog_path} updated")
PY

echo "==> Done. Query it with DuckDB, from the repository root:"
echo "    duckdb -c \"LOAD spatial; SELECT c.GEOID, c.NAMELSAD, p.POP_EST, p.POP_MOE"
echo "                 FROM read_parquet('$COUNTIES_PARQUET') c"
echo "                 JOIN read_parquet('$POPULATION_PARQUET') p USING (GEOID)"
echo "                 ORDER BY p.POP_EST DESC LIMIT 5;\""
