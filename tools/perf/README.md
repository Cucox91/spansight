# The NFR-1 performance pass

NFR-1 (SRS §6) sets **300 ms p95** on a served request. This is how that claim is checked, and how
the numbers quoted in `docs/TRACEABILITY.md` are produced.

```bash
tools/perf/perf-pass.sh                    # every shape: latency + plans → artifacts/perf/
tools/perf/perf-pass.sh --only rank-       # just the ranking shapes
# the deployed API — note the API origin, NOT www.spansights.com (see below)
tools/perf/perf-pass.sh --api "$API_ORIGIN" --n 20
```

Requires `dotnet`, `psql`, `curl`, `python3`, and — for plans — `docker` with the local Postgres
container running. The script starts and stops the API itself.

`--api` measures a deployed API. Two things about it, both learned the hard way:

- **It must be the API origin, not the site.** `https://www.spansights.com/api/lookups` returns
  HTTP 200 with 488 bytes of `text/html` — the Static Web App rewrites unmatched paths to
  `index.html`, so a status-code check sails straight past it. The API lives at the Container App
  hostname (`ca-spansight-api-demo…azurecontainerapps.io`). The script now rejects a `text/html`
  response rather than timing the CDN.
- **The limiter is not raised remotely.** The `RateLimiting__PermitLimit` override only applies to
  a process this script starts. A deployed API runs the production policy — 100 requests per 10 s
  per IP — so keep `--n` well under that or most of the run will be measuring 429s. The report
  says so in its own header when `--api` was used.

## What it measures

Two things, in two passes, because they interfere:

| Pass | What | Why separately |
|---|---|---|
| 1 | End-to-end latency per shape, p50/p95/p99/max over *n* samples | This is what a reader waits for: routing, EF materialization, JSON serialization, the lot |
| 2 | The plan of every SQL statement each shape issues | `auto_explain` with `log_analyze` instruments every node of every query — leaving it on during pass 1 would measure the instrumentation |

Percentiles are nearest-rank with no interpolation, so every number printed is a request that
actually happened.

## Why the plans are captured and not written

The obvious way to do an EXPLAIN pass is to copy the SQL into a `psql` session and prefix it with
`EXPLAIN (ANALYZE, BUFFERS)`. That measures a statement nobody serves. The parameters bind
differently, the plan can differ from the one the driver gets, and — worst — the transcription is a
copy that rots silently the first time the LINQ changes, while the report keeps printing the old
plan and looking healthy.

So pass 2 enables `auto_explain` for the API's own database role and captures what Postgres logged
for the statements the API actually issued. Nothing here knows any SQL. A shape whose query changes
reports its new plan on the next run with no edit to this directory.

Two consequences worth knowing:

- **The script owns the API process.** `session_preload_libraries` is read when a connection opens,
  so an already-running API would never load the module. That is why it starts and stops the API
  rather than measuring one you started.
- **The role settings are reset on exit**, including on Ctrl-C, and cleared again at the start of
  the next run in case a previous one was killed hard.

## Adding a shape

Add a row to [`shapes.tsv`](./shapes.tsv). A shape that is not in that file is a shape the NFR-1
evidence does not cover, so an endpoint added without a row here is an endpoint nobody has measured.

Pick parameters at the **expensive** end: the largest county, the state with the most structures,
the deepest history, the widest cohort, the row cap rather than the default page. A shape measured
on its cheapest input is not measured. The script reports any shape that stops returning HTTP 200
rather than skipping it — a shape whose sample parameters have gone stale is a finding.

## Reading the report

`artifacts/perf/perf-<stamp>.md` opens with the row counts it measured against. That header is the
point: a 0.4 ms number means nothing without the size of the table it came from, and a run against
fixture data produces a report that says so in its own first table.

The numbers come from the dev Mac with a warm cache. The demo runs a **single-vCPU B1ms** with a
smaller cache and no parallel workers, so a shape whose plan relies on `Workers Launched: 2` will
cost more there than here. Treat the local number as a floor, and the margin against 300 ms as what
makes that difference survivable — which is also why a plan that says `Seq Scan` matters even when
the wall-clock is comfortable.
