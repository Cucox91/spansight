#!/usr/bin/env bash
# tools/census/download.sh — FR-1.5 AC-1: stage the Census reference data (county boundaries and
# county population) with vintage and licence provenance.
#
# Usage:
#   tools/census/download.sh                # both sources into data/census/raw/ (~102 MB)
#   tools/census/download.sh boundaries     # only one — 'boundaries' or 'population'
#   tools/census/download.sh --list         # print the resolved URLs, download nothing
#
# Downloads into data/census/raw/ (gitignored — CLAUDE.md rule 4) and records, per source, the
# resolved URL, SHA-256, byte size, the server's Last-Modified, the download timestamp, the exact
# vintage used and the licence/citation text into tools/census/catalog.json, which IS committed.
# That file is the provenance record NFR-8 asks for: it is what lets a population figure shown in
# the UI be traced back to the exact bytes it came from.
#
# Re-runnable: a file already on disk is skipped, so an interrupted transfer resumes by running the
# script again. A skipped file whose SHA-256 still matches the catalog keeps its recorded
# timestamps untouched, so a re-run does not churn the committed catalog.
#
# No API key, deliberately. api.census.gov now requires a key for *every* data query (Census Data
# API User Guide, 2026-05-21: "An API key must be used with all data queries made to the Census
# Data API") — verified 2026-07-26, a keyless county query 302s to missing_key.html. The bulk
# files on www2.census.gov used here need no key at all, so SpanSight holds no Census secret and
# CLAUDE.md rule 2 never comes into it.
#
# Bridges gain nothing here. This is staging only — the point-in-polygon join and the
# join-coverage metric are W5 (FR-1.5 AC-2). Nothing in this script touches bridge data.
#
# See tools/census/README.md for the vintage quirks that actually matter.

set -euo pipefail

cd "$(dirname "$0")/../.."

RAW_DIR="data/census/raw"
CATALOG="tools/census/catalog.json"

# Pinned to verified facts, not guessed from the calendar. Probed against the live Census servers
# 2026-07-26: TIGER2026/ does not exist (the series releases each September — 2025 shipped
# 2025-09-23), and ACS 5-year 2025 is 404 on both api.census.gov and www2 (the 2020-2024 file
# released 2026-01-29, which broke the historical December cadence). A script that assumes
# "latest = last year, out in December" is wrong for months at a time on this data, and wrong
# silently, because the 404 only shows up when someone runs it.
#
# Overridable by environment so a future vintage is one variable and not an edit — but the default
# must stay something that was actually observed to exist.
TIGER_VINTAGE="${SPANSIGHT_TIGER_VINTAGE:-2025}"
ACS_VINTAGE="${SPANSIGHT_ACS_VINTAGE:-2024}"

# Full-resolution TIGER/Line, NOT the cartographic-boundary (cb_) generalization. Census documents
# that the cb_ files are "clipped to a simplified version of the U.S. outline. As a result, some
# offshore areas may be excluded". The population being joined in W5 is *bridges*, which sit over
# water by definition — every causeway and bay crossing in the NBI is exactly the geometry a
# shoreline clip removes, so those structures would silently return no county. The cb_ files are
# the right source for a rendered county overlay and the wrong source for a containment test.
# There is no per-state TIGER county file (tl_2025_12_county.zip → 404); the national file is it.
TIGER_URL="https://www2.census.gov/geo/tiger/TIGER${TIGER_VINTAGE}/COUNTY/tl_${TIGER_VINTAGE}_us_county.zip"

# ACS 5-year, table B01003 (Total Population), from the bulk table-based Summary File.
# 5-year and not 1-year: the 1-year file publishes only 861 counties against the 5-year's 3,222,
# because it has a 65,000-population threshold. The 2,361 counties it drops are the rural ones —
# precisely where bridge counts per capita are highest, so 1-year would bias the W5 metric in the
# direction the metric is about.
ACS_URL="https://www2.census.gov/programs-surveys/acs/summary_file/${ACS_VINTAGE}/table-based-SF/data/5YRData/acsdt5y${ACS_VINTAGE}-b01003.dat"

TIGER_FILE="$RAW_DIR/tl_${TIGER_VINTAGE}_us_county.zip"
ACS_FILE="$RAW_DIR/acsdt5y${ACS_VINTAGE}-b01003.dat"

usage() { sed -n '2,29p' "$0" | sed 's/^# \{0,1\}//'; }

LIST_ONLY=0
SOURCES=()
for arg in "$@"; do
  case "$arg" in
    --list) LIST_ONLY=1 ;;
    --help|-h) usage; exit 0 ;;
    boundaries|population) SOURCES+=("$arg") ;;
    *) echo "error: unrecognised argument '$arg'." >&2; usage >&2; exit 2 ;;
  esac
done

if (( ${#SOURCES[@]} == 0 )); then
  SOURCES=(boundaries population)
fi

for tool in curl shasum unzip python3; do
  command -v "$tool" >/dev/null 2>&1 || { echo "error: $tool not found on PATH." >&2; exit 1; }
done

url_for()  { case "$1" in boundaries) echo "$TIGER_URL" ;; population) echo "$ACS_URL" ;; esac; }
file_for() { case "$1" in boundaries) echo "$TIGER_FILE" ;; population) echo "$ACS_FILE" ;; esac; }
vint_for() { case "$1" in boundaries) echo "$TIGER_VINTAGE" ;; population) echo "$ACS_VINTAGE" ;; esac; }

if (( LIST_ONLY )); then
  for src in "${SOURCES[@]}"; do printf '%s\t%s\t%s\n' "$src" "$(vint_for "$src")" "$(url_for "$src")"; done
  exit 0
fi

mkdir -p "$RAW_DIR"

for src in "${SOURCES[@]}"; do
  url="$(url_for "$src")"
  path="$(file_for "$src")"
  vintage="$(vint_for "$src")"
  last_modified=""
  downloaded=0

  if [[ -f "$path" ]]; then
    echo "==> $src  $(basename "$path") already present, skipping download"
  else
    echo "==> $src  $url"
    # Download to a temp name first so an interrupted transfer never looks like a complete file.
    # Headers are dumped alongside because Last-Modified is the only freshness evidence this
    # server offers — it sends no ETag and no gzip, so there is no conditional-GET shortcut and
    # idempotency has to be file-presence plus SHA-256 (same as tools/vintages/download.sh).
    if ! curl -sS --fail --location --max-time 1800 \
        --dump-header "$path.headers" -o "$path.partial" "$url"; then
      echo "error: $src download failed from $url" >&2
      echo "  if this is a 404, the vintage may have moved — see the pinned vintages at the top" >&2
      rm -f "$path.partial" "$path.headers"
      exit 1
    fi
    mv "$path.partial" "$path"
    last_modified="$(tr -d '\r' < "$path.headers" | awk 'tolower($1) == "last-modified:" { sub(/^[^ ]+ /, ""); print }' | tail -1)"
    rm -f "$path.headers"
    downloaded=1
  fi

  # Fail here rather than three steps into a conversion. A truncated transfer or an HTML error
  # page saved under a .zip name both satisfy curl --fail; neither survives this.
  case "$src" in
    boundaries)
      # Listed into a variable rather than piped into `grep -q`: under `set -o pipefail`, grep
      # exiting on its first match SIGPIPEs unzip and the whole pipeline reports failure
      # intermittently. Anchored on '.shp' at end of line so the two .shp.*.iso.xml metadata
      # members do not count as the geometry.
      members="$(unzip -Z1 "$path" 2>/dev/null || true)"
      if ! printf '%s\n' "$members" | grep -q '\.shp$'; then
        echo "error: $path is not a shapefile archive (no .shp member). Delete it and re-run." >&2
        exit 1
      fi ;;
    population)
      # The bulk summary file is pipe-delimited with a GEO_ID header. Note the column spelling:
      # the bulk file uses B01003_E001 and the API uses B01003_001E — copying a variable name
      # between the two paths fails silently, so the header is asserted, not assumed.
      if ! head -1 "$path" | grep -q '^GEO_ID|B01003_E001|B01003_M001$'; then
        echo "error: $path has an unexpected header: $(head -1 "$path")" >&2
        echo "  expected GEO_ID|B01003_E001|B01003_M001 — the published layout may have changed." >&2
        exit 1
      fi ;;
  esac

  sha="$(shasum -a 256 "$path" | cut -d' ' -f1)"

  python3 - "$CATALOG" "$src" "$vintage" "$url" "$path" "$sha" "$downloaded" "$last_modified" <<'PY'
import datetime, json, os, sys

catalog_path, key, vintage, url, path, sha, downloaded, last_modified = sys.argv[1:9]
vintage = int(vintage)
downloaded = downloaded == "1"

# The licence and disclaimer prose lives here rather than in the shell so it stays quotable
# verbatim. Both sources are U.S. Government works: public domain under 17 U.S.C. Sect 105, with
# the Census Bureau asking only to be cited. The disclaimers are not optional decoration — the
# TIGER "not legal land descriptions" clause is the geography equivalent of the not-engineering-
# advice footer (CLAUDE.md rule 6), and the ACS margin-of-error clause is what keeps a
# bridges-per-capita figure inside the GR-6 descriptive framing.
META = {
    "boundaries": {
        "product": "TIGER/Line Shapefiles - County and Equivalent Entity (National)",
        "publisher": "U.S. Census Bureau, Geography Division",
        "crs": "EPSG:4269 (NAD83 geographic, read from the shipped .prj)",
        # Census: "All legal boundaries and names are as of January 1" of the vintage year.
        "boundariesAsOf": f"{vintage}-01-01",
        "citation": (
            f"U.S. Census Bureau, {vintage} TIGER/Line(R) Shapefiles, County and Equivalent Entity "
            f"National Shapefile. Boundaries and names as of {vintage}-01-01."
        ),
        "license": (
            "Public domain - a work of the United States Government is not eligible for copyright "
            "protection (17 U.S.C. Sect 105). The Census Bureau asks only that it be cited as the "
            "source."
        ),
        "disclaimer": (
            "No warranty is made as to positional or attribute accuracy. The boundary information "
            "is for statistical data collection and tabulation purposes only; its depiction does "
            "not constitute a determination of jurisdictional authority or rights of ownership or "
            "entitlement, and it is not a legal land description. TIGER/Line(R) is a registered "
            "trademark of the U.S. Census Bureau."
        ),
        "redistributionNote": (
            "The Census Bureau requests that any repackaging of TIGER/Line data for distribution "
            "carry a conspicuously placed statement to that effect. Anything derived from this "
            "geometry and served publicly (tiles, a county overlay) must show the citation and "
            "disclaimer above."
        ),
    },
    "population": {
        "product": "American Community Survey 5-Year Estimates, Table B01003 (Total Population)",
        "publisher": "U.S. Census Bureau",
        "period": f"{vintage - 4}-{vintage}",
        "accessPath": (
            "Bulk table-based Summary File on www2.census.gov. No API key: api.census.gov requires "
            "a key for every data query (API User Guide, 2026-05-21), the bulk path requires none."
        ),
        "citation": (
            f"U.S. Census Bureau, {vintage - 4}-{vintage} American Community Survey 5-Year "
            f"Estimates, Table B01003 (Total Population)."
        ),
        "license": (
            "Public domain - a work of the United States Government is not eligible for copyright "
            "protection (17 U.S.C. Sect 105)."
        ),
        "disclaimer": (
            "ACS values are survey estimates with margins of error, not counts. The published "
            "margin of error travels with the estimate (POP_MOE) and must be shown alongside any "
            "derived figure (GR-6)."
        ),
        "redistributionNote": (
            "The API terms of service are not triggered by the bulk path, so no endorsement "
            "disclaimer is required. Do not present a modified value as a Census figure."
        ),
    },
}

catalog = {"schemaVersion": 1, "sources": {}}
if os.path.exists(catalog_path):
    catalog = json.load(open(catalog_path))
catalog.setdefault("sources", {})

previous = catalog["sources"].get(key, {})
now = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")

# A skipped file whose bytes still hash the same is the same download it always was. Carrying the
# original timestamps forward keeps a re-run from producing a diff on a committed file for no
# reason, which is what makes "just run it again" safe.
unchanged = not downloaded and previous.get("sha256") == sha
record = {
    "vintage": vintage,
    **META[key],
    "url": url,
    "file": os.path.basename(path),
    "sha256": sha,
    "bytes": os.path.getsize(path),
    # The server's own Last-Modified, kept because it is independent evidence of which release
    # these bytes are: TIGER 2025 reads 2025-09-23 and ACS 2024 reads 2026-01-29, both matching
    # the announced release dates to the day.
    "lastModified": previous.get("lastModified") if unchanged else (last_modified or previous.get("lastModified")),
    "downloadedUtc": previous.get("downloadedUtc") if unchanged else now,
}
changed = catalog["sources"].get(key) != record
catalog["sources"][key] = record
catalog["sources"] = dict(sorted(catalog["sources"].items()))
# Only stamp updatedUtc when something actually moved, for the same reason: a no-op run must
# leave a committed file byte-identical or "re-run it" stops being free advice.
if changed or not catalog.get("updatedUtc"):
    catalog["updatedUtc"] = now
with open(catalog_path, "w") as fh:
    json.dump(catalog, fh, indent=2)
    fh.write("\n")

print(f"    {record['file']}  ({record['bytes'] / 1e6:.1f} MB)  vintage={vintage}  sha256={sha[:16]}...")
if record["lastModified"]:
    print(f"    published: {record['lastModified']}")
PY
done

echo "==> Done. Convert with: tools/census/convert.sh"
