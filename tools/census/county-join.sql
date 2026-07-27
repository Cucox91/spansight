-- tools/census/county-join.sql — FR-1.5 AC-2: the bridge→county point-in-polygon join, as SQL.
--
-- Run it with tools/census/join-counties.sh; this file is the whole method. It expects three
-- relations from its caller, and nothing else:
--
--   bridge_source(state_code, structure_number, record_type, county_code, lon, lat)
--       one row per served structure, longitude/latitude already in WGS84 lon/lat.
--   county_source(geoid, statefp, countyfp, name, namelsad, aland, awater, tiger_vintage, geom)
--       TIGER county polygons, already lon/lat (OGC:CRS84), from tools/census/convert.sh.
--   population_source(geoid, pop_est, pop_moe, acs_vintage, acs_period, acs_table)
--       ACS 5-year table B01003, from the same converter.
--
-- The build script points bridge_source at the serving database for a real run and at a committed
-- hand-placed CSV for a fixture run, so CI golden-tests the *same* SQL that produces the published
-- numbers. Every column is named explicitly and nothing does SELECT * on a source relation — that is
-- what lets a six-column hand-written CSV stand in for 741k rows of PostGIS.
--
-- WHY THE BRIDGE POINTS COME FROM THE SERVING DATABASE, NOT FROM THE VINTAGE PARQUET.
-- Two reasons, and both are about the number meaning what it says. First, AC-2 asks for the share of
-- *bridges* matched to a polygon, and the bridges a reader can see are the rows in core.bridge —
-- computing coverage over a different population would publish a percentage for an inventory nobody
-- is served. Second, the WGS84 point is produced by NbiDmsCoordinateConverter, which decodes items
-- 016/017 from degrees-minutes-seconds and negates the west-positive longitude NBI publishes. Redoing
-- that in SQL would be a second implementation of the one conversion the repo has tested hardest, and
-- the failure mode is silent: a missed negation puts every American bridge in Asia and this job would
-- report ~0% coverage rather than an error. Reading the converted point means there is one decoder.
--
-- WHAT THIS COMPUTES, AND WHAT IT DOES NOT (GR-6).
-- It computes which published county polygon each published bridge coordinate falls inside, and
-- compares that with the county code FHWA published for the same structure. It is a data-quality
-- measurement, not a correction: nothing here overrides item 3. SpanSight displays published data,
-- so the county a bridge is *reported* in stays the county FHWA reported, and the whole product —
-- the county filter, the FR-1.2 trend rollups, the FR-1.4 report card — remains keyed on that
-- published code. This join exists to say how often the two disagree and where, which is the story
-- AC-2 asks to be surfaced rather than smoothed away.

--------------------------------------------------------------------------------------------------
-- 0. The published constants.
--------------------------------------------------------------------------------------------------
-- Declared once and carried outward: the build script writes them into manifest.json, the loader
-- stamps them onto the run row, and the API reads them from there. Nothing downstream re-derives
-- them, so the containment rule a coverage figure was measured under travels with the figure.
CREATE OR REPLACE VIEW cj_method AS
SELECT
  -- The containment predicate, named so the manifest and the QA page can state it rather than imply
  -- it. ST_Within excludes the boundary: a point lying exactly on a shared county line is inside
  -- neither county and is quarantined instead of being assigned to whichever polygon sorts first.
  -- Picking one would be SpanSight inventing a geography the publisher did not state, and the case
  -- is real — bridges frequently ARE the county line, because the boundary follows the river they
  -- cross. Measured on the 2025 national load: 2 structures, both recovered as boundary misses below.
  'ST_Within'  AS containment_predicate,

  -- How far the nearest-county search looks when a point is inside no polygon at all, in degrees.
  -- Bounded because it is a cross join against every county; a miss with nothing inside the radius
  -- publishes a null distance rather than a fabricated one.
  2.0          AS nearest_search_degrees,

  -- The sign-convention guard (§7). NBI publishes longitude unsigned and west-positive, so a decoder
  -- regression would mirror every point into the eastern hemisphere and collapse this share, not the
  -- row count. Set below 1 so a genuine border effect cannot trip it and a mirrored hemisphere must.
  0.90         AS min_state_agreement_share,

  'v1.0'       AS method_version;

--------------------------------------------------------------------------------------------------
-- 1. The bridge side.
--------------------------------------------------------------------------------------------------
-- The published county key, built as text and never as arithmetic: 06037 is not the number 6037.
-- nullif(trim(...)) runs before any padding because lpad('', 3, '0') is '000', which is a real
-- published county code — padding first would turn an absence into a claim. The length guard is
-- DuckDB-specific: its lpad TRUNCATES a value longer than the target width, so lpad('0210', 3, '0')
-- silently yields '021' and joins the wrong county, where .NET's PadLeft would leave it alone. An
-- over-long code therefore becomes NULL here and is reported by cj_check_overlong_code rather than
-- quietly landing somewhere plausible (the same guard deterioration.sql:139-146 needed).
CREATE OR REPLACE VIEW cj_bridge AS
SELECT
  CAST(b.state_code AS VARCHAR)                                    AS state_code,
  CAST(b.structure_number AS VARCHAR)                              AS structure_number,
  CAST(b.record_type AS VARCHAR)                                   AS record_type,
  CAST(nullif(trim(b.county_code), '') AS VARCHAR)                 AS county_code_raw,
  CASE
    WHEN nullif(trim(b.state_code), '') IS NULL THEN NULL
    WHEN length(trim(b.state_code)) > 2 THEN NULL
    WHEN nullif(trim(b.county_code), '') IS NULL THEN NULL
    WHEN length(trim(b.county_code)) > 3 THEN NULL
    ELSE lpad(trim(b.state_code), 2, '0') || lpad(trim(b.county_code), 3, '0')
  END                                                              AS nbi_county_fips,
  CAST(b.lon AS DOUBLE)                                            AS lon,
  CAST(b.lat AS DOUBLE)                                            AS lat,
  ST_Point(CAST(b.lon AS DOUBLE), CAST(b.lat AS DOUBLE))           AS point
FROM bridge_source b;

--------------------------------------------------------------------------------------------------
-- 2. The county side.
--------------------------------------------------------------------------------------------------
-- Population is LEFT joined on purpose. 13 county-equivalents have a TIGER boundary and no ACS row
-- at all — American Samoa, Guam, the Northern Mariana Islands and the U.S. Virgin Islands — so a
-- population here is genuinely absent, not zero, and every consumer has to render it as an absence
-- (tools/census/README.md; catalog.json records the same 13 as boundaryWithoutPopulation).
CREATE OR REPLACE VIEW cj_county AS
SELECT
  CAST(c.geoid AS VARCHAR)          AS county_fips,
  CAST(c.statefp AS VARCHAR)        AS state_fips,
  CAST(c.name AS VARCHAR)           AS name,
  CAST(c.namelsad AS VARCHAR)       AS name_lsad,
  CAST(c.aland AS BIGINT)           AS land_area_m2,
  CAST(c.awater AS BIGINT)          AS water_area_m2,
  CAST(c.tiger_vintage AS SMALLINT) AS tiger_vintage,
  CAST(p.pop_est AS BIGINT)         AS population,
  CAST(p.pop_moe AS BIGINT)         AS population_moe,
  CAST(p.acs_vintage AS SMALLINT)   AS acs_vintage,
  CAST(p.acs_period AS VARCHAR)     AS acs_period,
  CAST(p.acs_table AS VARCHAR)      AS acs_table,
  c.geom                            AS geom
FROM county_source c
LEFT JOIN population_source p ON p.geoid = c.geoid;

--------------------------------------------------------------------------------------------------
-- 3. The assignment (AC-2).
--------------------------------------------------------------------------------------------------
-- One row per structure that falls strictly inside exactly one county polygon. ST_Within is the
-- predicate named in cj_method; because it excludes the boundary, this relation cannot contain a
-- structure twice through a shared edge, and cj_check_assignment_unique proves that rather than
-- assuming it.
CREATE OR REPLACE VIEW cj_assignment AS
SELECT
  b.state_code,
  b.structure_number,
  b.record_type,
  b.nbi_county_fips,
  c.county_fips,
  c.state_fips
FROM cj_bridge b
JOIN cj_county c ON ST_Within(b.point, c.geom);

--------------------------------------------------------------------------------------------------
-- 4. The misses, with reasons (AC-2).
--------------------------------------------------------------------------------------------------
-- A miss is quarantined with a reason and the evidence to act on it, never dropped. The two reasons
-- are distinguishable facts about the geometry, not severity labels:
--
--   on_county_boundary            the point touches at least one polygon but is inside none of them,
--                                 i.e. it sits exactly on a boundary line. Recovering it would mean
--                                 choosing between the counties that share the line (§0).
--   outside_all_county_polygons   no polygon touches the point. TIGER boundaries stop at the
--                                 international border and at the shoreline, and NBI structures
--                                 legitimately sit on both, so the nearest county and the distance
--                                 to it are recorded — that is what separates a metre of shoreline
--                                 slop from a coordinate in the wrong ocean.
CREATE OR REPLACE VIEW cj_unmatched AS
SELECT b.*
FROM cj_bridge b
LEFT JOIN cj_assignment a
       ON a.state_code = b.state_code
      AND a.structure_number = b.structure_number
      AND a.record_type = b.record_type
WHERE a.county_fips IS NULL;

-- Counties whose polygon the point touches without being inside it. Evaluated only over the misses,
-- so this is a small cross join and not a second national spatial pass.
CREATE OR REPLACE VIEW cj_touching AS
SELECT
  u.state_code,
  u.structure_number,
  u.record_type,
  count(*)                    AS touching_counties,
  min(c.county_fips)          AS first_touching_county_fips
FROM cj_unmatched u
JOIN cj_county c ON ST_Intersects(u.point, c.geom)
GROUP BY 1, 2, 3;

-- The radius arrives as a scalar subquery rather than as a cross-joined column: in `FROM u, m JOIN c
-- ON …` the ON clause belongs to the JOIN, so whether it may see `m` at all is dialect-dependent.
-- A point with no county inside the radius simply has no row here and keeps a null distance.
CREATE OR REPLACE VIEW cj_nearest AS
SELECT
  u.state_code,
  u.structure_number,
  u.record_type,
  min_by(c.county_fips, ST_Distance_Sphere(u.point, ST_ClosestPoint(c.geom, u.point))) AS nearest_county_fips,
  min(ST_Distance_Sphere(u.point, ST_ClosestPoint(c.geom, u.point)))                   AS nearest_distance_m
FROM cj_unmatched u
JOIN cj_county c
  ON ST_DWithin(u.point, c.geom, (SELECT nearest_search_degrees FROM cj_method))
GROUP BY 1, 2, 3;

CREATE OR REPLACE VIEW cj_miss AS
SELECT
  CAST(u.state_code AS VARCHAR)                       AS state_code,
  CAST(u.structure_number AS VARCHAR)                 AS structure_number,
  CAST(u.record_type AS VARCHAR)                      AS record_type,
  CAST(u.nbi_county_fips AS VARCHAR)                  AS nbi_county_fips,
  CAST(CASE WHEN t.touching_counties IS NULL THEN 'outside_all_county_polygons'
            ELSE 'on_county_boundary' END AS VARCHAR) AS reason,
  CAST(coalesce(t.touching_counties, 0) AS INTEGER)   AS touching_counties,
  CAST(coalesce(t.first_touching_county_fips, n.nearest_county_fips) AS VARCHAR) AS nearest_county_fips,
  -- A boundary point is zero metres from the polygon it touches; stating that explicitly is clearer
  -- than a null a reader has to interpret.
  CAST(CASE WHEN t.touching_counties IS NOT NULL THEN 0
            ELSE round(n.nearest_distance_m) END AS BIGINT)                      AS nearest_distance_m,
  CAST(round(u.lon, 6) AS DOUBLE)                     AS lon,
  CAST(round(u.lat, 6) AS DOUBLE)                     AS lat
FROM cj_unmatched u
LEFT JOIN cj_touching t
       ON t.state_code = u.state_code
      AND t.structure_number = u.structure_number
      AND t.record_type = u.record_type
LEFT JOIN cj_nearest n
       ON n.state_code = u.state_code
      AND n.structure_number = u.structure_number
      AND n.record_type = u.record_type;

--------------------------------------------------------------------------------------------------
-- 5. The cross-check against the published county code (AC-2).
--------------------------------------------------------------------------------------------------
-- Every matched structure is classified once, and the four kinds are exhaustive by construction:
--
--   agree                        the polygon is the county item 3 published.
--   different_county_same_state  the polygon is a different county of the same state. Border
--                                precision accounts for most of these; independent cities and
--                                retired county codes account for the rest.
--   different_state              the polygon belongs to another state entirely. A structure on a
--                                state-line river is the ordinary case.
--   county_not_published         item 3 carried no usable code, so there is nothing to compare.
--
-- Aggregated to (published code, polygon) pairs rather than published per structure: the pair is
-- what a reader can act on, it is three orders of magnitude smaller, and it keeps this job from
-- putting a per-structure geography of its own next to the published one.
-- One row per served structure, classified once. Everything downstream — the disagreement pairs, the
-- coverage row, the by-state diagnostic — reads this relation rather than repeating the CASE, because
-- a classification rule written twice is a classification rule that will eventually disagree with
-- itself. 'unmatched' is a fifth kind here so that the outcome relation covers every input row and
-- the reconciliation in §7 has something total to check against.
CREATE OR REPLACE VIEW cj_outcome AS
SELECT
  b.state_code,
  b.structure_number,
  b.record_type,
  b.nbi_county_fips,
  a.county_fips,
  a.state_fips,
  CASE
    WHEN a.county_fips IS NULL                          THEN 'unmatched'
    WHEN b.nbi_county_fips IS NULL                      THEN 'county_not_published'
    WHEN b.nbi_county_fips = a.county_fips              THEN 'agree'
    WHEN a.state_fips = substr(b.nbi_county_fips, 1, 2) THEN 'different_county_same_state'
    ELSE 'different_state'
  END AS kind
FROM cj_bridge b
LEFT JOIN cj_assignment a
       ON a.state_code = b.state_code
      AND a.structure_number = b.structure_number
      AND a.record_type = b.record_type;

-- The disagreeing pairs, with the one fact that explains most of them: whether the code item 3
-- published is a county TIGER still publishes at all. Connecticut replaced its eight counties with
-- nine planning regions for the 2022 vintage of the Census geography, while NBI item 3 goes on
-- publishing the eight legacy codes — so every Connecticut structure disagrees, and it disagrees
-- because the published code names a county that no longer exists in the boundary file rather than
-- because the coordinate is wrong. Distinguishing the two is the whole value of this relation.
CREATE OR REPLACE VIEW cj_disagreement AS
SELECT
  CAST(x.nbi_county_fips AS VARCHAR)                  AS nbi_county_fips,
  CAST(x.county_fips AS VARCHAR)                      AS county_fips,
  CAST(x.kind AS VARCHAR)                             AS kind,
  CAST(count(*) AS INTEGER)                           AS bridges,
  -- Null, not false, when item 3 published nothing: there is no code, so "is that code still in the
  -- boundary file" is a question that does not apply. Writing false would read as "the published
  -- code is retired" for a row whose whole point is that no code was published.
  -- count() ignores nulls, so `= 0` means no row in this group published a code at all. Written this
  -- way rather than as a bare `x.nbi_county_fips IS NULL` because the GROUP BY is ordinal and the
  -- grouped expression is the CAST above, so DuckDB's binder does not accept the unwrapped column.
  CAST(CASE WHEN count(x.nbi_county_fips) = 0 THEN NULL
            ELSE max(CASE WHEN t.county_fips IS NULL THEN 0 ELSE 1 END) END AS BOOLEAN) AS nbi_fips_in_tiger
FROM cj_outcome x
LEFT JOIN cj_county t ON t.county_fips = x.nbi_county_fips
WHERE x.kind NOT IN ('agree', 'unmatched')
GROUP BY 1, 2, 3;

--------------------------------------------------------------------------------------------------
-- 6. The published relations.
--------------------------------------------------------------------------------------------------
-- Every TIGER county is published, including the 27 that carry no NBI-coded structure at all. A
-- report card asked for one of them must be able to say "no structures published here" with a name
-- and a population attached; omitting the row would make an empty county indistinguishable from a
-- bad request, and it is exactly the nine Connecticut planning regions that land in that set.
CREATE OR REPLACE VIEW cj_county_out AS
SELECT
  county_fips, state_fips, name, name_lsad, land_area_m2, water_area_m2,
  tiger_vintage, population, population_moe, acs_vintage, acs_period, acs_table
FROM cj_county;

-- Coverage, in one row. Both denominators are published rather than one: record type 1 is the
-- structure itself and is what "bridges" means everywhere else in the product (FR-1.2, FR-1.3),
-- while the all-records figure describes every row the serving table actually holds. Reporting only
-- the first would understate what was measured; reporting only the second would answer a different
-- question than AC-2 asks.
CREATE OR REPLACE VIEW cj_coverage AS
SELECT
  CAST(count(*) AS BIGINT)                                          AS bridges,
  CAST(count(*) FILTER (kind <> 'unmatched') AS BIGINT)             AS matched,
  CAST(count(*) FILTER (kind = 'unmatched') AS BIGINT)              AS unmatched,
  CAST(count(*) FILTER (record_type = '1') AS BIGINT)               AS structures,
  CAST(count(*) FILTER (record_type = '1' AND kind <> 'unmatched') AS BIGINT) AS structures_matched,
  CAST(count(*) FILTER (kind = 'agree') AS BIGINT)                            AS agree,
  CAST(count(*) FILTER (kind = 'different_county_same_state') AS BIGINT)      AS different_county_same_state,
  CAST(count(*) FILTER (kind = 'different_state') AS BIGINT)                  AS different_state,
  CAST(count(*) FILTER (kind = 'county_not_published') AS BIGINT)             AS county_not_published
FROM cj_outcome;

--------------------------------------------------------------------------------------------------
-- 7. Invariants. Every view here returns the VIOLATING ROWS; the build script refuses to write
--    anything while any of them is non-empty.
--------------------------------------------------------------------------------------------------

-- A structure must be assigned at most once. ST_Within should make a second assignment impossible,
-- but "should" is what a check is for: a duplicated county polygon in the boundary file, or a
-- predicate changed to ST_Intersects without changing anything else, both land here.
CREATE OR REPLACE VIEW cj_check_assignment_unique AS
SELECT state_code, structure_number, record_type, count(*) AS assignments
FROM cj_assignment
GROUP BY 1, 2, 3
HAVING count(*) > 1;

-- Assigned plus quarantined must equal the input, with nothing counted twice and nothing lost.
-- Deliberately a FULL OUTER JOIN against the source rather than arithmetic over cj_coverage: a row
-- that vanished from both sides is invisible to a sum of the two sides, and that is precisely the
-- failure this exists to catch. A relation that only re-adds columns one GROUP BY already produced
-- cannot fail — the tautological check deterioration.sql shipped once and had to fix.
CREATE OR REPLACE VIEW cj_check_reconciles AS
SELECT
  coalesce(b.state_code, o.state_code)             AS state_code,
  coalesce(b.structure_number, o.structure_number) AS structure_number,
  coalesce(b.record_type, o.record_type)           AS record_type,
  o.outcomes
FROM cj_bridge b
FULL OUTER JOIN (
  SELECT state_code, structure_number, record_type, count(*) AS outcomes FROM (
    SELECT state_code, structure_number, record_type FROM cj_assignment
    UNION ALL
    SELECT state_code, structure_number, record_type FROM cj_miss
  ) GROUP BY 1, 2, 3
) o ON o.state_code = b.state_code
   AND o.structure_number = b.structure_number
   AND o.record_type = b.record_type
WHERE b.state_code IS NULL OR o.outcomes IS DISTINCT FROM 1;

-- The coverage row must be the counts, recomputed independently from the detail relations rather
-- than read back out of the same aggregate that produced it.
CREATE OR REPLACE VIEW cj_check_coverage_totals AS
SELECT
  v.bridges, v.matched, v.unmatched,
  (SELECT count(*) FROM cj_bridge)     AS actual_bridges,
  (SELECT count(*) FROM cj_assignment) AS actual_matched,
  (SELECT count(*) FROM cj_miss)       AS actual_unmatched
FROM cj_coverage v
WHERE v.bridges   IS DISTINCT FROM (SELECT count(*) FROM cj_bridge)
   OR v.matched   IS DISTINCT FROM (SELECT count(*) FROM cj_assignment)
   OR v.unmatched IS DISTINCT FROM (SELECT count(*) FROM cj_miss);

-- The sign-convention and datum guard (§0). NBI publishes longitude west-positive and unsigned; a
-- decoder that stopped negating, or a source read in the wrong axis order, mirrors every point out
-- of the country. Row counts survive that unchanged — the join simply matches nothing, or matches
-- absurdly — so the guard is the share of matched structures whose polygon is in the state item 1
-- published. Measured 99.9% on the 2025 national load; the floor is 90%.
-- Note the first disjunct. Written only as a share of what matched, this check is blind to the very
-- failure it exists for: a mirrored hemisphere matches NO polygon, so cj_assignment would be empty,
-- the GROUP BY would produce no group, and a job that assigned nothing at all would pass every
-- invariant in this file — the reconciliation is satisfied by every row being a miss. "Matched
-- nothing" is therefore a violation in its own right, tested separately from "matched the wrong
-- states". The share itself is taken over matched rows rather than over all served rows because
-- unmatched rows are legitimately not in any state's polygon.
CREATE OR REPLACE VIEW cj_check_sign_convention AS
SELECT
  (SELECT count(*) FROM cj_bridge)                                            AS bridges,
  (SELECT count(*) FROM cj_assignment)                                        AS matched,
  (SELECT count(*) FROM cj_assignment WHERE state_fips = state_code)          AS same_state,
  m.min_state_agreement_share
FROM cj_method m
WHERE (SELECT count(*) FROM cj_bridge) > 0
  AND ((SELECT count(*) FROM cj_assignment) = 0
    OR (SELECT count(*) FROM cj_assignment WHERE state_fips = state_code)
       < m.min_state_agreement_share * (SELECT count(*) FROM cj_assignment));

-- A published code longer than its field is dropped to NULL by cj_bridge rather than truncated into
-- a different county (§1). That is the safe behaviour, but it must never be silent.
CREATE OR REPLACE VIEW cj_check_overlong_code AS
SELECT state_code, structure_number, record_type, county_code_raw
FROM cj_bridge
WHERE county_code_raw IS NOT NULL
  AND nbi_county_fips IS NULL
  AND (length(trim(state_code)) > 2 OR length(county_code_raw) > 3);

-- The county key is text and exactly five digits on both sides, and it is the concatenation the
-- converter already asserts on the TIGER side. A key that stops being comparable would show up as a
-- collapse in agreement, not as an error, so it is checked directly.
CREATE OR REPLACE VIEW cj_check_county_key AS
SELECT county_fips, state_fips, name
FROM cj_county
WHERE county_fips IS NULL
   OR length(county_fips) <> 5
   OR county_fips !~ '^[0-9]{5}$'
   OR county_fips <> state_fips || substr(county_fips, 3, 3);

-- Every miss carries a reason from the published vocabulary, and the two reasons must stay
-- distinguishable: a boundary miss touches a polygon, an outside miss does not.
CREATE OR REPLACE VIEW cj_check_miss_reason AS
SELECT state_code, structure_number, record_type, reason, touching_counties
FROM cj_miss
WHERE reason NOT IN ('on_county_boundary', 'outside_all_county_polygons')
   OR (reason = 'on_county_boundary' AND touching_counties < 1)
   OR (reason = 'outside_all_county_polygons' AND touching_counties <> 0);

-- Every disagreement pair names a polygon that exists. The published side may legitimately name a
-- retired county (that is the Connecticut case and nbi_fips_in_tiger records it), but the polygon
-- side came from the boundary file and must always resolve.
CREATE OR REPLACE VIEW cj_check_disagreement_resolves AS
SELECT d.nbi_county_fips, d.county_fips, d.kind, d.bridges
FROM cj_disagreement d
LEFT JOIN cj_county c ON c.county_fips = d.county_fips
WHERE c.county_fips IS NULL
   OR d.bridges <= 0
   OR d.kind NOT IN ('different_county_same_state', 'different_state', 'county_not_published');

--------------------------------------------------------------------------------------------------
-- 8. Diagnostics. Published into the manifest so the shape of the gap is recorded at the time it was
--    measured, not rediscovered later. None of these filters anything.
--------------------------------------------------------------------------------------------------

-- How far outside the county set the unmatched points actually fall. A metre of shoreline slop and a
-- coordinate in the wrong ocean are both "unmatched", and only this distinguishes them.
CREATE OR REPLACE VIEW cj_diagnostic_miss_distance AS
SELECT
  CAST(bucket AS VARCHAR) AS bucket,
  CAST(count(*) AS INTEGER) AS misses
FROM (
  SELECT CASE
    WHEN reason = 'on_county_boundary'      THEN 'on a county boundary'
    WHEN nearest_distance_m IS NULL         THEN 'no county within the search radius'
    WHEN nearest_distance_m < 100           THEN 'under 100 m from a county'
    WHEN nearest_distance_m < 1000          THEN '100 m to 1 km'
    WHEN nearest_distance_m < 10000         THEN '1 km to 10 km'
    ELSE 'over 10 km'
  END AS bucket
  FROM cj_miss
)
GROUP BY 1;

-- Where the disagreement is concentrated. Connecticut is expected to dominate and the point of
-- publishing this is that the expectation is checkable rather than asserted.
CREATE OR REPLACE VIEW cj_diagnostic_disagreement_by_state AS
SELECT
  CAST(substr(nbi_county_fips, 1, 2) AS VARCHAR) AS state_fips,
  CAST(sum(bridges) AS INTEGER)                  AS bridges,
  CAST(count(*) AS INTEGER)                      AS pairs,
  CAST(sum(bridges) FILTER (NOT nbi_fips_in_tiger) AS INTEGER) AS bridges_under_retired_code
FROM cj_disagreement
WHERE nbi_county_fips IS NOT NULL
GROUP BY 1
HAVING sum(bridges) > 0;

-- County codes item 3 publishes that the boundary file no longer carries. This is the retired-code
-- population stated directly, so the Connecticut effect is a number in the manifest rather than an
-- inference from a percentage.
CREATE OR REPLACE VIEW cj_diagnostic_retired_codes AS
SELECT
  CAST(b.nbi_county_fips AS VARCHAR) AS nbi_county_fips,
  CAST(count(*) AS INTEGER)          AS bridges
FROM cj_bridge b
LEFT JOIN cj_county c ON c.county_fips = b.nbi_county_fips
WHERE b.nbi_county_fips IS NOT NULL
  AND c.county_fips IS NULL
GROUP BY 1;

-- Counties with a boundary and no ACS population. Expected to be the four island areas; published so
-- that a report card rendering "population not published" can be traced to a measured absence.
CREATE OR REPLACE VIEW cj_diagnostic_population_absent AS
SELECT
  CAST(state_fips AS VARCHAR) AS state_fips,
  CAST(count(*) AS INTEGER)   AS counties
FROM cj_county
WHERE population IS NULL
GROUP BY 1;
