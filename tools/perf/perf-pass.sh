#!/usr/bin/env bash
# tools/perf/perf-pass.sh — the NFR-1 evidence pass: every serving query shape, measured.
#
# Usage:
#   tools/perf/perf-pass.sh                     # measure + capture plans, write a report
#   tools/perf/perf-pass.sh --n 500             # more samples per shape (default 200)
#   tools/perf/perf-pass.sh --no-explain        # latency only; touches no database settings
#   tools/perf/perf-pass.sh --api https://...   # measure a deployed API (implies --no-explain)
#   tools/perf/perf-pass.sh --only rank-        # just the shapes whose id starts with this
#
# NFR-1 (SRS §6) sets 300 ms p95 on a served request. Two things are measured for each shape in
# tools/perf/shapes.tsv, and they answer different questions:
#
#   end-to-end latency   what a reader waits for — routing, EF materialization, JSON, the lot.
#   the database plan    *why* it costs that, and whether it will still hold when the data grows.
#
# The plans are not hand-written SQL. Hand-transcribing an EF query into a psql session measures a
# statement nobody serves: the parameters bind differently, the plan can differ, and the
# transcription silently rots the first time the LINQ changes. This script instead turns on
# auto_explain for the API's own database role, so what lands in the report is the plan Postgres
# chose for the statement the API actually issued. A shape whose LINQ changes therefore reports its
# new plan on the next run with no edit here.
#
# The two run in SEPARATE passes, and that is not tidiness. auto_explain with log_analyze and
# log_timing instruments every node of every query; leaving it on during the timing loop measures
# the instrumentation as much as the endpoint (it added roughly 10-15% when this was first written).
# So: pass 1 times the shapes with the database in its normal state, pass 2 restarts the API with
# auto_explain enabled and issues one request per shape purely to capture plans. Restarting is the
# reason the script owns the API process at all — session_preload_libraries is read when a
# connection opens, so an already-running API would never load the module.
#
# The report states the row counts it measured against, in its own header. A run against fixture
# data produces a report that says so, which is the point: a 0.4 ms number means nothing without
# the size of the table it came from.
#
# Requires: dotnet, curl, psql, python3, and (for plans) docker with the local Postgres container.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

API="http://localhost:5194"
API_IS_LOCAL=1
SAMPLES=200
WARMUP=10
EXPLAIN=1
ONLY=""
OUT="artifacts/perf"
PGCONTAINER="${PGCONTAINER:-spansight-postgres-1}"
PGHOST_="${PGHOST_:-localhost}"
PGPORT_="${PGPORT_:-5432}"
PGUSER_="${PGUSER_:-spansight}"
PGDB_="${PGDB_:-spansight}"
export PGPASSWORD="${PGPASSWORD:-spansight}"

while [ $# -gt 0 ]; do
  case "$1" in
    --api) API="$2"; API_IS_LOCAL=0; EXPLAIN=0; shift 2 ;;
    --n) SAMPLES="$2"; shift 2 ;;
    --warmup) WARMUP="$2"; shift 2 ;;
    --no-explain) EXPLAIN=0; shift ;;
    --only) ONLY="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    -h|--help) sed -n '2,35p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

mkdir -p "$OUT"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
REPORT="$OUT/perf-$STAMP.md"
PLANS="$OUT/plans-$STAMP.txt"
RESULTS="$(mktemp)"
: > "$PLANS"

psql_() { psql -h "$PGHOST_" -p "$PGPORT_" -U "$PGUSER_" -d "$PGDB_" -qtAX "$@"; }

# ---------------------------------------------------------------- lifecycle
API_PID=""

start_api() {
  [ "$API_IS_LOCAL" = "1" ] || return 0

  # Refuse to start on top of something else. Kestrel would fail to bind, the child would exit, and
  # the readiness poll below would be satisfied by the foreign listener on its first iteration — so
  # the whole run would silently measure another process, against another database, behind the
  # production limiter. That is not hypothetical: it happened during development, with a stale API
  # from an e2e session still bound to 5194 serving a 99-row fixture.
  if lsof -nP -iTCP:"${API##*:}" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "error: something is already listening on ${API##*:}." >&2
    echo "  This script must own the API process — auto_explain is loaded when a connection opens," >&2
    echo "  so an API started before this run would never have it. Stop it and re-run:" >&2
    echo "    lsof -ti tcp:${API##*:} | xargs kill" >&2
    exit 1
  fi

  # The limiter is raised for the duration: 200 samples inside a 10 s window is exactly what the
  # production policy exists to refuse, and a 429 is not a latency measurement. Nothing else about
  # the process differs from how it is served.
  #
  # The EF logging override is a command-line argument, not an environment variable, and that is
  # not a style choice: the env provider maps `__` to `:` but leaves a single `_` alone, so
  # `Logging__LogLevel__Microsoft_EntityFrameworkCore_Database_Command` becomes a config key with
  # underscores, which matches no logger category. appsettings.Development.json pins that category
  # at Information, so the first version of this script measured every request with its full SQL
  # text being formatted and written inside the timed path — 19,476 command log entries in one run.
  RateLimiting__PermitLimit=1000000 \
  Otlp__Endpoint= \
  ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --project src/SpanSight.Api -c Release --no-build --launch-profile http \
    -- --Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Warning \
    >> "$OUT/api-$STAMP.log" 2>&1 &
  API_PID=$!

  for _ in $(seq 1 60); do
    # kill -0 first: if the child died (a bind failure, a bad connection string), stop rather than
    # let the poll find someone else's listener.
    if ! kill -0 "$API_PID" 2>/dev/null; then
      echo "API process exited during startup; see $OUT/api-$STAMP.log" >&2
      exit 1
    fi
    if curl -fsS "$API/readyz" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  echo "API did not become ready; see $OUT/api-$STAMP.log" >&2
  exit 1
}

stop_api() {
  [ -n "$API_PID" ] || return 0
  kill "$API_PID" 2>/dev/null || true
  wait "$API_PID" 2>/dev/null || true
  API_PID=""
  # Give the sockets a moment so the next start does not race the old listener off the port.
  sleep 2
}

explain_off() {
  psql_ -c "ALTER ROLE $PGUSER_ RESET session_preload_libraries;" >/dev/null 2>&1 || true
  for g in log_min_duration log_analyze log_buffers log_timing log_format; do
    psql_ -c "ALTER ROLE $PGUSER_ RESET auto_explain.$g;" >/dev/null 2>&1 || true
  done
}

cleanup() { stop_api; [ "$EXPLAIN" = "1" ] && explain_off; rm -f "$RESULTS"; }

# EXIT and the signals need different handlers. A bash trap handler returns to where it was
# interrupted, so a single `trap cleanup INT` would kill the API, reset the role, delete the
# results file — and then carry on measuring into a report built from whatever rows happened to
# land after the Ctrl-C, exiting 0. The signal handler therefore exits.
trap cleanup EXIT
trap 'echo; echo "interrupted — cleaning up" >&2; exit 130' INT TERM

# The role may still carry settings from an interrupted earlier run. Clearing them here is what
# makes pass 1 trustworthy — otherwise the "uninstrumented" numbers quietly are not.
[ "$EXPLAIN" = "1" ] && explain_off

# ---------------------------------------------------------------- scale of the data being measured
echo "==> reading the scale of the serving database"
SCALE="$(psql_ -c "
select 'core.bridge (all records)', to_char(count(*), 'FM999,999,999') from core.bridge
union all select 'core.bridge (record type 1)', to_char(count(*) filter (where record_type = '1'), 'FM999,999,999') from core.bridge
union all select 'analytics.bridge_condition_series', to_char(count(*), 'FM999,999,999') from analytics.bridge_condition_series
union all select 'analytics.condition_rollup', to_char(count(*), 'FM999,999,999') from analytics.condition_rollup
union all select 'analytics.deterioration_matrix_row', to_char(count(*), 'FM999,999,999') from analytics.deterioration_matrix_row
union all select 'analytics.deterioration_matrix_cell', to_char(count(*), 'FM999,999,999') from analytics.deterioration_matrix_cell
union all select 'analytics.census_county', to_char(count(*), 'FM999,999,999') from analytics.census_county;" \
  | awk -F'|' '{printf "| %s | %s |\n", $1, $2}')"

PG_VERSION="$(psql_ -c "select version();" | cut -d, -f1)"
PG_SETTINGS="$(psql_ -c "select name || '=' || setting || coalesce(unit, '') from pg_settings
  where name in ('shared_buffers','work_mem','effective_cache_size','max_parallel_workers_per_gather') order by name;" | tr '\n' ' ')"

# ---------------------------------------------------------------- pass 1: latency, uninstrumented
if [ "$API_IS_LOCAL" = "1" ]; then
  echo "==> building the API (Release)"
  dotnet build src/SpanSight.Api/SpanSight.Api.csproj -c Release --nologo -v quiet >/dev/null
fi

echo "==> pass 1: ${SAMPLES} samples per shape against $API (no instrumentation)"
start_api

while IFS=$'\t' read -r id requirement label path; do
  case "$id" in ''|'#'*) continue ;; esac
  [ -n "$path" ] || continue
  if [ -n "$ONLY" ]; then case "$id" in "$ONLY"*) ;; *) continue ;; esac; fi

  url="$API$path"
  probe="$(curl -s -o /dev/null -w '%{http_code} %{content_type} %{size_download}' "$url")"
  code="${probe%% *}"
  ctype="$(printf '%s' "$probe" | cut -d' ' -f2)"
  bytes="${probe##* }"

  # A 200 is not proof the API answered. Point --api at the Static Web App host and every /api
  # path comes back as the SPA shell — 200, text/html, 488 bytes — because staticwebapp.config.json
  # rewrites unmatched paths to index.html. Checking only the status code times a CDN handing back
  # HTML and fills the report with numbers that describe nothing.
  case "$ctype" in
    text/html*)
      echo "    !! $id -> 200 but Content-Type is $ctype ($path)"
      echo "       This is an HTML page, not an API response. If you passed --api, check that it" >&2
      echo "       names the API origin and not the SPA host." >&2
      printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "$id" "$requirement" "$label" "$path" "HTTP 200 text/html" "-" "-" "-" "-" >> "$RESULTS"
      continue
      ;;
  esac

  if [ "$code" != "200" ]; then
    # Loud, and recorded in the report. A shape that stopped returning rows is a finding, not a
    # row to quietly drop: the whole point of the file is that every shape is covered.
    echo "    !! $id -> HTTP $code  ($path)"
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
      "$id" "$requirement" "$label" "$path" "HTTP $code" "-" "-" "-" "-" >> "$RESULTS"
    continue
  fi

  for _ in $(seq 1 "$WARMUP"); do curl -s -o /dev/null "$url"; done

  read -r p50 p95 p99 max <<EOF
$(for _ in $(seq 1 "$SAMPLES"); do curl -s -o /dev/null -w '%{time_total}\n' "$url"; done | python3 -c '
import sys
xs = sorted(float(x) * 1000 for x in sys.stdin if x.strip())
def pct(p):
    # Nearest-rank: the smallest sample at or above p% of the run. No interpolation, so every
    # number printed is a request that actually happened.
    return xs[min(len(xs) - 1, max(0, (len(xs) * p + 99) // 100 - 1))]
print(f"{pct(50):.1f} {pct(95):.1f} {pct(99):.1f} {xs[-1]:.1f}")
')
EOF

  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$id" "$requirement" "$label" "$path" "$p50" "$p95" "$p99" "$max" "$bytes" >> "$RESULTS"
  printf '    %-22s p50 %8s ms   p95 %8s ms   max %8s ms   %s B\n' "$id" "$p50" "$p95" "$max" "$bytes"
done < tools/perf/shapes.tsv

stop_api

# ---------------------------------------------------------------- pass 2: plans
if [ "$EXPLAIN" = "1" ]; then
  echo "==> pass 2: capturing the plan of every statement each shape issues"
  psql_ -c "ALTER ROLE $PGUSER_ SET session_preload_libraries = 'auto_explain';" >/dev/null
  psql_ -c "ALTER ROLE $PGUSER_ SET auto_explain.log_min_duration = 0;" >/dev/null
  psql_ -c "ALTER ROLE $PGUSER_ SET auto_explain.log_analyze = on;" >/dev/null
  psql_ -c "ALTER ROLE $PGUSER_ SET auto_explain.log_buffers = on;" >/dev/null
  psql_ -c "ALTER ROLE $PGUSER_ SET auto_explain.log_timing = on;" >/dev/null
  psql_ -c "ALTER ROLE $PGUSER_ SET auto_explain.log_format = 'text';" >/dev/null
  start_api

  while IFS=$'\t' read -r id requirement label path; do
    case "$id" in ''|'#'*) continue ;; esac
    [ -n "$path" ] || continue
    if [ -n "$ONLY" ]; then case "$id" in "$ONLY"*) ;; *) continue ;; esac; fi

    url="$API$path"
    curl -s -o /dev/null "$url"   # warm, so the captured plan is the steady-state one
    # The Z matters: `docker logs --since` reads a bare timestamp as *local* time, so a UTC
    # string without it lands hours in the future and the window comes back empty — which reads
    # exactly like "this shape ran no queries".
    since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    sleep 1
    curl -s -o /dev/null "$url"
    sleep 1
    {
      echo "########## $id — $label"
      echo "########## $path"
      # A plan block is the `duration: … plan:` line plus its tab-indented continuation. Anything
      # else in the window (checkpoints, connection noise) is dropped.
      docker logs --since "$since" "$PGCONTAINER" 2>&1 \
        | awk '/LOG:  duration: .* plan:/ {p = 1; print; next} /^\t/ {if (p) print; next} {p = 0}' \
        | sed 's/^/  /'
      echo
    } >> "$PLANS"
    printf '    %-22s plans captured\n' "$id"
  done < tools/perf/shapes.tsv

  stop_api
  explain_off
fi

# ---------------------------------------------------------------- report
{
  echo "# NFR-1 performance pass — $STAMP"
  echo
  if [ "$API_IS_LOCAL" = "1" ]; then
    echo "Every serving query shape in \`tools/perf/shapes.tsv\`, measured end to end against a"
    echo "locally served API over the data below. NFR-1 (SRS §6) sets **300 ms p95**."
  else
    echo "Every serving query shape in \`tools/perf/shapes.tsv\`, measured end to end against"
    echo "\`$API\`. NFR-1 (SRS §6) sets **300 ms p95**."
    echo
    echo "> **Read these numbers carefully.** This was a remote run. The row counts below come from"
    echo "> the *local* database this script could reach, not from whatever the remote API is serving."
    echo "> The rate limiter was **not** raised — that override only applies to a process this script"
    echo "> starts — so a remote API on the default policy (100 requests per 10 s per IP) will reject"
    echo "> most of a 200-sample run, and curl records each rejection as a fast sample. Treat a remote"
    echo "> p95 as a smoke check, not as NFR-1 evidence, and keep \`--n\` under the permit limit."
  fi
  echo
  echo "Regenerate with \`tools/perf/perf-pass.sh\`. Plans for every shape: \`$(basename "$PLANS")\`."
  echo
  echo "## The data these numbers came from"
  echo
  echo "| Table | Rows |"
  echo "|---|---:|"
  printf '%s\n' "$SCALE"
  echo
  echo "\`$PG_VERSION\` — $PG_SETTINGS"
  echo
  echo "Dev Mac, warm cache, $SAMPLES samples per shape after $WARMUP warm-up requests$(
    [ "$API_IS_LOCAL" = "1" ] && printf ', with the\nrate limiter raised for the duration')."
  echo "Latency was measured with the database in its normal"
  echo "state; plans were captured in a second pass, because \`auto_explain\` instrumentation shows"
  echo "up in the timings. The demo runs a single-vCPU B1ms with a smaller cache and no parallel"
  echo "workers, so treat these as the floor: the margin against 300 ms is what makes the"
  echo "difference survivable."
  echo
  echo "## Results"
  echo
  echo "| Shape | Req | p50 ms | p95 ms | p99 ms | max ms | Bytes |"
  echo "|---|---|---:|---:|---:|---:|---:|"
  while IFS=$'\t' read -r id requirement label path p50 p95 p99 max bytes; do
    printf '| `%s`<br><sub>%s</sub> | %s | %s | %s | %s | %s | %s |\n' \
      "$id" "$label" "$requirement" "$p50" "$p95" "$p99" "$max" "$bytes"
  done < "$RESULTS"
  echo
  awk -F'\t' '
    $6 ~ /^[0-9.]+$/ { n++; if ($6 + 0 > m) { m = $6 + 0; s = $1 } if ($6 + 0 > 300) breach = breach " " $1 }
    $5 ~ /^HTTP/ { failed = failed " `" $1 "` (" $5 ")"; f++ }
    END {
      # A verdict is only ever printed from measurements. A run where every shape 404s, or an
      # --only filter that matches nothing, used to print "No shape breaches the 300 ms target"
      # over zero rows — an NFR-1 pass drawn from no evidence at all.
      if (n == 0) {
        print "**No shape was measured.** No conclusion about NFR-1 can be drawn from this run."
      } else {
        printf "%d shapes measured. Slowest: `%s` at %s ms p95.\n", n, s, m
        if (breach == "") print "\nNo measured shape breaches the 300 ms NFR-1 target."
        else printf "\n**Over the 300 ms NFR-1 target:**%s\n", breach
      }
      if (f > 0) printf "\n**%d shape(s) did not return 200 and are unmeasured:**%s\n", f, failed
    }' "$RESULTS"
} > "$REPORT"

echo
echo "==> $REPORT"
[ "$EXPLAIN" = "1" ] && echo "==> $PLANS"
exit 0
