using SpanSight.Api.Endpoints;

namespace SpanSight.Api.Tests;

/// <summary>
/// FR-1.4 — the request rules for <c>/api/rankings</c> and <c>/api/counties/{fips}</c>, exercised
/// without a database. The DB-backed half repeats the same table over HTTP.
/// </summary>
public class RankingQueryValidationTests
{
    // ------------------------------------------------------------------ rankings

    [Fact]
    public void An_empty_request_is_the_national_worst_condition_ranking_by_county()
    {
        Assert.True(RankingEndpoints.TryBuildQuery(null, null, null, null, out var query, out _));

        Assert.Equal(RankingEndpoints.RankingView.WorstCondition, query.View);
        Assert.Equal(RankingEndpoints.RankingGroupBy.County, query.GroupBy);
        Assert.Null(query.StateFips);
        Assert.Equal(RankingEndpoints.DefaultLimit, query.Limit);
    }

    [Theory]
    [InlineData("worst-condition", RankingEndpoints.RankingView.WorstCondition)]
    [InlineData("WORST-CONDITION", RankingEndpoints.RankingView.WorstCondition)]
    [InlineData("high-adt-poor", RankingEndpoints.RankingView.HighAdtPoor)]
    [InlineData(" high-adt-poor ", RankingEndpoints.RankingView.HighAdtPoor)]
    public void A_known_view_is_accepted_case_and_whitespace_insensitively(
        string view, RankingEndpoints.RankingView expected)
    {
        Assert.True(RankingEndpoints.TryBuildQuery(view, null, null, null, out var query, out _));
        Assert.Equal(expected, query.View);
    }

    [Fact]
    public void An_unknown_view_names_the_field_and_lists_what_is_allowed()
    {
        Assert.False(RankingEndpoints.TryBuildQuery("worst", null, null, null, out _, out var errors));

        Assert.Contains("view", errors.Keys);
        Assert.Contains("worst-condition", errors["view"][0]);
        Assert.Contains("high-adt-poor", errors["view"][0]);
    }

    [Theory]
    [InlineData("state", RankingEndpoints.RankingGroupBy.State)]
    [InlineData("county", RankingEndpoints.RankingGroupBy.County)]
    [InlineData("cohort", RankingEndpoints.RankingGroupBy.Cohort)]
    public void Each_grouping_is_accepted(string groupBy, RankingEndpoints.RankingGroupBy expected)
    {
        Assert.True(RankingEndpoints.TryBuildQuery(null, groupBy, null, null, out var query, out _));
        Assert.Equal(expected, query.GroupBy);
    }

    [Fact]
    public void An_unknown_grouping_is_rejected()
    {
        Assert.False(RankingEndpoints.TryBuildQuery(null, "region", null, null, out _, out var errors));
        Assert.Contains("groupBy", errors.Keys);
    }

    /// <summary>
    /// A caller who asked to group a structure-level list has misunderstood the response they are
    /// about to get. Rejecting says so; silently dropping the parameter is how that misunderstanding
    /// survives all the way to a chart someone screenshots.
    /// </summary>
    [Fact]
    public void Grouping_a_structure_level_view_is_rejected_rather_than_ignored()
    {
        Assert.False(
            RankingEndpoints.TryBuildQuery("high-adt-poor", "county", null, null, out _, out var errors));

        Assert.Contains("groupBy", errors.Keys);
        Assert.Contains("does not apply", errors["groupBy"][0]);
    }

    [Fact]
    public void The_structure_level_view_is_fine_without_a_grouping()
    {
        Assert.True(RankingEndpoints.TryBuildQuery("high-adt-poor", null, null, null, out var query, out _));
        Assert.Equal(RankingEndpoints.RankingView.HighAdtPoor, query.View);
    }

    /// <summary>A USPS code is accepted as readily as a FIPS — the courtesy /bridges and /trends extend.</summary>
    [Theory]
    [InlineData("FL")]
    [InlineData("fl")]
    [InlineData("12")]
    public void A_state_scope_resolves_from_either_spelling(string state)
    {
        Assert.True(RankingEndpoints.TryBuildQuery(null, null, state, null, out var query, out _));

        Assert.Equal("12", query.StateFips);
        Assert.Equal("FL", query.StateAbbreviation);
        Assert.Equal("Florida", query.StateName);
    }

    [Fact]
    public void An_unknown_state_is_rejected()
    {
        Assert.False(RankingEndpoints.TryBuildQuery(null, null, "ZZ", null, out _, out var errors));
        Assert.Contains("state", errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RankingEndpoints.MaxLimit + 1)]
    public void A_limit_outside_the_published_bounds_is_rejected(int limit)
    {
        Assert.False(RankingEndpoints.TryBuildQuery(null, null, null, limit, out _, out var errors));
        Assert.Contains("limit", errors.Keys);
    }

    [Fact]
    public void The_maximum_limit_itself_is_allowed()
    {
        Assert.True(
            RankingEndpoints.TryBuildQuery(null, null, null, RankingEndpoints.MaxLimit, out var query, out _));
        Assert.Equal(RankingEndpoints.MaxLimit, query.Limit);
    }

    /// <summary>
    /// The floor is the one FR-1.3's methodology publishes. One number across the product is one
    /// thing to explain and one thing to change; two would eventually disagree.
    /// </summary>
    [Fact]
    public void The_minimum_group_size_is_the_same_floor_the_methodology_publishes()
    {
        Assert.Equal(50, RankingEndpoints.MinimumGroupSize);
    }

    /// <summary>
    /// GR-6: the note that travels with every ranking says what it is and denies what it is not.
    /// "prediction"-adjacent vocabulary is permitted only inside the clause that rules it out.
    /// </summary>
    [Fact]
    public void The_ranking_note_denies_being_a_priority_list_or_engineering_advice()
    {
        Assert.Contains("not a priority list", RankingEndpoints.Note);
        Assert.Contains("not engineering advice", RankingEndpoints.Note);
        Assert.Contains("record type 1", RankingEndpoints.RecordTypeNote);
    }

    // ------------------------------------------------------------------ county fips

    [Theory]
    [InlineData("12086", "12", "FL")]
    [InlineData(" 12086 ", "12", "FL")]
    [InlineData("06037", "06", "CA")]
    public void A_well_formed_county_fips_resolves_its_state(string fips, string stateFips, string abbreviation)
    {
        Assert.True(CountyEndpoints.TryParseFips(fips, out var query, out _));

        Assert.Equal(fips.Trim(), query.Fips);
        Assert.Equal(stateFips, query.StateFips);
        Assert.Equal(abbreviation, query.StateAbbreviation);
    }

    [Theory]
    [InlineData(null, "no fips at all")]
    [InlineData("", "empty")]
    [InlineData("1208", "four digits")]
    [InlineData("120866", "six digits")]
    [InlineData("1208a", "not all digits")]
    [InlineData("12-86", "punctuation")]
    public void A_malformed_county_fips_is_rejected_with_the_shape_it_expected(string? fips, string why)
    {
        Assert.False(CountyEndpoints.TryParseFips(fips, out _, out var errors));

        Assert.Contains("fips", errors.Keys);
        Assert.Contains("5 digits", errors["fips"][0]);
        Assert.NotEmpty(why);
    }

    [Fact]
    public void A_five_digit_code_whose_state_half_is_not_a_state_is_rejected()
    {
        Assert.False(CountyEndpoints.TryParseFips("99001", out _, out var errors));

        Assert.Contains("fips", errors.Keys);
        Assert.Contains("99", errors["fips"][0]);
    }

    /// <summary>
    /// Connecticut's legacy codes must parse. Item 3 publishes them in every vintage through 2025,
    /// so rejecting them would drop 4,362 structures out of the report card entirely — the fallback
    /// label is what handles the missing Census name, not a validation failure.
    /// </summary>
    [Fact]
    public void A_county_code_the_census_retired_still_parses_and_gets_a_label()
    {
        Assert.True(CountyEndpoints.TryParseFips("09003", out var query, out _));

        Assert.Equal("09003", query.Fips);
        Assert.Equal("County FIPS 003, Connecticut", query.FallbackName);
    }

    /// <summary>The two county surfaces reject the same inputs the same way, by construction.</summary>
    [Theory]
    [InlineData("1208")]
    [InlineData("99001")]
    public void The_report_card_and_the_trend_series_reject_a_bad_county_code_identically(string fips)
    {
        Assert.False(CountyEndpoints.TryParseFips(fips, out _, out var cardErrors));
        Assert.False(TrendEndpoints.TryBuildQuery("county", fips, null, null, out _, out var trendErrors));

        Assert.Equal(trendErrors["fips"][0], cardErrors["fips"][0]);
    }
}
