#!/usr/bin/env bash
# tools/build-tiles.sh — FR-0.5 AC-1 (ADR-002): export core.bridge to GeoJSONSeq, build a single
# PMTiles artifact with tippecanoe, and write a manifest tied to the ingestion run that produced
# the data (via the exporter's .meta.json sidecar).
#
# Usage:
#   tools/build-tiles.sh [--out-dir <dir>] [--connection "<cs>"]
#
# Defaults: --out-dir data/tiles (gitignored — tile artifacts never enter git, CLAUDE.md rule 4).
# Requires: dotnet, tippecanoe (brew install tippecanoe), python3 (manifest assembly).
# Publish the resulting .pmtiles to Blob/CDN and point VITE_TILES_URL at it (web/ reads it via
# the pmtiles protocol; source-layer name is "bridges").

set -euo pipefail

cd "$(dirname "$0")/.."

OUT_DIR="data/tiles"
CONNECTION=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out-dir) OUT_DIR="$2"; shift 2 ;;
    --connection) CONNECTION="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

for tool in dotnet tippecanoe python3; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "error: $tool not found on PATH." >&2
    [[ "$tool" == "tippecanoe" ]] && echo "  install: brew install tippecanoe" >&2
    exit 1
  fi
done

mkdir -p "$OUT_DIR"
GEOJSONL="$OUT_DIR/bridges.geojsonl"
PMTILES="$OUT_DIR/bridges.pmtiles"
MANIFEST="$OUT_DIR/manifest.json"

echo "==> Exporting core.bridge to $GEOJSONL"
EXPORT_ARGS=(export-geojson --out "$GEOJSONL")
[[ -n "$CONNECTION" ]] && EXPORT_ARGS+=(--connection "$CONNECTION")
dotnet run --project src/SpanSight.Ingestion -- "${EXPORT_ARGS[@]}"

echo "==> Building $PMTILES (tippecanoe)"
# Single point layer named "bridges" (must match the web map's source-layer). -zg guesses a
# sensible maxzoom; densest-first dropping keeps low zooms readable at national scale (ADR-002).
tippecanoe -o "$PMTILES" --force \
  --layer=bridges \
  -zg \
  --drop-densest-as-needed \
  --read-parallel \
  "$GEOJSONL"

echo "==> Writing $MANIFEST"
python3 - "$GEOJSONL" "$PMTILES" "$MANIFEST" <<'PY'
import hashlib, json, os, subprocess, sys
geojsonl, pmtiles, manifest = sys.argv[1:4]

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

with open(geojsonl + ".meta.json") as f:
    export_meta = json.load(f)

tippecanoe_version = subprocess.run(
    ["tippecanoe", "--version"], capture_output=True, text=True
).stderr.strip() or None

doc = {
    "artifact": os.path.basename(pmtiles),
    "layer": "bridges",
    "generatedUtc": export_meta.get("generatedUtc"),
    "featureCount": export_meta.get("featureCount"),
    "ingestionRun": export_meta.get("ingestionRun"),
    "source": {"file": os.path.basename(geojsonl), "sha256": sha256(geojsonl)},
    "tiles": {
        "sha256": sha256(pmtiles),
        "bytes": os.path.getsize(pmtiles),
        "tippecanoe": tippecanoe_version,
    },
}
with open(manifest, "w") as f:
    json.dump(doc, f, indent=2)
print(json.dumps(doc, indent=2))
PY

echo "==> Done: $PMTILES + $MANIFEST"
echo "    Publish both, then set VITE_TILES_URL to the published .pmtiles URL."
