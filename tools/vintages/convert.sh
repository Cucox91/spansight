#!/usr/bin/env bash
# tools/vintages/convert.sh — FR-1.1 AC-1/AC-2/AC-4: normalize NBI vintages to Parquet.
#
# Usage:
#   tools/vintages/convert.sh                  # every vintage downloaded under data/vintages/raw/
#   tools/vintages/convert.sh 1992 2010 2025   # only these years
#   tools/vintages/convert.sh --fixtures       # the committed era fixtures (fast; used by CI)
#
# Shape (mirrors tools/build-tiles.sh): the .NET CLI does the parse and normalization, because
# every era quirk then lives in tested C#; DuckDB does the columnar step, because that is what it
# is for (ADR-005). Nothing here touches Postgres — the historical set never enters the serving
# database.
#
# Per vintage this produces:
#   data/vintages/parquet/nbi_<year>.parquet   normalized superset, one row per structure record
#   data/vintages/rejects/<year>.csv           itemized rejects with reason codes (AC-2)
# and updates tools/vintages/catalog.json, which is committed and is the reconciliation record.
#
# Requires: dotnet, duckdb (brew install duckdb), python3.

set -euo pipefail

cd "$(dirname "$0")/../.."

RAW_DIR="data/vintages/raw"
OUT_DIR="data/vintages"
FIXTURE_DIR="src/tests/fixtures/vintages"
CATALOG="tools/vintages/catalog.json"
TOOL_VERSION="1.0.0"

usage() { sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'; }

FIXTURES=0
YEARS=()
for arg in "$@"; do
  case "$arg" in
    --fixtures) FIXTURES=1 ;;
    --help|-h) usage; exit 0 ;;
    [0-9][0-9][0-9][0-9]) YEARS+=("$arg") ;;
    *) echo "error: unrecognised argument '$arg'." >&2; usage >&2; exit 2 ;;
  esac
done

for tool in dotnet duckdb python3; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "error: $tool not found on PATH." >&2
    [[ "$tool" == "duckdb" ]] && echo "  install: brew install duckdb" >&2
    exit 1
  fi
done

if (( FIXTURES )); then
  OUT_DIR="data/vintages/fixtures-out"
  CATALOG=""   # fixtures never touch the real catalog
  # macOS ships bash 3.2, which has no mapfile — read into the array portably.
  SOURCES=()
  while IFS= read -r line; do SOURCES+=("$line"); done < <(find "$FIXTURE_DIR" -name 'nbi_*.txt' | sort)
  if (( ${#SOURCES[@]} == 0 )); then
    echo "error: no fixtures under $FIXTURE_DIR" >&2; exit 1
  fi
else
  if (( ${#YEARS[@]} == 0 )); then
    while IFS= read -r line; do YEARS+=("$line"); done < <(find "$RAW_DIR" -maxdepth 1 -mindepth 1 -type d -exec basename {} \; 2>/dev/null | sort)
  fi
  if (( ${#YEARS[@]} == 0 )); then
    echo "error: nothing downloaded under $RAW_DIR — run tools/vintages/download.sh first." >&2
    exit 1
  fi
  SOURCES=()
  for year in "${YEARS[@]}"; do
    txt="$(find "$RAW_DIR/$year/extracted" -maxdepth 1 -name '*.txt' 2>/dev/null | head -1)"
    [[ -z "$txt" ]] && { echo "error: no extracted .txt for $year — re-run download.sh $year" >&2; exit 1; }
    SOURCES+=("$txt")
  done
fi

mkdir -p "$OUT_DIR/parquet" "$OUT_DIR/normalized" "$OUT_DIR/rejects" "$OUT_DIR/summaries"

echo "==> Building the ingestion CLI"
dotnet build src/SpanSight.Ingestion --configuration Release --nologo --verbosity quiet

for src in "${SOURCES[@]}"; do
  # Fixture files are named nbi_<year>.txt; real vintages live under raw/<year>/extracted/.
  if (( FIXTURES )); then
    year="$(basename "$src" .txt)"; year="${year#nbi_}"
  else
    year="$(basename "$(dirname "$(dirname "$src")")")"
  fi

  echo "==> $year  normalizing $(basename "$src")"
  summary="$OUT_DIR/summaries/$year.json"
  dotnet run --project src/SpanSight.Ingestion --configuration Release --no-build -- \
    convert-vintage --file "$src" --snapshot-year "$year" --out "$OUT_DIR" > "$summary"

  normalized="$OUT_DIR/normalized/nbi_$year.csv"
  parquet="$OUT_DIR/parquet/nbi_$year.parquet"

  echo "    writing $(basename "$parquet")"
  # Every superset column stays VARCHAR so the Parquet is a faithful copy of published text (an
  # NBI code like '01' is not the number 1, and 'N' is not a missing value). The four condition
  # items additionally get typed *_NUM columns: TRY_CAST turns the single digits into TINYINT and
  # leaves 'N' / blank as NULL, which is exactly what the Phase 0 classifier ignores. FR-1.2
  # replays that classifier over these rather than re-deriving it here.
  duckdb -c "
    COPY (
      SELECT
        * REPLACE (CAST(VINTAGE_YEAR AS SMALLINT) AS VINTAGE_YEAR, CAST(SOURCE_ROW AS INTEGER) AS SOURCE_ROW),
        TRY_CAST(DECK_COND_058           AS TINYINT) AS DECK_COND_058_NUM,
        TRY_CAST(SUPERSTRUCTURE_COND_059 AS TINYINT) AS SUPERSTRUCTURE_COND_059_NUM,
        TRY_CAST(SUBSTRUCTURE_COND_060   AS TINYINT) AS SUBSTRUCTURE_COND_060_NUM,
        TRY_CAST(CULVERT_COND_062        AS TINYINT) AS CULVERT_COND_062_NUM
      FROM read_csv('$normalized', delim=',', header=true, quote='\"', escape='\"', all_varchar=true)
    ) TO '$parquet' (FORMAT PARQUET, COMPRESSION ZSTD);
  "

  # The intermediate is large and fully reproducible; keep only the Parquet.
  rm -f "$normalized"

  python3 - "$summary" "$parquet" "$OUT_DIR/rejects/$year.csv" <<'PY'
import json, os, subprocess, sys
summary_path, parquet, rejects = sys.argv[1:4]
s = json.load(open(summary_path))
rows = int(subprocess.run(["duckdb", "-noheader", "-list", "-c",
    f"SELECT count(*) FROM read_parquet('{parquet}')"], capture_output=True, text=True).stdout.strip())
s["parquetRows"] = rows
s["parquetBytes"] = os.path.getsize(parquet)
s["parquetFile"] = os.path.basename(parquet)
json.dump(s, open(summary_path, "w"), indent=2)
ok = rows == s["RowsConverted"]
print(f"    rows: source {s['RowsRead']} = converted {s['RowsConverted']} + rejected {s['RowsRejected']}"
      f"  |  parquet {rows} {'OK' if ok else 'MISMATCH'}  ({s['parquetBytes']/1e6:.1f} MB)")
if not ok:
    sys.exit(f"error: parquet row count {rows} != converted {s['RowsConverted']}")
PY
done

if [[ -n "$CATALOG" ]]; then
  echo "==> Updating $CATALOG"
  python3 - "$CATALOG" "$OUT_DIR/summaries" "$RAW_DIR" "$TOOL_VERSION" <<'PY'
import datetime, glob, json, os, sys
catalog_path, summaries_dir, raw_dir, tool_version = sys.argv[1:5]

catalog = {"schemaVersion": 1, "vintages": {}}
if os.path.exists(catalog_path):
    catalog = json.load(open(catalog_path))
catalog.setdefault("vintages", {})

now = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")

for path in sorted(glob.glob(os.path.join(summaries_dir, "*.json"))):
    s = json.load(open(path))
    year = str(s["Year"])
    source_meta = {}
    source_json = os.path.join(raw_dir, year, "source.json")
    if os.path.exists(source_json):
        source_meta = json.load(open(source_json))
    catalog["vintages"][year] = {
        "year": s["Year"],
        "era": s["Era"],
        "source": {
            "url": source_meta.get("url"),
            "zip": source_meta.get("zip"),
            "zipSha256": source_meta.get("zipSha256"),
            "file": s["SourceFile"],
            "fileSha256": s["SourceSha256"],
            "downloadedUtc": source_meta.get("downloadedUtc"),
        },
        "rowsInSource": s["RowsRead"],
        "rowsConverted": s["RowsConverted"],
        "rowsRejected": s["RowsRejected"],
        "rejectsByReason": s["RejectsByReason"],
        "reconciles": s["Reconciles"],
        "sourceColumns": s["SourceColumns"],
        "absentColumns": s["AbsentColumns"],
        "parquet": {"file": s.get("parquetFile"), "rows": s.get("parquetRows"), "bytes": s.get("parquetBytes")},
        "convertedUtc": now,
        "toolVersion": tool_version,
    }

catalog["outputColumns"] = json.load(open(sorted(glob.glob(os.path.join(summaries_dir, "*.json")))[0]))["OutputColumns"]
catalog["updatedUtc"] = now
catalog["vintages"] = dict(sorted(catalog["vintages"].items()))
with open(catalog_path, "w") as fh:
    json.dump(catalog, fh, indent=2)
    fh.write("\n")

total_src = sum(v["rowsInSource"] for v in catalog["vintages"].values())
total_out = sum(v["rowsConverted"] for v in catalog["vintages"].values())
total_rej = sum(v["rowsRejected"] for v in catalog["vintages"].values())
print(f"    {len(catalog['vintages'])} vintage(s): {total_src} source rows = {total_out} converted + {total_rej} rejected")
PY
fi

echo "==> Done. Query the catalog with DuckDB:"
echo "    duckdb -c \"SELECT VINTAGE_YEAR, count(*) FROM read_parquet('$OUT_DIR/parquet/nbi_*.parquet') GROUP BY 1 ORDER BY 1;\""
