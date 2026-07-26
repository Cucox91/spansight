using SpanSight.Api.Endpoints;
using SpanSight.Core.Analytics;

namespace SpanSight.Api.Tests;

/// <summary>
/// Trends request validation (FR-1.2 AC-2), without a database. Every rejection names the field it
/// is about, so a caller gets a ProblemDetails that says what to fix.
/// </summary>
public class TrendQueryValidationTests
{
    [Fact]
    public void A_state_resolves_from_fips_or_usps_to_the_same_query()
    {
        Assert.True(TrendEndpoints.TryBuildQuery("state", "12", null, null, out var byFips, out _));
        Assert.True(TrendEndpoints.TryBuildQuery("state", "fl", null, null, out var byUsps, out _));

        Assert.Equal("12", byFips.Fips);
        Assert.Equal("12", byUsps.Fips);
        Assert.Equal("Florida", byUsps.Name);
        Assert.Equal(RollupLevel.State, byUsps.Level);
    }

    [Fact]
    public void A_county_keeps_its_five_digit_key_and_names_its_state()
    {
        Assert.True(TrendEndpoints.TryBuildQuery("county", "12086", null, null, out var query, out _));

        Assert.Equal(RollupLevel.County, query.Level);
        Assert.Equal("12086", query.Fips);
        Assert.Contains("086", query.Name);
        Assert.Contains("Florida", query.Name);
    }

    [Fact]
    public void Missing_years_span_the_whole_published_range()
    {
        Assert.True(TrendEndpoints.TryBuildQuery("state", "12", null, null, out var query, out _));

        Assert.Equal(TrendEndpoints.MinYear, query.FromYear);
        Assert.Equal(TrendEndpoints.MaxYear, query.ToYear);
    }

    [Theory]
    [InlineData(null, "12", "level")]
    [InlineData("", "12", "level")]
    [InlineData("region", "12", "level")]
    [InlineData("state", null, "fips")]
    [InlineData("state", "  ", "fips")]
    [InlineData("state", "ZZ", "fips")]
    [InlineData("state", "99", "fips")]
    [InlineData("county", "12", "fips")]          // too short
    [InlineData("county", "120860", "fips")]      // too long
    [InlineData("county", "1208X", "fips")]       // not digits
    [InlineData("county", "99001", "fips")]       // no such state
    public void Bad_level_or_fips_is_rejected_by_name(string? level, string? fips, string field)
    {
        Assert.False(TrendEndpoints.TryBuildQuery(level, fips, null, null, out _, out var errors));
        Assert.True(errors.ContainsKey(field));
    }

    [Theory]
    [InlineData(1900, null, "fromYear")]
    [InlineData(null, 1900, "toYear")]
    [InlineData(2200, null, "fromYear")]
    public void Years_outside_the_published_range_are_rejected(int? from, int? to, string field)
    {
        Assert.False(TrendEndpoints.TryBuildQuery("state", "12", from, to, out _, out var errors));
        Assert.True(errors.ContainsKey(field));
    }

    [Fact]
    public void An_inverted_year_range_is_rejected_rather_than_silently_returning_nothing()
    {
        Assert.False(TrendEndpoints.TryBuildQuery("state", "12", 2025, 2000, out _, out var errors));

        Assert.Contains("fromYear", errors.Keys);
        Assert.Contains("2025", errors["fromYear"][0]);
    }

    [Fact]
    public void A_valid_range_is_carried_through_unchanged()
    {
        Assert.True(TrendEndpoints.TryBuildQuery("state", "12", 2010, 2020, out var query, out _));

        Assert.Equal(2010, query.FromYear);
        Assert.Equal(2020, query.ToYear);
    }

    [Fact]
    public void The_method_note_states_the_rule_and_the_gr6_framing()
    {
        // The string is user-facing and the phase guardrail turns on it, so it is asserted rather
        // than trusted to survive editing.
        Assert.Contains("items 58", TrendEndpoints.MethodNote);
        Assert.Contains("Good 7–9", TrendEndpoints.MethodNote);
        Assert.Contains("never estimated", TrendEndpoints.MethodNote);
        Assert.Contains("not a prediction", TrendEndpoints.MethodNote);
        Assert.Contains("not engineering advice", TrendEndpoints.MethodNote);
    }
}
