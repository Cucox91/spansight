#!/usr/bin/env bash
# tools/vintages/download.sh — FR-1.1 AC-1: fetch FHWA annual NBI vintages (1992-2025).
#
# Usage:
#   tools/vintages/download.sh                 # every vintage 1992-2025 (bulk; ~1.6 GB zipped)
#   tools/vintages/download.sh 1992 2010 2025  # only these years
#   tools/vintages/download.sh --list          # print the resolved URLs, download nothing
#
# Downloads into data/vintages/raw/<year>/ (gitignored — no bulk NBI data in git, CLAUDE.md rule 4),
# extracts the single .txt each zip contains, and appends a provenance line (URL + SHA-256 + date)
# to data/vintages/raw/<year>/source.json. Files already present are skipped, so a re-run is a
# cheap no-op and an interrupted bulk download resumes by just running it again.
#
# See tools/vintages/README.md for what the per-year quirks actually are.

set -euo pipefail

cd "$(dirname "$0")/../.."

BASE="https://www.fhwa.dot.gov/bridge/nbi"
FIRST_YEAR=1992
LAST_YEAR=2025
RAW_DIR="data/vintages/raw"

# The national single-file *delimited* export is the only packaging that exists for every year, and
# its URL slug changes at 2018. The "all records" variant (2013+) is deliberately NOT used: it
# includes non-highway records, so mixing it in would move the population mid-series and corrupt
# every trend. Verified against the live site 2026-07-25 by probing all 34 years.
slug_for() {
  local year="$1"
  if (( year >= 2018 )); then
    echo "${year}hwybronefiledel"
  else
    echo "${year}hwybronlyonefile"
  fi
}

usage() { sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'; }

LIST_ONLY=0
YEARS=()
for arg in "$@"; do
  case "$arg" in
    --list) LIST_ONLY=1 ;;
    --help|-h) usage; exit 0 ;;
    [0-9][0-9][0-9][0-9])
      if (( arg < FIRST_YEAR || arg > LAST_YEAR )); then
        echo "error: $arg is outside the published range ${FIRST_YEAR}-${LAST_YEAR}." >&2
        exit 2
      fi
      YEARS+=("$arg") ;;
    *) echo "error: unrecognised argument '$arg'." >&2; usage >&2; exit 2 ;;
  esac
done

if (( ${#YEARS[@]} == 0 )); then
  for (( y = FIRST_YEAR; y <= LAST_YEAR; y++ )); do YEARS+=("$y"); done
fi

for tool in curl unzip shasum python3; do
  command -v "$tool" >/dev/null 2>&1 || { echo "error: $tool not found on PATH." >&2; exit 1; }
done

if (( LIST_ONLY )); then
  for year in "${YEARS[@]}"; do printf '%s\t%s/%s.zip\n' "$year" "$BASE" "$(slug_for "$year")"; done
  exit 0
fi

for year in "${YEARS[@]}"; do
  slug="$(slug_for "$year")"
  url="$BASE/$slug.zip"
  dir="$RAW_DIR/$year"
  zip="$dir/$slug.zip"
  extracted="$dir/extracted"
  mkdir -p "$extracted"

  if [[ -f "$zip" ]]; then
    echo "==> $year  zip already present, skipping download"
  else
    echo "==> $year  $url"
    # Download to a temp name first so an interrupted transfer never looks like a complete file.
    if ! curl -sS --fail --location --max-time 1800 -o "$zip.partial" "$url"; then
      echo "error: $year download failed from $url" >&2
      rm -f "$zip.partial"
      exit 1
    fi
    mv "$zip.partial" "$zip"
  fi

  sha="$(shasum -a 256 "$zip" | cut -d' ' -f1)"

  # Inner filenames are not predictable: 1992 ships 'fluna_991992-20160919110712.txt', 2010 ships
  # '2010_highwaybridgesonly_onefile.txt'. Extract whatever single .txt is inside.
  if ! compgen -G "$extracted/*.txt" >/dev/null; then
    echo "    extracting $(basename "$zip")"
    unzip -q -o "$zip" -d "$extracted"
  fi

  txt="$(find "$extracted" -maxdepth 1 -name '*.txt' | head -1)"
  if [[ -z "$txt" ]]; then
    echo "error: $year zip contained no .txt file (got: $(ls "$extracted"))" >&2
    exit 1
  fi

  python3 - "$year" "$url" "$zip" "$sha" "$txt" "$dir/source.json" <<'PY'
import json, os, sys
year, url, zip_path, sha, txt, out = sys.argv[1:7]
doc = {
    "year": int(year),
    "url": url,
    "zip": os.path.basename(zip_path),
    "zipSha256": sha,
    "zipBytes": os.path.getsize(zip_path),
    "extracted": os.path.basename(txt),
    "extractedBytes": os.path.getsize(txt),
    "downloadedUtc": __import__("datetime").datetime.now(__import__("datetime").timezone.utc)
        .replace(microsecond=0).isoformat().replace("+00:00", "Z"),
}
with open(out, "w") as fh:
    json.dump(doc, fh, indent=2)
    fh.write("\n")
print(f"    {doc['extracted']}  ({doc['extractedBytes'] / 1e6:.0f} MB)  sha256={sha[:16]}…")
PY
done

echo "==> Done. Convert with: tools/vintages/convert.sh ${YEARS[*]}"
