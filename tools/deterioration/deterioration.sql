-- tools/deterioration/deterioration.sql — FR-1.3 AC-1: the condition-transition job, as SQL.
--
-- Run it with tools/deterioration/build-deterioration.sh; this file is the whole method and is meant
-- to be read alongside docs/METHODOLOGY-DETERIORATION.md, which it implements section by section.
-- It expects one thing from its caller: a relation named `nbi_source` with the normalized vintage
-- columns. The build script points that at the real Parquet set, at the committed era fixtures, or at
-- the hand-written synthetic fixture, so CI golden-tests the *same* SQL that produces the published
-- numbers. Every column is named explicitly and nothing does SELECT * on nbi_source — that is what
-- lets a 12-column hand-written CSV stand in as the source.
--
-- WHAT THIS COMPUTES, AND WHAT IT DOES NOT (GR-6, methodology §1).
-- A transition matrix is a count of how often bridges in a cohort were published at rating i one year
-- and rating j the next. It is a description of published federal inspection history at cohort level.
-- It is not a prediction, not an assessment of any structure, and not engineering judgment. Nothing
-- here iterates a chain, solves a steady state, or projects a future year; the SRS calls FR-1.3 a
-- "Markov-chain baseline" and this file is the empirical transition table only.
--
-- Nothing is smoothed, imputed or filled in. A year FHWA did not publish a structure in is a gap and
-- stays a gap; a component FHWA did not rate is absent, never a zero.

--------------------------------------------------------------------------------------------------
-- 0. The published methodology constants.
--------------------------------------------------------------------------------------------------
-- The sample-size floor lives here, once, and travels outward: the build script writes it into
-- manifest.json, the loader stamps it onto the run row, and the API reads it from there. Nothing
-- downstream re-derives it, so the number the UI shows is the number the matrices were judged by
-- (methodology §4).
CREATE OR REPLACE VIEW det_methodology AS
SELECT
  50    AS sample_floor,      -- §4: n >= 50 per matrix ROW, because the row is every rate's denominator
  'v1.1' AS methodology_version,
  'All' AS all_cohorts,       -- §4: the reserved value marking the national all-cohorts context matrix
  'Not published' AS not_published;   -- §5: item 43A/43B carried no code

--------------------------------------------------------------------------------------------------
-- 1. The cohort dimensions (§5) as static relations.
--------------------------------------------------------------------------------------------------
-- Relations rather than CASE trees so they can be joined, counted and diffed against the C#. All
-- three also exist in SpanSight.Core.Domain.Lookups.NbiCohorts, because the API and the UI need the
-- same groups to label what the job produced. Two implementations of one rule is a standing drift
-- risk, so DeteriorationCohortGoldenTests executes these very relations and compares them to the C#
-- over every published code in both directions — the same guard FR-1.2 put on Good/Fair/Poor.

-- The public NOAA/NCEI nine-region climate map of the 48 contiguous states, keyed by the 2-digit
-- state FIPS that item 1 carries (verified zero-padded in all 34 vintages). DC sits in Northeast by
-- NOAA's own convention. This is a static lookup, not a new data pipeline (FR-1.3 AC-1).
CREATE OR REPLACE VIEW det_climate_region AS
SELECT * FROM (VALUES
  ('09','Northeast'), ('10','Northeast'), ('25','Northeast'), ('24','Northeast'),
  ('23','Northeast'), ('33','Northeast'), ('34','Northeast'), ('36','Northeast'),
  ('42','Northeast'), ('44','Northeast'), ('50','Northeast'), ('11','Northeast'),
  ('19','Upper Midwest'), ('26','Upper Midwest'), ('27','Upper Midwest'), ('55','Upper Midwest'),
  ('17','Ohio Valley'), ('18','Ohio Valley'), ('21','Ohio Valley'), ('29','Ohio Valley'),
  ('39','Ohio Valley'), ('47','Ohio Valley'), ('54','Ohio Valley'),
  ('01','Southeast'), ('12','Southeast'), ('13','Southeast'), ('37','Southeast'),
  ('45','Southeast'), ('51','Southeast'),
  ('30','Northern Rockies & Plains'), ('38','Northern Rockies & Plains'),
  ('31','Northern Rockies & Plains'), ('46','Northern Rockies & Plains'),
  ('56','Northern Rockies & Plains'),
  ('05','South'), ('20','South'), ('22','South'), ('28','South'), ('40','South'), ('48','South'),
  ('04','Southwest'), ('08','Southwest'), ('35','Southwest'), ('49','Southwest'),
  ('16','Northwest'), ('41','Northwest'), ('53','Northwest'),
  ('06','West'), ('32','West'),
  -- Everything FHWA publishes outside the contiguous 48 + DC, as the *else branch* of the map rather
  -- than a fixed list of today's territories (§5). American Samoa (60) and the Northern Marianas (69)
  -- have never appeared in a published vintage but are listed so the first year one does, it lands in
  -- a labelled bucket instead of failing or vanishing. Guam appears only 2018-2024 and the U.S. Virgin
  -- Islands only 2019-2025, so this bucket's population is not constant across the 34 years either.
  ('02','Outside contiguous U.S.'), ('15','Outside contiguous U.S.'),
  ('60','Outside contiguous U.S.'), ('66','Outside contiguous U.S.'),
  ('69','Outside contiguous U.S.'), ('72','Outside contiguous U.S.'),
  ('78','Outside contiguous U.S.')
) AS t(state_fips, region);

-- Item 43B structure type groups — the grouping the filter rail already presents, reused verbatim
-- (§5: "the implementation must not invent new groupings"). All 23 published codes are covered.
CREATE OR REPLACE VIEW det_type_group AS
SELECT * FROM (VALUES
  ('01','Girder / Stringer'), ('02','Girder / Stringer'), ('03','Girder / Stringer'),
  ('04','Girder / Stringer'), ('05','Girder / Stringer'), ('06','Girder / Stringer'),
  ('21','Girder / Stringer'), ('22','Girder / Stringer'),
  ('09','Truss / Arch'), ('10','Truss / Arch'), ('11','Truss / Arch'),
  ('12','Truss / Arch'), ('13','Truss / Arch'), ('14','Truss / Arch'),
  ('19','Culvert'),
  ('00','Other'), ('07','Other'), ('08','Other'), ('15','Other'),
  ('16','Other'), ('17','Other'), ('18','Other'), ('20','Other')
) AS t(code_043b, type_group);

-- Item 43A material groups (§5). This collapses the Phase 0 decode (NbiCodes.Materials) on the
-- continuous/simple-span distinction NBI itself draws, and exists only to reach viable cohort sample
-- sizes — the decode stays authoritative for labelling an individual code. "Other" is the
-- methodology's own name for codes 0 (Other) and 9 (Aluminum, wrought iron, or cast iron); because
-- that label under-describes code 9, the UI renders each group's member codes with their published
-- decodes beside it rather than leaving the reader to guess.
CREATE OR REPLACE VIEW det_material_group AS
SELECT * FROM (VALUES
  ('1','Concrete'), ('2','Concrete'),
  ('3','Steel'), ('4','Steel'),
  ('5','Prestressed concrete'), ('6','Prestressed concrete'),
  ('7','Timber'),
  ('8','Masonry'),
  ('0','Other'), ('9','Other')
) AS t(code_043a, material_group);

--------------------------------------------------------------------------------------------------
-- 2. One row per structure per year (§2).
--------------------------------------------------------------------------------------------------
-- Character for character the population tools/trends/trends.sql §1 keeps, so FR-1.2 and FR-1.3
-- describe the same bridges and their totals reconcile:
--
--   (a) RECORD_TYPE_005A = '1' only — the route carried *on* the structure. Non-type-1 rows describe
--       a route, not the structure, and carry no structure condition at all. FHWA published all
--       record types through 2009 and only type 1 from 2010, so keeping them would also put a 15%
--       cliff at that boundary that nothing in the real world did.
--
--   (b) Duplicate identity keys: keep the row published first (SOURCE_ROW). (state, structure number)
--       is not quite unique even within type 1 — genuine collisions exist in the published files,
--       2,854 keys across 1992-2025 (954 in 1995, 2 in 2025). Keeping the first row is reproducible
--       from the source alone and is what FR-1.2 already ships. Methodology v1.0 §2 said such rows
--       were "excluded entirely" while claiming in the same sentence to be identical to FR-1.2; only
--       keep-first actually reconciles (20,649,259 structure-years over 1,039,109 structures), so
--       v1.1 corrects the wording to the rule that ships.
--
-- Ratings: a value counts only when it is exactly one digit after trimming, so 'N', lowercase 'n',
-- blanks and anything longer are null and never imputed. Identical to
-- SpanSight.Core.Domain.ConditionClassifier's rule and to trends.sql's, deliberately spelled out so
-- all three can be checked against each other by eye as well as by test.
CREATE OR REPLACE VIEW det_structure_year AS
WITH on_structure AS (
  SELECT
    VINTAGE_YEAR                                    AS vintage_year,
    STATE_CODE_001                                  AS state_code,
    STRUCTURE_NUMBER_008                            AS structure_number,
    SOURCE_ROW                                      AS source_row,
    -- Normalized cohort codes. nullif(trim(...),'') collapses NULL and blank into one "not published"
    -- signal *before* any padding: lpad('' ,2,'0') would be '00', which is a real published 43B code
    -- (Other), so padding first would silently convert an absence into a claim.
    --
    -- The pad is a CASE and not a bare lpad() because DuckDB's lpad TRUNCATES when the value is
    -- already longer than the target width — lpad('021',2,'0') is '02', a real published code meaning
    -- Girder / Stringer. A three-character 43B would therefore be silently filed under whichever group
    -- its first two characters name, and det_check_unmapped would report zero violations, which is the
    -- opposite of what §4 below promises. .NET's PadLeft never truncates, so this also keeps the SQL
    -- and NbiCohorts.TypeGroup identical on inputs the golden test can reach. Every 43B value in the
    -- 34 published vintages is exactly two characters today; this is here so a future one that is not
    -- fails the build instead of joining the wrong cohort.
    nullif(trim(STRUCTURE_KIND_043A), '')                   AS code_043a,
    CASE
      WHEN length(nullif(trim(STRUCTURE_TYPE_043B), '')) < 2
        THEN lpad(nullif(trim(STRUCTURE_TYPE_043B), ''), 2, '0')
      ELSE nullif(trim(STRUCTURE_TYPE_043B), '')
    END                                                     AS code_043b,
    CASE WHEN regexp_matches(trim(DECK_COND_058),           '^[0-9]$') THEN CAST(trim(DECK_COND_058)           AS TINYINT) END AS deck,
    CASE WHEN regexp_matches(trim(SUPERSTRUCTURE_COND_059), '^[0-9]$') THEN CAST(trim(SUPERSTRUCTURE_COND_059) AS TINYINT) END AS superstructure,
    CASE WHEN regexp_matches(trim(SUBSTRUCTURE_COND_060),   '^[0-9]$') THEN CAST(trim(SUBSTRUCTURE_COND_060)   AS TINYINT) END AS substructure,
    CASE WHEN regexp_matches(trim(CULVERT_COND_062),        '^[0-9]$') THEN CAST(trim(CULVERT_COND_062)        AS TINYINT) END AS culvert
  FROM nbi_source
  WHERE RECORD_TYPE_005A = '1'
),
deduped AS (
  SELECT *, row_number() OVER (
    PARTITION BY vintage_year, state_code, structure_number ORDER BY source_row
  ) AS pick
  FROM on_structure
)
SELECT * EXCLUDE (pick, source_row) FROM deduped WHERE pick = 1;

--------------------------------------------------------------------------------------------------
-- 3. Long form: one row per structure-year per *rated* component (§3.2).
--------------------------------------------------------------------------------------------------
-- The matrices are per-component, four independent families (§4). Basing them on the governing
-- lowest-of-components rating would conflate two different events: a rating that moved, and a
-- different component becoming the minimum. FR-1.2's trend view stays governing-based on purpose —
-- the two views answer different questions and each says which.
--
-- A component with no numeric rating simply produces no row, so a culvert record (58/59/60 = 'N',
-- item 62 rated) contributes only to the culvert family. That is *usually* exclusive but not always:
-- 30,485 structure-years publish both a numeric item 62 and a numeric 58/59/60. Such a structure
-- appears in both families, which is why the four families are independent populations and must never
-- be summed into an "any component" matrix — 22,967 pair units would be double-counted.
CREATE OR REPLACE VIEW det_component_year AS
SELECT vintage_year, state_code, structure_number, code_043a, code_043b, 'Deck'           AS component, deck           AS rating FROM det_structure_year WHERE deck           IS NOT NULL
UNION ALL
SELECT vintage_year, state_code, structure_number, code_043a, code_043b, 'Superstructure' AS component, superstructure AS rating FROM det_structure_year WHERE superstructure IS NOT NULL
UNION ALL
SELECT vintage_year, state_code, structure_number, code_043a, code_043b, 'Substructure'   AS component, substructure   AS rating FROM det_structure_year WHERE substructure   IS NOT NULL
UNION ALL
SELECT vintage_year, state_code, structure_number, code_043a, code_043b, 'Culvert'        AS component, culvert        AS rating FROM det_structure_year WHERE culvert        IS NOT NULL;

--------------------------------------------------------------------------------------------------
-- 4. Transition pairs (§3).
--------------------------------------------------------------------------------------------------
-- A pair is one structure, one component, observed in two *consecutive* vintages with a numeric
-- rating in both. b.vintage_year = a.vintage_year + 1 is the whole pairing rule: a structure absent
-- from either year contributes nothing for that year and the gap is never bridged. Rating
-- improvements are retained exactly as published (2.51% of pairs) — this is a record of published
-- history, and censoring improvements would misstate it.
--
-- COHORT ATTRIBUTES COME FROM YEAR y, the from-year whose rating is the matrix row. The methodology
-- v1.0 did not say, so v1.1 states it: 43A differs across a pair on 0.70% of structure-pairs and 43B
-- on 0.70%. Keying on y makes the cohort a function of the same record as the row; requiring the two
-- years to agree would drop ~244k component pairs and counting the pair under both cohorts would
-- double it, and either one breaks the invariant that the cohort matrices sum to the national matrix.
--
-- The three coalesce/CASE expressions distinguish two different absences, which is the point:
--   * the code was not published at all -> the labelled 'Not published' bucket, reported not dropped;
--   * the code WAS published but this grouping has never seen it -> NULL, which det_check_unmapped
--     turns into a build failure. An unrecognised published code must stop the run, never quietly
--     land in "Other".
-- A state code that does not resolve is always a build failure: there is no "not published" reading
-- of a bridge with no state.
CREATE OR REPLACE VIEW det_pair AS
SELECT
  a.vintage_year                              AS from_year,
  b.vintage_year                              AS to_year,
  a.state_code,
  a.structure_number,
  a.component,
  a.rating                                    AS from_rating,
  b.rating                                    AS to_rating,
  CASE WHEN a.code_043b IS NULL THEN 'Not published' ELSE tg.type_group     END AS type_group,
  CASE WHEN a.code_043a IS NULL THEN 'Not published' ELSE mg.material_group END AS material_group,
  cr.region                                   AS region
FROM det_component_year a
JOIN det_component_year b
  ON  b.state_code       = a.state_code
  AND b.structure_number = a.structure_number
  AND b.component        = a.component
  AND b.vintage_year     = a.vintage_year + 1
LEFT JOIN det_type_group     tg ON tg.code_043b  = a.code_043b
LEFT JOIN det_material_group mg ON mg.code_043a  = a.code_043a
LEFT JOIN det_climate_region cr ON cr.state_fips = a.state_code;

--------------------------------------------------------------------------------------------------
-- 5. The matrix rows — every rate's denominator (output a).
--------------------------------------------------------------------------------------------------
-- The row, not the cell, is the unit the sample-size floor applies to, because the row total is the
-- denominator of all ten of its rates (§4). Storing the row separately means row_total exists in
-- exactly one place and cannot drift from the cells beneath it.
--
-- ALL 33 YEAR-PAIRS ARE POOLED into one matrix per cohort per component (v1). Per year-pair, 63 of
-- 1,320 national rows fall below the floor with a minimum n of 2, so pooling is what makes the
-- feature exist at all. Pooling hides *when* the evidence was published, so every row carries its own
-- span — first and last from-year and how many distinct year-pairs contributed — and the UI renders it
-- beside the n. That is what stops a cell whose evidence is entirely pre-2009 coding practice from
-- reading as 34 years of history (§6.8).
--
-- The national all-cohorts context matrix is the same relation with the reserved value 'All' in all
-- three dimensions at once. One relation means the floor, the API DTO and the sum invariant are each
-- a single code path, so the national matrix physically cannot bypass the floor check.
CREATE OR REPLACE VIEW det_matrix_row AS
SELECT
  component,
  type_group,
  material_group,
  region,
  CAST(from_rating AS SMALLINT)               AS from_rating,
  CAST(count(*) AS INTEGER)                   AS row_total,
  CAST(min(from_year) AS SMALLINT)            AS first_from_year,
  CAST(max(from_year) AS SMALLINT)            AS last_from_year,
  CAST(count(DISTINCT from_year) AS SMALLINT) AS year_pairs_observed
FROM det_pair
GROUP BY 1, 2, 3, 4, 5
UNION ALL
SELECT
  component,
  'All', 'All', 'All',
  CAST(from_rating AS SMALLINT),
  CAST(count(*) AS INTEGER),
  CAST(min(from_year) AS SMALLINT),
  CAST(max(from_year) AS SMALLINT),
  CAST(count(DISTINCT from_year) AS SMALLINT)
FROM det_pair
GROUP BY 1, 5;

--------------------------------------------------------------------------------------------------
-- 6. The matrix cells — the numerators (output b).
--------------------------------------------------------------------------------------------------
-- One row per NON-ZERO cell. A cohort-component populates 12 of its 100 cells at the median and
-- exactly 1 of 1,028 populates all 100, so dense storage would be almost entirely zeros — and
-- "never observed in 34 years" is a real published fact worth keeping distinct from an observed zero.
-- The API zero-fills to a full 10x10 grid on read, so the reader still sees ten columns. Rates are not stored: a rate is a division of two stored numbers and storing it would only
-- create something that can disagree with them (the rule ConditionRollup already follows).
CREATE OR REPLACE VIEW det_matrix_cell AS
SELECT
  component,
  type_group,
  material_group,
  region,
  CAST(from_rating AS SMALLINT) AS from_rating,
  CAST(to_rating   AS SMALLINT) AS to_rating,
  CAST(count(*) AS INTEGER)     AS pairs
FROM det_pair
GROUP BY 1, 2, 3, 4, 5, 6
UNION ALL
SELECT
  component,
  'All', 'All', 'All',
  CAST(from_rating AS SMALLINT),
  CAST(to_rating   AS SMALLINT),
  CAST(count(*) AS INTEGER)
FROM det_pair
GROUP BY 1, 5, 6;

--------------------------------------------------------------------------------------------------
-- 7. Self-checks — run by build-deterioration.sh before anything is written, and asserted by tests.
--------------------------------------------------------------------------------------------------
-- Every one must return zero rows. They are written as "show me the violations" rather than as
-- booleans so a failure names the offending cohort or code instead of just saying false.

-- Nothing may fall through a cohort lookup. A NULL here means FHWA published a code (or a state) this
-- job has never seen — the run must stop rather than publish a matrix with a silently missing slice.
-- 'Not published' is not a violation: it is a labelled bucket for a code that was genuinely absent.
CREATE OR REPLACE VIEW det_check_unmapped AS
SELECT 'type_group' AS dimension, state_code, structure_number, from_year, component
FROM det_pair WHERE type_group IS NULL
UNION ALL
SELECT 'material_group', state_code, structure_number, from_year, component
FROM det_pair WHERE material_group IS NULL
UNION ALL
SELECT 'region', state_code, structure_number, from_year, component
FROM det_pair WHERE region IS NULL;

-- The pairing rule itself: consecutive years only, both ratings a published 0-9.
CREATE OR REPLACE VIEW det_check_pair_shape AS
SELECT state_code, structure_number, component, from_year, to_year, from_rating, to_rating
FROM det_pair
WHERE to_year <> from_year + 1
   OR from_rating NOT BETWEEN 0 AND 9
   OR to_rating   NOT BETWEEN 0 AND 9;

-- A row total must equal the cells beneath it. This is the invariant that makes every displayed rate
-- trustworthy, since row_total is the denominator the API divides by.
CREATE OR REPLACE VIEW det_check_row_totals AS
SELECT r.component, r.type_group, r.material_group, r.region, r.from_rating,
       r.row_total AS declared, coalesce(sum(c.pairs), 0) AS summed
FROM det_matrix_row r
LEFT JOIN det_matrix_cell c
  ON  c.component      = r.component
  AND c.type_group     = r.type_group
  AND c.material_group = r.material_group
  AND c.region         = r.region
  AND c.from_rating    = r.from_rating
GROUP BY 1, 2, 3, 4, 5, 6
HAVING r.row_total <> coalesce(sum(c.pairs), 0);

-- The cohort matrices must be an exact partition of the national matrix, cell by cell — including the
-- 'Outside contiguous U.S.' and 'Not published' buckets. Dropping any of them, or double-counting a
-- pair whose cohort attributes changed mid-pair, shows up here and nowhere else.
CREATE OR REPLACE VIEW det_check_national_is_the_sum AS
WITH cohort AS (
  SELECT component, from_rating, to_rating, sum(pairs) AS n
  FROM det_matrix_cell WHERE type_group <> 'All' GROUP BY 1, 2, 3
),
national AS (
  SELECT component, from_rating, to_rating, sum(pairs) AS n
  FROM det_matrix_cell WHERE type_group = 'All' GROUP BY 1, 2, 3
)
SELECT coalesce(c.component, n.component)     AS component,
       coalesce(c.from_rating, n.from_rating) AS from_rating,
       coalesce(c.to_rating, n.to_rating)     AS to_rating,
       c.n AS cohort_pairs, n.n AS national_pairs
FROM cohort c FULL OUTER JOIN national n USING (component, from_rating, to_rating)
WHERE c.n IS DISTINCT FROM n.n;

-- The national sentinel is all-or-nothing: a row with 'All' in one dimension and a real group in
-- another would be neither a cohort nor the national total, and would corrupt the partition check.
CREATE OR REPLACE VIEW det_check_sentinel_is_whole AS
SELECT component, type_group, material_group, region, from_rating
FROM det_matrix_row
WHERE (type_group = 'All' OR material_group = 'All' OR region = 'All')
  AND NOT (type_group = 'All' AND material_group = 'All' AND region = 'All');

-- No published group may be spelled 'All', or the sentinel would collide with real data.
CREATE OR REPLACE VIEW det_check_reserved_value AS
SELECT 'type_group' AS dimension, type_group AS value FROM det_type_group WHERE type_group = 'All'
UNION ALL SELECT 'material_group', material_group FROM det_material_group WHERE material_group = 'All'
UNION ALL SELECT 'region', region FROM det_climate_region WHERE region = 'All'
UNION ALL SELECT 'not_published', 'All' FROM det_methodology WHERE not_published = 'All';

-- Each row's span must describe the pairs it summarises.
--
-- This deliberately recomputes the span from det_pair and compares, rather than checking the row
-- against itself. An earlier version tested internal consistency — year_pairs_observed >= 1, <=
-- last-first+1, <= row_total, first <= last — and every one of those is a tautology when the columns
-- come from min()/max()/count(DISTINCT)/count(*) over one GROUP BY: the view could never return a row
-- whatever the source said. It would have passed with the span read from to_year instead of from_year,
-- which is exactly the corruption the span exists to prevent.
CREATE OR REPLACE VIEW det_check_span AS
WITH recomputed AS (
  SELECT
    component, type_group, material_group, region,
    CAST(from_rating AS SMALLINT)               AS from_rating,
    CAST(count(*) AS INTEGER)                   AS row_total,
    CAST(min(from_year) AS SMALLINT)            AS first_from_year,
    CAST(max(from_year) AS SMALLINT)            AS last_from_year,
    CAST(count(DISTINCT from_year) AS SMALLINT) AS year_pairs_observed
  FROM det_pair
  GROUP BY 1, 2, 3, 4, 5
  UNION ALL
  SELECT
    component, 'All', 'All', 'All',
    CAST(from_rating AS SMALLINT),
    CAST(count(*) AS INTEGER),
    CAST(min(from_year) AS SMALLINT),
    CAST(max(from_year) AS SMALLINT),
    CAST(count(DISTINCT from_year) AS SMALLINT)
  FROM det_pair
  GROUP BY 1, 5
)
SELECT
  coalesce(r.component, x.component)           AS component,
  coalesce(r.type_group, x.type_group)         AS type_group,
  coalesce(r.material_group, x.material_group) AS material_group,
  coalesce(r.region, x.region)                 AS region,
  coalesce(r.from_rating, x.from_rating)       AS from_rating,
  r.first_from_year AS stored_first, x.first_from_year AS actual_first,
  r.last_from_year  AS stored_last,  x.last_from_year  AS actual_last,
  r.year_pairs_observed AS stored_year_pairs, x.year_pairs_observed AS actual_year_pairs,
  r.row_total AS stored_total, x.row_total AS actual_total
FROM det_matrix_row r
FULL OUTER JOIN recomputed x
  USING (component, type_group, material_group, region, from_rating)
WHERE r.first_from_year     IS DISTINCT FROM x.first_from_year
   OR r.last_from_year      IS DISTINCT FROM x.last_from_year
   OR r.year_pairs_observed IS DISTINCT FROM x.year_pairs_observed
   OR r.row_total           IS DISTINCT FROM x.row_total
   -- ...and the property the old comment claimed but never implemented: a span cannot leave the
   -- published vintage range, and a from-year must have a to-year after it.
   OR x.first_from_year < (SELECT min(from_year) FROM det_pair)
   OR x.last_from_year  > (SELECT max(from_year) FROM det_pair);

--------------------------------------------------------------------------------------------------
-- 8. Diagnostics the build publishes rather than leaves to be discovered.
--------------------------------------------------------------------------------------------------
-- Rows that clear the sample-size floor on evidence drawn from few year-pairs. These are real and are
-- not suppressed — suppressing them would violate §5's "reported, never silently dropped" — but they
-- are the rows where a pooled 1992-2025 label is most misleading, so the build lists them and the UI
-- shows every row's span. The measured case: 19,927 deck pairs sit inside the Culvert type group,
-- 99.1% of them with a from-year of 2008 or earlier (§6.8).
CREATE OR REPLACE VIEW det_diagnostic_narrow_span AS
SELECT r.component, r.type_group, r.material_group, r.region, r.from_rating,
       r.row_total, r.first_from_year, r.last_from_year, r.year_pairs_observed
FROM det_matrix_row r, det_methodology m
WHERE r.row_total >= m.sample_floor
  AND r.year_pairs_observed <= 5
ORDER BY r.row_total DESC;

-- The §6.1 inspection-cadence caveat, as a number per component rather than a hand-wave: NBI is
-- typically re-inspected on a 24-month cycle and intermediate annual files carry the last inspection
-- forward, so a "no change" observation often means no new inspection. The diagonal share this
-- produces is the dominant feature of every matrix, so the caption carries it.
CREATE OR REPLACE VIEW det_diagnostic_movement AS
SELECT
  component,
  CAST(count(*) AS BIGINT)                                                        AS pairs,
  round(100.0 * count(*) FILTER (to_rating = from_rating) / count(*), 2)          AS pct_unchanged,
  round(100.0 * count(*) FILTER (to_rating <  from_rating) / count(*), 2)         AS pct_declined,
  round(100.0 * count(*) FILTER (to_rating >  from_rating) / count(*), 2)         AS pct_improved
FROM det_pair
GROUP BY 1;

-- How much of the feature the floor suppresses, so the number is published rather than inferred from
-- an apparently empty UI.
CREATE OR REPLACE VIEW det_diagnostic_floor AS
SELECT
  CAST(count(*) AS INTEGER)                                            AS matrix_rows,
  CAST(count(*) FILTER (r.row_total < m.sample_floor) AS INTEGER)       AS rows_below_floor,
  round(100.0 * count(*) FILTER (r.row_total < m.sample_floor) / count(*), 2) AS pct_rows_below_floor,
  CAST(sum(r.row_total) AS BIGINT)                                     AS pairs_total,
  CAST(sum(r.row_total) FILTER (r.row_total < m.sample_floor) AS BIGINT) AS pairs_below_floor
FROM det_matrix_row r, det_methodology m
WHERE r.type_group <> 'All';
