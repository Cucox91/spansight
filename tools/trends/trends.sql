-- tools/trends/trends.sql — FR-1.2 AC-1: the condition-history job, as SQL.
--
-- Run it with tools/trends/build-trends.sh; this file is the whole method and is meant to be read.
-- It expects one thing from its caller: a relation named `nbi_source` with the normalized vintage
-- columns. build-trends.sh points that at the real Parquet set or at the committed era fixtures,
-- so CI golden-tests the *same* SQL that produces the published numbers.
--
-- Everything here is a descriptive statistic of published NBI condition ratings (GR-6). Nothing is
-- predicted, smoothed, extrapolated or filled in: a year FHWA did not publish a structure in is a
-- gap, and stays a gap.

--------------------------------------------------------------------------------------------------
-- 1. One row per structure per year.
--------------------------------------------------------------------------------------------------
-- Two decisions live here, and both change the published numbers, so both are stated in full.
--
-- (a) RECORD_TYPE_005A = '1' only.
--     Item 5A distinguishes the route carried *on* the structure ('1') from routes going under or
--     alongside it ('2', and 'A'..'Z' for further routes). FHWA published all 28 record types in
--     1992-2009 and only type '1' from 2010, which makes the raw row count fall off a cliff at the
--     boundary — 713,115 rows in 2009 to 604,493 in 2010, a 15% "loss" that is entirely an artefact
--     of what got published. Type-1 only: 608,419 to 604,493, a 0.6% change.
--
--     The filter is not just cosmetic. Non-type-1 rows describe a route, not the structure, and
--     carry no structure condition at all: of the 18,036 rows in 1992 belonging to structures with
--     no type-1 record, exactly 0 have a numeric rating in items 58/59/60/62. Keeping them would
--     double-count structures *and* manufacture Unknown observations out of records that never
--     claimed to report condition.
--
-- (b) Deterministic tie-break on SOURCE_ROW.
--     (state, structure number) is not quite unique even within type '1' — a handful of genuine
--     collisions exist in the published files (two different structures sharing a number in one
--     state; 954 keys in 1995, 2 in 2025). We keep the row that appears first in the published
--     file, which is reproducible from the source alone. build-trends.sh reports the count per year
--     rather than hiding it.
CREATE OR REPLACE VIEW trend_structure_year AS
WITH on_structure AS (
  SELECT
    VINTAGE_YEAR                              AS vintage_year,
    STATE_CODE_001                            AS state_code,
    STRUCTURE_NUMBER_008                      AS structure_number,
    COUNTY_CODE_003                           AS county_code,
    SOURCE_ROW                                AS source_row,
    -- Mirrors SpanSight.Core.Domain.ConditionClassifier.LowestRating character for character: the
    -- trimmed value counts only when it is exactly one digit, so 'N', blanks and anything longer
    -- are ignored rather than coerced. (The Parquet also carries *_NUM columns from a plain
    -- TRY_CAST; the two rules agree on all 89,229,448 published condition values, verified
    -- 2026-07-26. The explicit rule is used anyway so this file can be checked against the C# by
    -- eye, and so a change to convert.sh cannot silently move a published trend.)
    least(
      CASE WHEN regexp_matches(trim(DECK_COND_058),           '^[0-9]$') THEN CAST(trim(DECK_COND_058)           AS TINYINT) END,
      CASE WHEN regexp_matches(trim(SUPERSTRUCTURE_COND_059), '^[0-9]$') THEN CAST(trim(SUPERSTRUCTURE_COND_059) AS TINYINT) END,
      CASE WHEN regexp_matches(trim(SUBSTRUCTURE_COND_060),   '^[0-9]$') THEN CAST(trim(SUBSTRUCTURE_COND_060)   AS TINYINT) END,
      CASE WHEN regexp_matches(trim(CULVERT_COND_062),        '^[0-9]$') THEN CAST(trim(CULVERT_COND_062)        AS TINYINT) END
    )                                         AS lowest_rating
  FROM nbi_source
  WHERE RECORD_TYPE_005A = '1'
),
deduped AS (
  SELECT *, row_number() OVER (
    PARTITION BY vintage_year, state_code, structure_number ORDER BY source_row
  ) AS pick
  FROM on_structure
)
SELECT
  vintage_year,
  state_code,
  structure_number,
  county_code,
  lowest_rating,
  -- The same thresholds as ConditionClassifier.Classify: Good >= 7, Fair 5-6, Poor <= 4, and
  -- Unknown when the record carries no numeric rating at all. Unknown is a real published state
  -- (culvert records rate item 62 and leave 58-60 as 'N'), never a stand-in for "missing year".
  CASE
    WHEN lowest_rating IS NULL THEN 'Unknown'
    WHEN lowest_rating >= 7    THEN 'Good'
    WHEN lowest_rating >= 5    THEN 'Fair'
    ELSE                            'Poor'
  END                                         AS condition_class
FROM deduped
WHERE pick = 1;

--------------------------------------------------------------------------------------------------
-- 2. Per-bridge series (output a).
--------------------------------------------------------------------------------------------------
-- One row per structure, spanning its own first to last observed year. `ratings` has one character
-- per year in that span:
--
--   '0'..'9'  the lowest published rating that year
--   'U'       published that year, but with no numeric rating (Unknown)
--   '.'       not published that year — a gap, never interpolated
--
-- The string is a storage encoding, not a statistic: the API expands it back into years. It exists
-- because the serving Postgres is a B1ms and 34 skinny rows per bridge is ~21M rows for something
-- only ever read one whole bridge at a time (ADR-005 — the serving DB gets compact aggregates).
CREATE OR REPLACE VIEW trend_bridge_series AS
WITH observed AS (
  SELECT
    state_code,
    structure_number,
    vintage_year,
    CASE WHEN lowest_rating IS NULL THEN 'U' ELSE CAST(lowest_rating AS VARCHAR) END AS mark
  FROM trend_structure_year
),
bounds AS (
  SELECT state_code, structure_number, min(vintage_year) AS first_year, max(vintage_year) AS last_year
  FROM observed
  GROUP BY 1, 2
),
grid AS (
  SELECT b.state_code, b.structure_number, b.first_year, b.last_year,
         unnest(range(b.first_year, b.last_year + 1)) AS vintage_year
  FROM bounds b
)
SELECT
  g.state_code,
  g.structure_number,
  CAST(g.first_year AS SMALLINT)                                   AS first_year,
  CAST(g.last_year  AS SMALLINT)                                   AS last_year,
  CAST(count(o.mark) AS SMALLINT)                                  AS observed_years,
  string_agg(coalesce(o.mark, '.'), '' ORDER BY g.vintage_year)     AS ratings
FROM grid g
LEFT JOIN observed o
  ON  o.state_code       = g.state_code
  AND o.structure_number = g.structure_number
  AND o.vintage_year     = g.vintage_year
GROUP BY 1, 2, 3, 4;

--------------------------------------------------------------------------------------------------
-- 3. County and state rollups (output b).
--------------------------------------------------------------------------------------------------
-- Counts and shares of Good/Fair/Poor/Unknown per area per year, summarising exactly the rows in
-- trend_structure_year — so the rollups and the per-bridge series are two views of one number set
-- and must reconcile exactly (AC-3).
--
-- The area key is whatever the vintage itself published: a bridge that changes county code between
-- vintages moves between rollups in that year, because that is what the published record says.
-- Connecticut is the loud case — Census replaced its 8 counties with 9 planning regions in 2022 and
-- NBI followed, so CT county series break there by construction (see tools/census/README.md).
CREATE OR REPLACE VIEW trend_rollup AS
WITH classified AS (
  SELECT
    'state'                            AS level,
    state_code                         AS fips,
    vintage_year,
    condition_class
  FROM trend_structure_year
  UNION ALL
  SELECT
    'county'                           AS level,
    state_code || county_code          AS fips,
    vintage_year,
    condition_class
  FROM trend_structure_year
  -- A county rollup needs a county. Rows with no published county code are counted at state level
  -- only; build-trends.sh reports how many, so the gap is visible instead of silently absorbed.
  WHERE county_code IS NOT NULL AND trim(county_code) <> ''
)
SELECT
  level,
  fips,
  CAST(vintage_year AS SMALLINT)                                          AS vintage_year,
  CAST(count(*) AS INTEGER)                                               AS total,
  CAST(count(*) FILTER (condition_class = 'Good')    AS INTEGER)          AS good,
  CAST(count(*) FILTER (condition_class = 'Fair')    AS INTEGER)          AS fair,
  CAST(count(*) FILTER (condition_class = 'Poor')    AS INTEGER)          AS poor,
  CAST(count(*) FILTER (condition_class = 'Unknown') AS INTEGER)          AS unknown
FROM classified
GROUP BY 1, 2, 3;

--------------------------------------------------------------------------------------------------
-- 4. Self-checks — run by build-trends.sh, and the same assertions the tests make.
--------------------------------------------------------------------------------------------------
-- Every one of these must return zero rows. They are written as "show me the violations" rather
-- than as booleans so a failure names the offending year or bridge instead of just saying false.

-- The state rollup must equal the per-structure rows it summarises, year by year.
CREATE OR REPLACE VIEW trend_check_state_totals AS
SELECT r.vintage_year, r.fips, r.total AS rollup_total, s.n AS structure_rows
FROM trend_rollup r
JOIN (
  SELECT vintage_year, state_code, count(*) AS n FROM trend_structure_year GROUP BY 1, 2
) s ON s.vintage_year = r.vintage_year AND s.state_code = r.fips
WHERE r.level = 'state' AND r.total <> s.n;

-- Class counts must partition the total — no row counted twice, none dropped.
CREATE OR REPLACE VIEW trend_check_class_partition AS
SELECT level, fips, vintage_year, total, good + fair + poor + unknown AS summed
FROM trend_rollup
WHERE good + fair + poor + unknown <> total;

-- Every county row must roll up into its own state's total for that year.
CREATE OR REPLACE VIEW trend_check_county_within_state AS
SELECT c.vintage_year, c.state_fips, c.county_total, s.total AS state_total
FROM (
  SELECT vintage_year, substr(fips, 1, 2) AS state_fips, sum(total) AS county_total
  FROM trend_rollup WHERE level = 'county' GROUP BY 1, 2
) c
JOIN trend_rollup s ON s.level = 'state' AND s.fips = c.state_fips AND s.vintage_year = c.vintage_year
WHERE c.county_total > s.total;

-- The packed series must be exactly as long as the span it claims, and must start and end on an
-- observation (a leading or trailing '.' would mean the bounds were computed wrong).
CREATE OR REPLACE VIEW trend_check_series_shape AS
SELECT state_code, structure_number, first_year, last_year, ratings
FROM trend_bridge_series
WHERE length(ratings) <> (last_year - first_year + 1)
   OR starts_with(ratings, '.')
   OR ends_with(ratings, '.')
   OR observed_years <> length(replace(ratings, '.', ''));

-- The series and the rollups must agree on how many observations exist in each year: expand the
-- packed strings back out and compare to the state rollup totals.
CREATE OR REPLACE VIEW trend_check_series_vs_rollup AS
WITH expanded AS (
  SELECT
    state_code,
    first_year + CAST(g.i AS INTEGER) - 1 AS vintage_year,
    substr(ratings, CAST(g.i AS INTEGER), 1) AS mark
  FROM trend_bridge_series, unnest(range(1, length(ratings) + 1)) AS g(i)
),
from_series AS (
  SELECT vintage_year, state_code, count(*) AS n FROM expanded WHERE mark <> '.' GROUP BY 1, 2
),
from_rollup AS (
  SELECT vintage_year, fips AS state_code, total AS n FROM trend_rollup WHERE level = 'state'
)
SELECT coalesce(a.vintage_year, b.vintage_year) AS vintage_year,
       coalesce(a.state_code, b.state_code)     AS state_code,
       a.n AS series_rows, b.n AS rollup_rows
FROM from_series a
FULL OUTER JOIN from_rollup b USING (vintage_year, state_code)
WHERE a.n IS DISTINCT FROM b.n;
