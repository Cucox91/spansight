# Trend fixture (FR-1.2)

Three structures, six vintages, fourteen observations — hand-written, not generated.

The DuckDB job that computes real aggregates is golden-tested separately, against the era fixtures,
in `SpanSight.Core.Tests/Analytics/ConditionTrendGoldenTests.cs`. This fixture exists for the other
half: the loader and the API. It is written by hand *on purpose*, so that a bug in the job cannot
produce an expectation that the API tests then agree with. The two halves check each other.

The three structures are real Miami-Dade records from `nbi_sample_2025.csv`, so a bridge that has a
history here also has a detail record to open in the same test database.

| Structure | Span | Series | What it pins |
|---|---|---|---|
| `1213483000` | 2020–2025 | `776653` | An ordinary declining history: Good → Fair → Poor. |
| `1238705001` | 2020–2025 | `55.554` | A **gap** — no 2022 record. Five observations across a six-year span. |
| `1254730002` | 2023–2025 | `6U6` | An **unrated** year: published, but with no numeric rating. Not a gap, not a Poor. |

`rollup.csv` summarises exactly those fourteen observations, so the loader's own reconciliation
check and the API's totals both have something exact to agree with:

| Year | Total | Good | Fair | Poor | Unrated |
|---|---|---|---|---|---|
| 2020 | 2 | 1 | 1 | 0 | 0 |
| 2021 | 2 | 1 | 1 | 0 | 0 |
| 2022 | 1 | 0 | 1 | 0 | 0 |
| 2023 | 3 | 0 | 3 | 0 | 0 |
| 2024 | 3 | 0 | 2 | 0 | 1 |
| 2025 | 3 | 0 | 1 | 2 | 0 |

2 + 2 + 1 + 3 + 3 + 3 = **14**, which is 6 + 5 + 3 — the observed-year counts of the three series.
Every structure sits in county `12086`, so the county and state rollups carry identical counts and
a regression that mixes the two levels up is invisible in the totals but caught by the level filter.
