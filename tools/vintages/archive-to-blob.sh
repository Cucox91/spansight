#!/usr/bin/env bash
# tools/vintages/archive-to-blob.sh — FR-1.1 AC-3: archive the vintage Parquet set to Blob storage.
#
# Usage:
#   tools/vintages/archive-to-blob.sh --dry-run   # print what would be uploaded, touch nothing
#   tools/vintages/archive-to-blob.sh             # upload data/vintages/parquet/ + catalog.json
#
# Uploads the converted vintages (~1.4 GB) and the reconciliation manifest to the cool-tier
# 'parquet-archive' container that infra/modules/storage.bicep already declares. The operator
# procedure — and the fact that this is a [RAZIEL] step, because the az login is his — is
# docs/RUNBOOK.md §10.
#
# This is an archive, not a serving path. Nothing reads these blobs at run time: the historical set
# never enters the serving Postgres (ADR-005), and the whole set is reproducible from FHWA with
# download.sh + convert.sh. It exists so the exact bytes behind a published aggregate survive FHWA
# reorganising their site, at roughly a dollar a month.
#
# Requires: az (already logged in — this script never logs you in).

set -euo pipefail

cd "$(dirname "$0")/../.."

# Matches infra/main.bicep: the account is 'st${baseName}${env}' and the container is declared in
# infra/modules/storage.bicep. Overridable for a non-demo environment, but the defaults are the
# only environment that exists today.
ACCOUNT="${SPANSIGHT_STORAGE_ACCOUNT:-stspansightdemo}"
CONTAINER="${SPANSIGHT_ARCHIVE_CONTAINER:-parquet-archive}"
PARQUET_DIR="data/vintages/parquet"
CATALOG="tools/vintages/catalog.json"

usage() { sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; }

DRY_RUN=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    --help|-h) usage; exit 0 ;;
    *) echo "error: unrecognised argument '$arg'." >&2; usage >&2; exit 2 ;;
  esac
done

if [[ ! -d "$PARQUET_DIR" ]]; then
  echo "error: $PARQUET_DIR does not exist — run tools/vintages/convert.sh first." >&2
  exit 1
fi

# macOS ships bash 3.2, which has no mapfile — read into the array portably.
FILES=()
while IFS= read -r line; do FILES+=("$line"); done < <(find "$PARQUET_DIR" -maxdepth 1 -name 'nbi_*.parquet' | sort)

if (( ${#FILES[@]} == 0 )); then
  echo "error: no nbi_*.parquet under $PARQUET_DIR — run tools/vintages/convert.sh first." >&2
  exit 1
fi

if [[ ! -f "$CATALOG" ]]; then
  echo "error: $CATALOG is missing — it is the reconciliation record and ships with the data." >&2
  exit 1
fi

TOTAL=0
for f in "${FILES[@]}"; do
  size=$(wc -c < "$f")
  TOTAL=$(( TOTAL + size ))
done
TOTAL=$(( TOTAL + $(wc -c < "$CATALOG") ))

mb() { awk -v b="$1" 'BEGIN { printf "%.1f", b / 1000000 }'; }

echo "==> ${#FILES[@]} Parquet file(s) + $(basename "$CATALOG")"
echo "    $(mb "$TOTAL") MB → ${ACCOUNT}/${CONTAINER} (Cool)"

if (( DRY_RUN )); then
  for f in "${FILES[@]}"; do
    printf '    %-44s %8s MB\n' "$(basename "$f")" "$(mb "$(wc -c < "$f")")"
  done
  echo "==> --dry-run: nothing uploaded, Azure not contacted."
  exit 0
fi

command -v az >/dev/null 2>&1 || { echo "error: az not found on PATH." >&2; exit 1; }

# Deliberately does NOT run 'az login' — credentials and account actions stay with the operator
# (CLAUDE.md rule 2). Fail with the fix rather than silently opening a browser.
if ! az account show >/dev/null 2>&1; then
  echo "error: not logged in to Azure. Run 'az login' yourself, then re-run this script." >&2
  exit 1
fi

# --tier Cool at upload time, not just via the lifecycle rule: the rule in storage.bicep evaluates
# once a day, so without this the archive would sit in Hot for up to ~24 h on every re-upload.
# --auth-mode key for the same reason RUNBOOK §3 uses it for tiles: ARM Owner does not imply
# data-plane blob RBAC, and the CLI fetches the account key via ARM internally.
echo "==> uploading (Cool tier)"
az storage blob upload-batch \
  --account-name "$ACCOUNT" \
  --destination "$CONTAINER" \
  --source "$PARQUET_DIR" \
  --pattern 'nbi_*.parquet' \
  --tier Cool \
  --overwrite \
  --auth-mode key \
  --output none

az storage blob upload \
  --account-name "$ACCOUNT" \
  --container-name "$CONTAINER" \
  --name "$(basename "$CATALOG")" \
  --file "$CATALOG" \
  --tier Cool \
  --overwrite \
  --auth-mode key \
  --output none

echo "==> Done. ${#FILES[@]} vintage(s) + the manifest are in ${ACCOUNT}/${CONTAINER}."
echo "    Verify:  az storage blob list --account-name $ACCOUNT --container-name $CONTAINER --auth-mode key --query \"length(@)\""
