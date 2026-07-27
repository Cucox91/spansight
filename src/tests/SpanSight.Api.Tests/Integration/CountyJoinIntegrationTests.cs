using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Analytics;

namespace SpanSight.Api.Tests.Integration;

/// <summary>
/// FR-1.5 AC-2/AC-3 against real PostGIS: the county-join loader publishes the committed fixture,
/// then the API serves the coverage metric on the QA surface.
/// <para>
/// Expectations come from <c>src/tests/fixtures/census-join-aggregates/README.md</c>, which states
/// them by hand — nothing here is compared against the DuckDB job that produced the fixture. The job
/// is golden-tested separately, so a bug in either half cannot make both agree.
/// </para>
/// </summary>
[Collection("postgis-api")]
public class CountyJoinIntegrationTests(PostgisApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private async Task<JsonElement> CoverageAsync() =>
        (await GetJsonAsync("/api/qa/summary")).GetProperty("countyJoin");

    // ---------------------------------------------------------------- loader

    [DockerFact]
    public void The_seed_published_the_whole_fixture()
    {
        Assert.False(fixture.CountyJoinSeedSummary.Skipped);
        Assert.Equal(6, fixture.CountyJoinSeedSummary.Counties);
        Assert.Equal(3, fixture.CountyJoinSeedSummary.Misses);
        Assert.Equal(3, fixture.CountyJoinSeedSummary.Disagreements);
        Assert.Equal("county-join-fixture", fixture.CountyJoinSeedSummary.JobRunId);
    }

    [DockerFact]
    public async Task Reload_of_the_same_job_is_a_noop_and_force_republishes_without_duplicating()
    {
        var again = await fixture.LoadCountyJoinAsync(force: false);
        Assert.True(again.Skipped);

        var forced = await fixture.LoadCountyJoinAsync(force: true);
        Assert.False(forced.Skipped);
        Assert.Equal(6, forced.Counties);

        await using var db = fixture.NewDbContext();
        Assert.Equal(6, await db.CensusCounties.CountAsync());
        Assert.Equal(3, await db.CountyJoinMisses.CountAsync());

        // The disagreement key's first column is null for the county_not_published row. Postgres
        // treats nulls as distinct in a unique index by default, so without NULLS NOT DISTINCT this
        // re-publish would insert a second copy of that row instead of updating it — silently, and
        // only for the one kind whose whole point is that no code was published.
        Assert.Equal(3, await db.CountyJoinDisagreements.CountAsync());
        Assert.Equal(1, await db.CountyJoinDisagreements.CountAsync(d => d.NbiCountyFips == null));
    }

    // ---------------------------------------------------------------- coverage (AC-2)

    [DockerFact]
    public async Task Qa_publishes_the_join_coverage_with_both_denominators()
    {
        var coverage = await CoverageAsync();

        Assert.Equal(16, coverage.GetProperty("bridges").GetInt64());
        Assert.Equal(13, coverage.GetProperty("matched").GetInt64());
        Assert.Equal(3, coverage.GetProperty("unmatched").GetInt64());

        // Record type 1 is what "bridge" means in FR-1.2 and FR-1.3; the all-rows figure additionally
        // counts the routes published under a structure. Publishing one without the other would
        // answer a different question than AC-2 asks.
        Assert.Equal(15, coverage.GetProperty("structures").GetInt64());
        Assert.Equal(12, coverage.GetProperty("structuresMatched").GetInt64());

        Assert.Equal(6, coverage.GetProperty("counties").GetInt32());
        Assert.Equal(1, coverage.GetProperty("countiesWithoutPopulation").GetInt32());
    }

    /// <summary>
    /// Coverage is quoted to four decimals. The national figure is 99.9926%, and at two decimals it
    /// renders as 99.99% — but at zero decimals it would be "100%", which is the one thing a
    /// data-quality metric must never say while 55 structures are quarantined.
    /// </summary>
    [DockerFact]
    public async Task Coverage_percentages_are_computed_from_the_counts_they_describe()
    {
        var coverage = await CoverageAsync();

        Assert.Equal(Math.Round(100d * 13 / 16, 4), coverage.GetProperty("coveragePercent").GetDouble());
        Assert.Equal(Math.Round(100d * 12 / 15, 4), coverage.GetProperty("structureCoveragePercent").GetDouble());
        Assert.Equal(Math.Round(100d * 10 / 13, 4), coverage.GetProperty("agreePercent").GetDouble());
    }

    /// <summary>
    /// The four cross-check outcomes are exhaustive over what matched: anything else means a
    /// structure was classified into nothing, or twice.
    /// </summary>
    [DockerFact]
    public async Task The_cross_check_kinds_account_for_every_matched_structure()
    {
        var coverage = await CoverageAsync();

        var total = coverage.GetProperty("agree").GetInt64()
            + coverage.GetProperty("differentCountySameState").GetInt64()
            + coverage.GetProperty("differentState").GetInt64()
            + coverage.GetProperty("countyNotPublished").GetInt64();

        Assert.Equal(coverage.GetProperty("matched").GetInt64(), total);
    }

    /// <summary>AC-2: every miss is itemised with a reason, and the reasons account for all of them.</summary>
    [DockerFact]
    public async Task Every_miss_is_published_with_its_reason()
    {
        var coverage = await CoverageAsync();
        var reasons = coverage.GetProperty("missesByReason").EnumerateArray().ToList();

        Assert.Equal(
            coverage.GetProperty("unmatched").GetInt64(),
            reasons.Sum(r => (long)r.GetProperty("structures").GetInt32()));

        var boundary = reasons.Single(r => r.GetProperty("reason").GetString() == "on_county_boundary");
        Assert.Equal(1, boundary.GetProperty("structures").GetInt32());
        Assert.Equal(0, boundary.GetProperty("maxDistanceMeters").GetInt64());

        var outside = reasons.Single(r => r.GetProperty("reason").GetString() == "outside_all_county_polygons");
        Assert.Equal(2, outside.GetProperty("structures").GetInt32());
        // One of the two had no county inside the search radius, so the median of the measured
        // distances comes from the one that did — 9,543 m — rather than from an invented value.
        Assert.Equal(9543, outside.GetProperty("medianDistanceMeters").GetInt64());
    }

    /// <summary>
    /// The retired-code flag is the fact that explains most real disagreement, and it must reach the
    /// payload as three distinguishable states: true, false, and null where no code was published.
    /// </summary>
    [DockerFact]
    public async Task Disagreements_name_both_counties_and_flag_a_retired_published_code()
    {
        var coverage = await CoverageAsync();
        var pairs = coverage.GetProperty("largestDisagreements").EnumerateArray().ToList();

        Assert.Equal(3, pairs.Count);

        var retired = pairs.Single(p => p.GetProperty("nbiCountyFips").GetString() == "48389");
        Assert.False(retired.GetProperty("nbiFipsInTiger").GetBoolean());
        // The published side names a county the boundary file does not carry, so there is no Census
        // name for it — reported as absent rather than swallowing the row.
        Assert.Equal(JsonValueKind.Null, retired.GetProperty("nbiCountyName").ValueKind);
        Assert.Equal("Loving County", retired.GetProperty("countyName").GetString());

        var crossState = pairs.Single(p => p.GetProperty("kind").GetString() == "different_state");
        Assert.True(crossState.GetProperty("nbiFipsInTiger").GetBoolean());
        Assert.Equal("Miami-Dade County", crossState.GetProperty("nbiCountyName").GetString());
        Assert.Equal("Los Angeles County", crossState.GetProperty("countyName").GetString());

        var notPublished = pairs.Single(p => p.GetProperty("kind").GetString() == "county_not_published");
        Assert.Equal(JsonValueKind.Null, notPublished.GetProperty("nbiCountyFips").ValueKind);
        Assert.Equal(JsonValueKind.Null, notPublished.GetProperty("nbiFipsInTiger").ValueKind);
    }

    /// <summary>
    /// GR-6: the rule the coverage figure was measured under ships with it, so no view can render
    /// the number bare, and the response says plainly that a disagreement is not a correction.
    /// </summary>
    [DockerFact]
    public async Task The_method_note_and_provenance_travel_with_the_coverage()
    {
        var coverage = await CoverageAsync();

        var note = coverage.GetProperty("methodNote").GetString();
        Assert.Contains("ST_Within", note);
        Assert.Contains("not a correction", note);

        var provenance = coverage.GetProperty("provenance");
        Assert.Equal("county-join-fixture", provenance.GetProperty("jobRunId").GetString());
        Assert.Equal("ST_Within", provenance.GetProperty("containmentPredicate").GetString());
        Assert.Equal("v1.0", provenance.GetProperty("methodVersion").GetString());
    }

    // ---------------------------------------------------------------- names (AC-3)

    /// <summary>
    /// FR-1.2's county label was a synthesized string until this join published a real name. It has
    /// to degrade rather than disappear where the published code names a county TIGER retired —
    /// which is Connecticut, five thousand structures of it, on the national data.
    /// </summary>
    [DockerFact]
    public async Task A_county_trend_series_takes_the_census_name_and_falls_back_when_there_is_none()
    {
        var known = await GetJsonAsync("/api/trends?level=county&fips=12086");
        Assert.Equal("Miami-Dade County", known.GetProperty("name").GetString());

        // 12087 is a real Florida county the census fixture does not carry, standing in for the
        // retired-code case: the series must still answer, with a label that says what it knows.
        var unknown = await GetJsonAsync("/api/trends?level=county&fips=12087");
        Assert.Equal("County FIPS 087, Florida", unknown.GetProperty("name").GetString());
    }

    /// <summary>
    /// A county with a boundary and no ACS row keeps a null population all the way to the serving
    /// table. This is the row that must never become a zero (GR-6).
    /// </summary>
    [DockerFact]
    public async Task A_county_without_an_acs_row_is_stored_with_a_null_population()
    {
        await using var db = fixture.NewDbContext();

        var county = await db.CensusCounties.AsNoTracking()
            .SingleAsync(c => c.CountyFips == "60010");

        Assert.Equal("Eastern District", county.NameLsad);
        Assert.Null(county.Population);
        Assert.Null(county.AcsVintage);

        var loving = await db.CensusCounties.AsNoTracking().SingleAsync(c => c.CountyFips == "48301");
        Assert.Equal(33, loving.Population);
        Assert.Equal(30, loving.PopulationMoe);
        Assert.Equal((short)2024, loving.AcsVintage);
        Assert.Equal("2020-2024", loving.AcsPeriod);
    }

    /// <summary>
    /// The join writes nothing outside <c>analytics</c>. The county a bridge is reported in stays the
    /// county item 3 published, so a job whose entire output is a measurement of disagreement must
    /// not have edited the thing it measured (GR-6).
    /// </summary>
    [DockerFact]
    public async Task The_join_leaves_the_published_county_code_untouched()
    {
        await using var db = fixture.NewDbContext();

        var seeded = fixture.SeedSummary.RowsLoaded;
        Assert.Equal(seeded, await db.Bridges.CountAsync());

        // Every bridge still carries the county code ingestion loaded, and none carries a 5-digit
        // FIPS — which is what a join that "corrected" core would have written.
        Assert.Equal(0, await db.Bridges.CountAsync(b => b.CountyCode != null && b.CountyCode.Length > 3));
    }

    /// <summary>
    /// Provenance follows the rows being served, not the newest completed run. The collection fixture
    /// is shared and mutable, so this is not hypothetical: another test publishing and rolling back a
    /// different job would otherwise attach that job's coverage figures to these counties.
    /// </summary>
    [DockerFact]
    public async Task Provenance_follows_the_rows_being_served()
    {
        await using var db = fixture.NewDbContext();

        var servingRunId = await db.CensusCounties.AsNoTracking()
            .Select(c => c.CountyJoinRunId).Distinct().SingleAsync();
        var run = await db.CountyJoinRuns.AsNoTracking().SingleAsync(r => r.Id == servingRunId);

        Assert.Equal(CountyJoinRunStatus.Completed, run.Status);

        var coverage = await CoverageAsync();
        Assert.Equal(run.JobRunId, coverage.GetProperty("provenance").GetProperty("jobRunId").GetString());
    }
}
