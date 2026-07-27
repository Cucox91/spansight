namespace SpanSight.Core.Tests.Analytics;

/// <summary>
/// FR-1.5 AC-2 — the bridge→county point-in-polygon join, held against hand-computed expectations.
/// <para>
/// Every test here executes <c>tools/census/county-join.sql</c> itself, wired the way
/// <c>join-counties.sh --fixtures</c> wires it, over real six-county TIGER geometry and the 17
/// hand-placed points in <c>src/tests/fixtures/census-join/</c>. The expected numbers and the reason
/// each row exists are stated in that fixture's README; these tests are that document made executable.
/// </para>
/// </summary>
public sealed class CountyJoinGoldenTests : IDisposable
{
    private readonly string _init = DuckDb.IsAvailable ? DuckDb.CreateCountyJoinInitScript() : string.Empty;

    public void Dispose()
    {
        if (_init.Length > 0)
        {
            File.Delete(_init);
        }
    }

    /// <summary>
    /// The whole coverage row in one comparison. Asserted as a single pipe-joined literal rather than
    /// field by field so a change to any of the nine numbers fails once, with the full before/after
    /// visible in the diff.
    /// </summary>
    [CountyJoinFact]
    public void Coverage_row_matches_the_hand_computed_fixture()
    {
        var actual = DuckDb.Run(
            """
            SELECT bridges, matched, unmatched, structures, structures_matched,
                   agree, different_county_same_state, different_state, county_not_published
            FROM cj_coverage;
            """,
            _init);

        // 16 rows in, 13 inside a county, 3 quarantined. 15 are record type 1 and 12 of those match
        // (row 7 is the type-2 route record). Of the 13 matched: 10 agree with item 3, one lands in
        // another county of the same state, one in another state, and one published no county code.
        Assert.Equal("16|13|3|15|12|10|1|1|1", actual);
    }

    /// <summary>
    /// Each of the five outcomes is reachable and lands on the structure the fixture placed there.
    /// The coverage row above proves the counts; this proves the counts are made of the right rows,
    /// which a compensating pair of errors would otherwise satisfy.
    /// </summary>
    [CountyJoinTheory]
    [InlineData("LA-0001", "1", "agree", "06037", "Three ordinary interior points; a single-row bug cannot pass.")]
    [InlineData("CK-0001", "2", "agree", "17031", "Record type 2 shares a structure number with type 1 — the key is the full natural key.")]
    [InlineData("AJ-0001", "1", "agree", "72001", "Puerto Rico: in the ACS file, outside the published national total.")]
    [InlineData("LV-0002", "1", "different_county_same_state", "48301", "Published 48389, coordinate in Loving.")]
    [InlineData("XS-0001", "1", "different_state", "06037", "Published 12086, coordinate in California.")]
    [InlineData("CK-0003", "1", "county_not_published", "17031", "Item 3 blank: nullif before padding, or '' becomes the real code '000'.")]
    [InlineData("BD-0001", "1", "unmatched", "", "An exact vertex of the Loving ring — inside no county.")]
    [InlineData("OF-0001", "1", "unmatched", "", "Offshore: outside every polygon.")]
    [InlineData("PC-0001", "1", "unmatched", "", "Mid-Pacific: nothing within the search radius.")]
    public void Each_outcome_lands_on_the_structure_the_fixture_placed_there(
        string structureNumber, string recordType, string kind, string countyFips, string why)
    {
        var actual = DuckDb.Run(
            $"""
            SELECT kind, coalesce(county_fips, '')
            FROM cj_outcome
            WHERE structure_number = '{structureNumber}' AND record_type = '{recordType}';
            """,
            _init);

        Assert.Equal($"{kind}|{countyFips}", actual);
        Assert.NotEmpty(why);
    }

    /// <summary>
    /// The two miss reasons are distinguishable facts about the geometry, not severity labels: a
    /// boundary miss touches a polygon and is zero metres from it, an outside miss touches none. The
    /// third row pins the honest null — the job stopped looking rather than measuring a distance.
    /// </summary>
    [CountyJoinTheory]
    [InlineData("BD-0001", "on_county_boundary", "1", "48301", "0")]
    [InlineData("OF-0001", "outside_all_county_polygons", "0", "12086", "9543")]
    [InlineData("PC-0001", "outside_all_county_polygons", "0", "", "")]
    public void Every_miss_is_quarantined_with_its_reason_and_evidence(
        string structureNumber, string reason, string touching, string nearestFips, string nearestMetres)
    {
        var actual = DuckDb.Run(
            $"""
            SELECT reason, touching_counties, coalesce(nearest_county_fips, ''),
                   coalesce(CAST(nearest_distance_m AS VARCHAR), '')
            FROM cj_miss WHERE structure_number = '{structureNumber}';
            """,
            _init);

        Assert.Equal($"{reason}|{touching}|{nearestFips}|{nearestMetres}", actual);
    }

    /// <summary>
    /// The theory above names individual misses; without this a miss that stopped being produced at
    /// all would leave every remaining assertion passing.
    /// </summary>
    [CountyJoinFact]
    public void The_miss_theory_names_every_miss_the_job_produces()
    {
        Assert.Equal("3", DuckDb.Run("SELECT count(*) FROM cj_miss;", _init));
    }

    /// <summary>
    /// A county with a boundary and no ACS row keeps a null population. This is the row that must
    /// never become a zero: a per-capita figure computed against zero is a division by nothing, and a
    /// report card reading "population 0" states something the Census did not publish (GR-6).
    /// <para>
    /// The same county also has no structure assigned to it, so one row carries both absences a
    /// report card has to render — see the fixture README for why that mirrors the real data.
    /// </para>
    /// </summary>
    [CountyJoinFact]
    public void A_county_without_an_acs_row_keeps_a_null_population()
    {
        var actual = DuckDb.Run(
            """
            SELECT county_fips, name_lsad,
                   coalesce(CAST(population AS VARCHAR), 'null'),
                   coalesce(CAST(acs_vintage AS VARCHAR), 'null'),
                   (SELECT count(*) FROM cj_assignment WHERE county_fips = '60010')
            FROM cj_county_out WHERE county_fips = '60010';
            """,
            _init);

        Assert.Equal("60010|Eastern District|null|null|0", actual);
    }

    /// <summary>
    /// Loving County is the one fixture county whose margin of error survives the Census jam-value
    /// filter, so it pins that a published MOE travels with its estimate (GR-6: an estimate shown
    /// without its margin is still an estimate, and the margin is published where it exists).
    /// </summary>
    [CountyJoinFact]
    public void A_published_margin_of_error_travels_with_its_estimate()
    {
        Assert.Equal(
            "48301|33|30|2024|2020-2024",
            DuckDb.Run(
                """
                SELECT county_fips, population, population_moe, acs_vintage, acs_period
                FROM cj_county_out WHERE county_fips = '48301';
                """,
                _init));
    }

    /// <summary>
    /// Every county in the source is published, including one no structure was placed in. A report
    /// card asked for an empty county has to be able to say "no structures published here" with a
    /// name attached, and omitting the row would make that indistinguishable from a bad request.
    /// </summary>
    [CountyJoinFact]
    public void Every_source_county_is_published_even_with_no_structures_in_it()
    {
        Assert.Equal("6|1", DuckDb.Run(
            """
            SELECT (SELECT count(*) FROM cj_county_out),
                   (SELECT count(*) FROM cj_county_out c
                    WHERE NOT EXISTS (SELECT 1 FROM cj_assignment a WHERE a.county_fips = c.county_fips));
            """,
            _init));
    }

    /// <summary>
    /// The disagreement relation is keyed on the pair, and the boolean that explains most real-world
    /// disagreement is null — not false — where no code was published, because the question does not
    /// apply to a row whose whole point is that item 3 said nothing.
    /// </summary>
    [CountyJoinFact]
    public void Disagreements_are_pairs_and_the_retired_code_flag_is_null_when_no_code_was_published()
    {
        Assert.Equal(
            """
            |17031|county_not_published|1|null
            12086|06037|different_state|1|true
            48389|48301|different_county_same_state|1|false
            """.ReplaceLineEndings("\n"),
            DuckDb.Run(
                """
                SELECT coalesce(nbi_county_fips, ''), county_fips, kind, bridges,
                       coalesce(CAST(nbi_fips_in_tiger AS VARCHAR), 'null')
                FROM cj_disagreement ORDER BY nbi_county_fips NULLS FIRST, county_fips;
                """,
                _init));
    }

    /// <summary>
    /// Every self-check the job runs, over the fixture, asserted to return no rows — the same list
    /// <c>join-counties.sh</c> refuses to write output while any of them is non-empty.
    /// </summary>
    public static TheoryData<string> Invariants =>
    [
        "cj_check_assignment_unique",
        "cj_check_reconciles",
        "cj_check_coverage_totals",
        "cj_check_sign_convention",
        "cj_check_overlong_code",
        "cj_check_county_key",
        "cj_check_miss_reason",
        "cj_check_disagreement_resolves",
    ];

    [CountyJoinTheory]
    [MemberData(nameof(Invariants))]
    public void Invariant_holds_over_the_fixture(string view)
    {
        Assert.Equal("0", DuckDb.Run($"SELECT count(*) FROM {view};", _init));
    }

    /// <summary>
    /// An invariant that cannot fail is not an invariant. Each case redefines a relation so the named
    /// check must fire; <c>det_check_span</c> shipped once in FR-1.3 structurally incapable of
    /// returning a row, and this is the guard that would have caught it.
    /// <para>
    /// Every mutation is written against the relation <em>underneath</em> the one it replaces, never
    /// against the view itself: DuckDB rejects <c>CREATE OR REPLACE VIEW v AS SELECT … FROM v</c>
    /// outright ("infinite recursion detected"), so a self-referential mutation would fail the test
    /// with a binder error that looks exactly like a check that fired.
    /// </para>
    /// </summary>
    [CountyJoinTheory]
    // Assign every bridge to every county: a structure now has six assignments, not one.
    [InlineData("cj_check_assignment_unique",
        "CREATE OR REPLACE VIEW cj_assignment AS SELECT b.state_code, b.structure_number, b.record_type, " +
        "b.nbi_county_fips, c.county_fips, c.state_fips FROM cj_bridge b, cj_county c;")]
    // Drop the misses: assigned + quarantined no longer accounts for every input row.
    [InlineData("cj_check_reconciles",
        "CREATE OR REPLACE VIEW cj_miss AS SELECT u.state_code, u.structure_number, u.record_type, " +
        "u.nbi_county_fips, 'outside_all_county_polygons' AS reason, 0 AS touching_counties, " +
        "NULL AS nearest_county_fips, NULL AS nearest_distance_m, u.lon, u.lat " +
        "FROM cj_unmatched u WHERE false;")]
    // Overstate the denominator: the coverage row stops matching the relations it summarises.
    [InlineData("cj_check_coverage_totals",
        "CREATE OR REPLACE VIEW cj_coverage AS SELECT CAST(999 AS BIGINT) AS bridges, " +
        "CAST(0 AS BIGINT) AS matched, CAST(0 AS BIGINT) AS unmatched, CAST(0 AS BIGINT) AS structures, " +
        "CAST(0 AS BIGINT) AS structures_matched, CAST(0 AS BIGINT) AS agree, " +
        "CAST(0 AS BIGINT) AS different_county_same_state, CAST(0 AS BIGINT) AS different_state, " +
        "CAST(0 AS BIGINT) AS county_not_published;")]
    // Mirror every longitude into the eastern hemisphere — the west-positive regression this guard
    // exists for. Nothing then matches any polygon, so the check has to fire on "matched nothing at
    // all" rather than on a share it can no longer compute; that disjunct is the whole point of it.
    [InlineData("cj_check_sign_convention",
        "CREATE OR REPLACE TABLE bridge_source AS SELECT CAST(state_code AS VARCHAR) AS state_code, " +
        "CAST(structure_number AS VARCHAR) AS structure_number, CAST(record_type AS VARCHAR) AS record_type, " +
        "CAST(county_code AS VARCHAR) AS county_code, -CAST(lon AS DOUBLE) AS lon, CAST(lat AS DOUBLE) AS lat " +
        "FROM read_csv('src/tests/fixtures/census-join/bridge_points.csv', all_varchar=true);")]
    // A county code longer than its field: DuckDB's lpad truncates, so cj_bridge nulls it instead of
    // joining the wrong county — safe, but it must never be silent.
    [InlineData("cj_check_overlong_code",
        "CREATE OR REPLACE TABLE bridge_source AS SELECT CAST(state_code AS VARCHAR) AS state_code, " +
        "CAST(structure_number AS VARCHAR) AS structure_number, CAST(record_type AS VARCHAR) AS record_type, " +
        "'0210' AS county_code, CAST(lon AS DOUBLE) AS lon, CAST(lat AS DOUBLE) AS lat " +
        "FROM read_csv('src/tests/fixtures/census-join/bridge_points.csv', all_varchar=true);")]
    // A county key that is no longer its own state FIPS plus three digits.
    [InlineData("cj_check_county_key",
        "CREATE OR REPLACE VIEW cj_county AS SELECT '9' || substr(c.geoid, 2) AS county_fips, " +
        "c.statefp AS state_fips, c.name AS name, c.namelsad AS name_lsad, c.aland AS land_area_m2, " +
        "c.awater AS water_area_m2, c.tiger_vintage AS tiger_vintage, CAST(NULL AS BIGINT) AS population, " +
        "CAST(NULL AS BIGINT) AS population_moe, CAST(NULL AS SMALLINT) AS acs_vintage, " +
        "CAST(NULL AS VARCHAR) AS acs_period, CAST(NULL AS VARCHAR) AS acs_table, c.geom AS geom " +
        "FROM county_source c;")]
    // A boundary reason on a structure that touches nothing.
    [InlineData("cj_check_miss_reason",
        "CREATE OR REPLACE VIEW cj_miss AS SELECT u.state_code, u.structure_number, u.record_type, " +
        "u.nbi_county_fips, 'on_county_boundary' AS reason, 0 AS touching_counties, " +
        "NULL AS nearest_county_fips, NULL AS nearest_distance_m, u.lon, u.lat FROM cj_unmatched u;")]
    // A disagreement naming a polygon the county relation does not carry.
    [InlineData("cj_check_disagreement_resolves",
        "CREATE OR REPLACE VIEW cj_disagreement AS SELECT x.nbi_county_fips, '99999' AS county_fips, " +
        "x.kind, CAST(count(*) AS INTEGER) AS bridges, CAST(NULL AS BOOLEAN) AS nbi_fips_in_tiger " +
        "FROM cj_outcome x WHERE x.kind NOT IN ('agree', 'unmatched') GROUP BY 1, 2, 3;")]
    public void Invariant_fires_when_the_property_it_guards_is_broken(string view, string mutation)
    {
        var violations = int.Parse(DuckDb.Run($"{mutation} SELECT count(*) FROM {view};", _init));
        Assert.True(violations > 0, $"{view} returned no rows after a mutation that breaks it — it cannot fail.");
    }

    /// <summary>
    /// The containment predicate is a published constant that the manifest carries and the QA page
    /// states, so changing it has to be a deliberate edit in two places rather than a silent one.
    /// </summary>
    [CountyJoinFact]
    public void The_published_method_constants_are_what_the_job_reports()
    {
        Assert.Equal("ST_Within|2.0|0.90|v1.0", DuckDb.Run(
            """
            SELECT containment_predicate, nearest_search_degrees, min_state_agreement_share, method_version
            FROM cj_method;
            """,
            _init));
    }

    /// <summary>
    /// The choice ST_Within encodes, measured rather than asserted: the boundary point is matched by
    /// ST_Intersects and by nothing else. Switching the predicate would silently recover it — and
    /// nationally would assign two structures to one of the two counties sharing their line, which is
    /// a geography SpanSight would have invented.
    /// </summary>
    [CountyJoinFact]
    public void The_boundary_case_is_exactly_what_the_predicate_choice_excludes()
    {
        Assert.Equal("0|1", DuckDb.Run(
            """
            SELECT (SELECT count(*) FROM cj_county c, cj_bridge b
                    WHERE b.structure_number = 'BD-0001' AND ST_Within(b.point, c.geom)),
                   (SELECT count(*) FROM cj_county c, cj_bridge b
                    WHERE b.structure_number = 'BD-0001' AND ST_Intersects(b.point, c.geom));
            """,
            _init));
    }
}
