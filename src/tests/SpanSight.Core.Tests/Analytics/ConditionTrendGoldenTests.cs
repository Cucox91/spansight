using SpanSight.Core.Analytics;
using SpanSight.Core.Domain;

namespace SpanSight.Core.Tests.Analytics;

/// <summary>
/// FR-1.2 AC-1/AC-3 — golden tests over the committed era fixtures.
/// <para>
/// The condition rule exists twice: once in <see cref="ConditionClassifier"/>, which the API and
/// the map use, and once in <c>tools/trends/trends.sql</c>, which produces the published history
/// offline. Two implementations of one rule is a standing drift risk, so the first test here runs
/// the real SQL over the fixture Parquet and asserts it agrees with the C# on every row. The rest
/// pin specific hand-checked bridges and the rollup reconciliation.
/// </para>
/// <para>
/// These need the DuckDB CLI and the fixture Parquet (<c>tools/vintages/convert.sh --fixtures</c>);
/// they skip where either is missing rather than failing a machine that has neither.
/// </para>
/// </summary>
public class ConditionTrendGoldenTests
{
    /// <summary>
    /// The load-bearing test of AC-1. For every fixture row the SQL keeps, compare the class it
    /// computed against the class the Phase 0 classifier computes from the same four raw published
    /// values. Any divergence — a threshold edited on one side, a stray TRY_CAST accepting '10',
    /// culvert handling changed — fails here with the offending row.
    /// </summary>
    [DuckDbFact]
    public void The_sql_classification_matches_the_core_classifier_on_every_fixture_row()
    {
        var init = DuckDb.CreateFixtureInitScript();
        try
        {
            // The raw items as published, alongside what the SQL made of them.
            var rows = DuckDb.Run(
                """
                SELECT n.DECK_COND_058, n.SUPERSTRUCTURE_COND_059, n.SUBSTRUCTURE_COND_060,
                       n.CULVERT_COND_062, coalesce(t.lowest_rating::VARCHAR, ''), t.condition_class
                FROM trend_structure_year t
                JOIN nbi_source n
                  ON  n.VINTAGE_YEAR = t.vintage_year
                  AND n.STATE_CODE_001 = t.state_code
                  AND n.STRUCTURE_NUMBER_008 = t.structure_number
                  AND n.RECORD_TYPE_005A = '1'
                """, init).Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.NotEmpty(rows);

            var compared = 0;
            foreach (var row in rows)
            {
                var f = row.Split('|');
                var (deck, super, sub, culvert) = (f[0], f[1], f[2], f[3]);

                var expectedRating = ConditionClassifier.LowestRating(deck, super, sub, culvert);
                var expectedClass = ConditionClassifier.Classify(expectedRating);

                Assert.Equal(expectedRating?.ToString() ?? string.Empty, f[4]);
                Assert.Equal(expectedClass.ToString(), f[5]);
                compared++;
            }

            // 300 rows per fixture vintage, five vintages — all of them, not a sample.
            Assert.Equal(1500, compared);
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// AC-3 — four structures verified by reading the fixture files, chosen because each pins a
    /// different rule rather than four variations of the same one. Expected values are stated
    /// literally; the assertion is exact equality.
    /// </summary>
    public static TheoryData<string, string, int, int, int, string> HandCheckedBridges => new()
    {
        // 000002: deck falls 8 → 7 → 7 → 6, but superstructure sits at 4 in every vintage. The
        // series must follow the *lowest* component, so it reads 4 throughout and never moves off
        // Poor — the case that catches a "use the deck rating" regression.
        { "01", "000002", 2010, 2025, 4, "4.....44.......4" },

        // 000005: 5/5/5 in 2010 then 5/4/4 from 2016 — a genuine Fair → Poor transition, so a
        // series that never changes class would be wrong here.
        { "01", "000005", 2010, 2025, 4, "5.....44.......4" },

        // 000019: a culvert. Items 58/59/60 are all 'N' and item 62 governs at 5 — the FHWA
        // convention that a naive min() over four columns gets wrong by treating 'N' as zero.
        { "01", "000019", 2010, 2025, 4, "5.....55.......5" },

        // 0000000011070Z0: published only in 1992, with 'N' in all four items. One observation,
        // no numeric rating — an Unknown year, not a gap and not a Poor.
        { "01", "0000000011070Z0", 1992, 1992, 1, "U" },
    };

    [DuckDbTheory]
    [MemberData(nameof(HandCheckedBridges))]
    public void Hand_checked_bridges_produce_exactly_the_expected_series(
        string stateCode, string structureNumber, int firstYear, int lastYear, int observedYears, string ratings)
    {
        var init = DuckDb.CreateFixtureInitScript();
        try
        {
            var row = DuckDb.Run(
                $"""
                 SELECT first_year, last_year, observed_years, ratings
                 FROM trend_bridge_series
                 WHERE state_code = '{stateCode}' AND structure_number = '{structureNumber}'
                 """, init);

            Assert.Equal($"{firstYear}|{lastYear}|{observedYears}|{ratings}", row);

            // The same series, read the way the API reads it: the two must describe one history.
            var observations = ConditionSeriesCodec.Decode(firstYear, ratings);
            Assert.Equal(observedYears, observations.Count);
            Assert.Equal(firstYear, observations[0].Year);
            Assert.Equal(lastYear, observations[^1].Year);
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// The 000002 case spelled out year by year, so the expectation is legible as a history rather
    /// than as a string literal.
    /// </summary>
    [DuckDbFact]
    public void The_governing_component_case_decodes_to_the_history_it_claims()
    {
        var observations = ConditionSeriesCodec.Decode(2010, "4.....44.......4");

        Assert.Collection(observations,
            o => Assert.Equal((2010, 4, ConditionClass.Poor), (o.Year, o.LowestRating, o.ConditionClass)),
            o => Assert.Equal((2016, 4, ConditionClass.Poor), (o.Year, o.LowestRating, o.ConditionClass)),
            o => Assert.Equal((2017, 4, ConditionClass.Poor), (o.Year, o.LowestRating, o.ConditionClass)),
            o => Assert.Equal((2025, 4, ConditionClass.Poor), (o.Year, o.LowestRating, o.ConditionClass)));
    }

    /// <summary>
    /// AC-3 — the rollups must summarise exactly the rows behind the per-bridge series, with no
    /// row counted twice and none dropped. Each of these views returns the violating rows, so a
    /// failure names the year and area rather than just disagreeing.
    /// </summary>
    [DuckDbTheory]
    [InlineData("trend_check_state_totals")]
    [InlineData("trend_check_class_partition")]
    [InlineData("trend_check_county_within_state")]
    [InlineData("trend_check_series_shape")]
    [InlineData("trend_check_series_vs_rollup")]
    public void Rollups_reconcile_exactly_with_the_per_bridge_data(string check)
    {
        var init = DuckDb.CreateFixtureInitScript();
        try
        {
            Assert.Equal("0", DuckDb.Run($"SELECT count(*) FROM {check};", init));
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// Reconciliation stated as a total rather than as an invariant: every published observation in
    /// the fixtures appears once in the series and once in the state rollups.
    /// </summary>
    [DuckDbFact]
    public void Every_kept_observation_is_counted_once_in_each_output()
    {
        var init = DuckDb.CreateFixtureInitScript();
        try
        {
            var totals = DuckDb.Run(
                """
                SELECT
                  (SELECT count(*) FROM trend_structure_year),
                  (SELECT sum(observed_years) FROM trend_bridge_series),
                  (SELECT sum(total) FROM trend_rollup WHERE level = 'state')
                """, init).Split('|');

            Assert.Equal(totals[0], totals[1]);
            Assert.Equal(totals[0], totals[2]);
            // Five fixture vintages of 300 rows, all record type '1' and none colliding.
            Assert.Equal("1500", totals[0]);
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// The dedup rule, asserted directly (see the header comment in <c>trends.sql</c>): one row per
    /// structure per year, and nothing that is not a route-on-structure record.
    /// </summary>
    [DuckDbFact]
    public void One_row_survives_per_structure_per_year()
    {
        var init = DuckDb.CreateFixtureInitScript();
        try
        {
            Assert.Equal("0", DuckDb.Run(
                """
                SELECT count(*) FROM (
                  SELECT vintage_year, state_code, structure_number
                  FROM trend_structure_year GROUP BY 1, 2, 3 HAVING count(*) > 1)
                """, init));
        }
        finally
        {
            File.Delete(init);
        }
    }
}
