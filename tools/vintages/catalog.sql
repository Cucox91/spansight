-- tools/vintages/catalog.sql — FR-1.1 AC-4: the DuckDB entry point for the NBI vintage catalog.
--
-- Usage, from the repository root:
--
--   duckdb -init tools/vintages/catalog.sql                       # interactive session
--   duckdb -init tools/vintages/catalog.sql -c "SELECT * FROM nbi_bridges_per_year"
--
-- Every published historical aggregate must be reproducible from a clean checkout plus the Parquet
-- set alone, with no database and no application code in the way. That is what this file is: the
-- views below are the only sanctioned way to read the catalog, so a number quoted in a PR, a doc or
-- the UI can always be re-derived by someone who has just cloned the repo.
--
-- Paths are relative to the repository root, so run it from there. Rebuild the Parquet set with
-- tools/vintages/convert.sh (see tools/vintages/README.md).

-- The whole 1992-2025 history as one relation.
--
-- The glob is deliberately NOT read with union_by_name: every vintage Parquet is written from the
-- same normalized superset (SpanSight.Core/Vintages/VintageSchema.cs), so identical columns in
-- identical order is an invariant, not a hope. If a file drifts, DuckDB should fail here and loudly
-- rather than quietly NULL-filling a column difference that means the converter needs re-running.
CREATE OR REPLACE VIEW nbi AS
SELECT * FROM read_parquet('data/vintages/parquet/nbi_*.parquet');

-- The same relation with the three renamed FHWA columns stitched into one continuous series.
--
-- FHWA published bridge condition, the lowest NBI rating and deck area under opaque computed names
-- (CAT10 / CAT23 / CAT29) in 2016-2018, then under readable names (BRIDGE_CONDITION /
-- LOWEST_RATING / DECK_AREA) from 2019. They are the same three quantities: verified 2026-07-26
-- against the real files, CAT10 agreed with FHWA's Good/Fair/Poor rule over items 58/59/60/62 on
-- 299,947 of 299,947 rows carrying condition data, CAT23 agreed with the minimum of those same four
-- items on 299,947 of 299,947, and CAT29 matched DECK_AREA's distribution exactly (same maximum,
-- median ratio of 1.0000 to STRUCTURE_LEN_MT_049 * DECK_WIDTH_MT_052 — the same square metres).
--
-- The Parquet itself keeps both spellings untouched, because it is a faithful copy of published
-- text. The coalescing happens here, once, visibly — so a 2016-2025 trend is continuous by an
-- explicit and auditable decision rather than by a rename buried in the converter.
--
-- Nothing before 2016 has these columns at all: they are NULL for 1992-2015, which is a real gap in
-- what FHWA published, not a conversion defect. Any trend that starts earlier must say so (GR-6).
CREATE OR REPLACE VIEW nbi_unified AS
SELECT
  *,
  coalesce(BRIDGE_CONDITION, CAT10)      AS BRIDGE_CONDITION_ALL,
  coalesce(LOWEST_RATING,    CAT23)      AS LOWEST_RATING_ALL,
  TRY_CAST(coalesce(DECK_AREA, CAT29) AS DOUBLE) AS DECK_AREA_ALL
FROM nbi;

-- The reconciliation manifest, queryable alongside the data it describes, so "does the Parquet
-- still match what the converter recorded?" is a join and not a manual diff.
CREATE OR REPLACE VIEW nbi_manifest AS
WITH raw AS (
  SELECT vintages FROM read_json('tools/vintages/catalog.json', columns = {vintages: 'JSON'})
),
-- catalog.json keys each vintage by year, so the years are object keys rather than rows.
entries AS (
  SELECT json_extract(vintages, '$."' || k || '"') AS v
  FROM (SELECT unnest(json_keys(vintages)) AS k, vintages FROM raw)
)
SELECT
  CAST(v ->> 'year'          AS SMALLINT) AS vintage_year,
  v ->> 'era'                             AS era,
  CAST(v ->> 'rowsInSource'  AS BIGINT)   AS rows_in_source,
  CAST(v ->> 'rowsConverted' AS BIGINT)   AS rows_converted,
  CAST(v ->> 'rowsRejected'  AS BIGINT)   AS rows_rejected,
  CAST(v -> 'parquet' ->> 'rows'  AS BIGINT) AS parquet_rows,
  CAST(v -> 'parquet' ->> 'bytes' AS BIGINT) AS parquet_bytes,
  v -> 'source' ->> 'url'                 AS source_url,
  v -> 'source' ->> 'fileSha256'          AS source_sha256
FROM entries;

-- The sample aggregate AC-4 is proved with: one row per vintage, straight off the Parquet.
CREATE OR REPLACE VIEW nbi_bridges_per_year AS
SELECT VINTAGE_YEAR AS vintage_year, count(*) AS bridges
FROM nbi
GROUP BY 1
ORDER BY 1;

-- Reconciliation as a query: every vintage must agree with its manifest row, and
-- converted + rejected must equal what was read from the source file.
CREATE OR REPLACE VIEW nbi_reconciliation AS
SELECT
  m.vintage_year,
  m.era,
  m.rows_in_source,
  m.rows_converted,
  m.rows_rejected,
  round(100.0 * m.rows_rejected / nullif(m.rows_in_source, 0), 4) AS reject_pct,
  y.bridges                                                        AS parquet_rows_now,
  (m.rows_converted + m.rows_rejected = m.rows_in_source)
    AND (y.bridges = m.rows_converted)                             AS reconciles
FROM nbi_manifest m
JOIN nbi_bridges_per_year y ON y.vintage_year = m.vintage_year
ORDER BY m.vintage_year;
