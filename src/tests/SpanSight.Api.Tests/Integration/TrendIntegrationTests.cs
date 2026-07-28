using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Analytics;

namespace SpanSight.Api.Tests.Integration;

/// <summary>
/// FR-1.2 AC-2/AC-3 against real PostGIS: the trend loader publishes the committed fixture, then
/// the API serves it. Expectations come from <c>src/tests/fixtures/trends/README.md</c>, which was
/// written by hand — nothing here is compared against the job that produces real aggregates.
/// </summary>
[Collection("postgis-api")]
public class TrendIntegrationTests(PostgisApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    // ---------------------------------------------------------------- loader

    [DockerFact]
    public void Trend_seed_published_the_whole_fixture()
    {
        Assert.False(fixture.TrendSeedSummary.Skipped);
        Assert.Equal(3, fixture.TrendSeedSummary.SeriesRows);
        Assert.Equal(12, fixture.TrendSeedSummary.RollupRows);
        Assert.Equal("trends-fixture-0001", fixture.TrendSeedSummary.JobRunId);
    }

    [DockerFact]
    public async Task Reload_of_the_same_job_is_a_noop_and_force_converges()
    {
        var again = await fixture.LoadTrendsAsync(force: false);
        Assert.True(again.Skipped);

        var forced = await fixture.LoadTrendsAsync(force: true);
        Assert.False(forced.Skipped);

        await using var db = fixture.NewDbContext();
        // The upsert lands on the natural key, so a forced re-publish updates in place.
        Assert.Equal(3, await db.BridgeConditionSeries.CountAsync());
        Assert.Equal(12, await db.ConditionRollups.CountAsync());
    }

    [DockerFact]
    public async Task Every_published_row_is_stamped_with_the_run_that_wrote_it()
    {
        await using var db = fixture.NewDbContext();
        var run = await db.TrendRuns.OrderByDescending(r => r.Id).FirstAsync();

        Assert.Equal(TrendRunStatus.Completed, run.Status);
        Assert.Equal("trends-fixture-0001", run.JobRunId);
        Assert.NotNull(run.CompletedUtc);

        // NFR-3: nothing servable is left pointing at an older run.
        Assert.False(await db.BridgeConditionSeries.AnyAsync(s => s.TrendRunId != run.Id));
        Assert.False(await db.ConditionRollups.AnyAsync(r => r.TrendRunId != run.Id));
    }

    [DockerFact]
    public async Task A_shrinking_job_output_removes_the_rows_it_no_longer_contains()
    {
        // The convergence case a plain upsert cannot handle: a structure that disappears from the
        // job must disappear from the serving tables, not linger with a stale series.
        var staging = Path.Combine(Path.GetTempPath(), $"spansight-trend-shrink-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "bridge_series.csv"),
                "state_code,structure_number,first_year,last_year,observed_years,ratings\n" +
                "12,1213483000,2020,2025,6,776653\n");
            await File.WriteAllTextAsync(Path.Combine(staging, "rollup.csv"),
                "level,fips,vintage_year,total,good,fair,poor,unknown\n" +
                "state,12,2025,1,0,0,1,0\n");
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
                """
                {"runId":"trends-fixture-shrunk","catalogSha256":"deadbeef","firstYear":2020,
                 "lastYear":2025,"vintages":6,"bridges":1,"observations":6,"rollupRows":1}
                """);

            var shrunk = await fixture.LoadTrendsAsync(force: false, directory: staging);
            Assert.Equal(1, shrunk.SeriesRows);
            // 2 series + 11 rollup rows from the previous run had to go.
            Assert.Equal(13, shrunk.Superseded);

            await using (var db = fixture.NewDbContext())
            {
                Assert.Equal(1, await db.BridgeConditionSeries.CountAsync());
                Assert.Equal(1, await db.ConditionRollups.CountAsync());
            }
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            // Restore the shared fixture state for whatever runs next in this collection.
            await fixture.LoadTrendsAsync(force: true);
        }
    }

    [DockerFact]
    public async Task Re_publishing_a_job_whose_rows_were_superseded_restores_them()
    {
        // The trap a status-only idempotency check falls into: publish A, publish B (which sweeps
        // A's rows away), then publish A again. A is still marked Completed, but its data is gone —
        // so this must re-publish rather than report a no-op and leave the tables empty.
        var staging = Path.Combine(Path.GetTempPath(), $"spansight-trend-other-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "bridge_series.csv"),
                "state_code,structure_number,first_year,last_year,observed_years,ratings\n" +
                "12,1213483000,2020,2025,6,776653\n");
            await File.WriteAllTextAsync(Path.Combine(staging, "rollup.csv"),
                "level,fips,vintage_year,total,good,fair,poor,unknown\n" +
                "state,12,2025,1,0,0,1,0\n");
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
                """
                {"runId":"trends-fixture-other","catalogSha256":"deadbeef","firstYear":2020,
                 "lastYear":2025,"vintages":6,"bridges":1,"observations":6,"rollupRows":1}
                """);

            await fixture.LoadTrendsAsync(force: false, directory: staging);

            // Back to the original job id, without --force.
            var restored = await fixture.LoadTrendsAsync(force: false);

            Assert.False(restored.Skipped);
            Assert.Equal(3, restored.SeriesRows);
            Assert.Equal(12, restored.RollupRows);

            await using var db = fixture.NewDbContext();
            Assert.Equal(3, await db.BridgeConditionSeries.CountAsync());
            Assert.Equal(12, await db.ConditionRollups.CountAsync());
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            await fixture.LoadTrendsAsync(force: true);
        }
    }

    [DockerFact]
    public async Task A_manifest_that_disagrees_with_its_data_is_refused()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"spansight-trend-bad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "bridge_series.csv"),
                "state_code,structure_number,first_year,last_year,observed_years,ratings\n" +
                "12,1213483000,2020,2025,6,776653\n");
            await File.WriteAllTextAsync(Path.Combine(staging, "rollup.csv"),
                "level,fips,vintage_year,total,good,fair,poor,unknown\n" +
                "state,12,2025,1,0,0,1,0\n");
            // Claims 999 bridges; the CSV has one.
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
                """
                {"runId":"trends-fixture-mismatch","catalogSha256":"deadbeef","firstYear":2020,
                 "lastYear":2025,"vintages":6,"bridges":999,"observations":6,"rollupRows":1}
                """);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.LoadTrendsAsync(force: false, directory: staging));
            Assert.Contains("999", error.Message);

            await using var db = fixture.NewDbContext();
            var failed = await db.TrendRuns.FirstAsync(r => r.JobRunId == "trends-fixture-mismatch");
            Assert.Equal(TrendRunStatus.Failed, failed.Status);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            await fixture.LoadTrendsAsync(force: true);
        }
    }

    // ---------------------------------------------------------------- history endpoint

    [DockerFact]
    public async Task History_returns_the_published_years_and_omits_the_gap()
    {
        var history = await GetJsonAsync("/api/bridges/FL/1238705001/history");

        Assert.Equal(2020, history.GetProperty("firstYear").GetInt32());
        Assert.Equal(2025, history.GetProperty("lastYear").GetInt32());
        Assert.Equal(5, history.GetProperty("observedYears").GetInt32());

        var years = history.GetProperty("points").EnumerateArray()
            .Select(p => p.GetProperty("year").GetInt32()).ToArray();

        // 2022 is absent from the fixture and must not be invented (GR-6).
        Assert.Equal([2020, 2021, 2023, 2024, 2025], years);
    }

    [DockerFact]
    public async Task History_classes_follow_the_phase_0_thresholds()
    {
        var history = await GetJsonAsync("/api/bridges/FL/1213483000/history");

        var points = history.GetProperty("points").EnumerateArray()
            .Select(p => (p.GetProperty("year").GetInt32(),
                          p.GetProperty("lowestRating").GetInt32(),
                          p.GetProperty("conditionClass").GetString()))
            .ToArray();

        Assert.Equal(
        [
            (2020, 7, "Good"), (2021, 7, "Good"), (2022, 6, "Fair"),
            (2023, 6, "Fair"), (2024, 5, "Fair"), (2025, 3, "Poor"),
        ], points);
    }

    [DockerFact]
    public async Task An_unrated_year_is_returned_as_Unknown_with_no_rating()
    {
        var history = await GetJsonAsync("/api/bridges/FL/1254730002/history");

        var unrated = history.GetProperty("points").EnumerateArray()
            .Single(p => p.GetProperty("year").GetInt32() == 2024);

        Assert.Equal(JsonValueKind.Null, unrated.GetProperty("lowestRating").ValueKind);
        Assert.Equal("Unknown", unrated.GetProperty("conditionClass").GetString());
        // Still an observation: three published years across a three-year span.
        Assert.Equal(3, history.GetProperty("observedYears").GetInt32());
    }

    [DockerFact]
    public async Task History_carries_its_method_note_and_provenance()
    {
        var history = await GetJsonAsync("/api/bridges/FL/1213483000/history");

        // GR-6: the method ships with the numbers.
        Assert.Contains("not engineering advice", history.GetProperty("method").GetString());
        Assert.Equal("trends-fixture-0001", history.GetProperty("provenance").GetProperty("jobRunId").GetString());
    }

    [DockerFact]
    public async Task History_of_an_unknown_structure_is_a_problem_details_404()
    {
        var response = await fixture.Client.GetAsync("/api/bridges/FL/no-such-structure/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("No condition history", problem.GetProperty("title").GetString());
    }

    [DockerFact]
    public async Task History_of_an_unknown_state_is_a_validation_problem()
    {
        var response = await fixture.Client.GetAsync("/api/bridges/ZZ/1213483000/history");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------- trends endpoint

    [DockerFact]
    public async Task Trends_returns_counts_and_shares_that_agree()
    {
        var trend = await GetJsonAsync("/api/trends?level=state&fips=12");

        Assert.Equal("State", trend.GetProperty("level").GetString());
        Assert.Equal("Florida", trend.GetProperty("name").GetString());

        var points = trend.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(6, points.Length);

        foreach (var point in points)
        {
            var total = point.GetProperty("total").GetInt32();
            var parts = point.GetProperty("good").GetInt32() + point.GetProperty("fair").GetInt32()
                      + point.GetProperty("poor").GetInt32() + point.GetProperty("unknown").GetInt32();
            Assert.Equal(total, parts);
        }

        var last = points[^1];
        Assert.Equal(2025, last.GetProperty("year").GetInt32());
        Assert.Equal(3, last.GetProperty("total").GetInt32());
        Assert.Equal(2, last.GetProperty("poor").GetInt32());
        Assert.Equal(66.7, last.GetProperty("poorShare").GetDouble(), 1);
    }

    [DockerFact]
    public async Task Trends_totals_reconcile_with_the_per_bridge_series()
    {
        // AC-3 through the API: summing the rollups must equal summing the observed years of the
        // series they summarise — 14 either way.
        var trend = await GetJsonAsync("/api/trends?level=state&fips=12");
        var rollupTotal = trend.GetProperty("points").EnumerateArray()
            .Sum(p => p.GetProperty("total").GetInt32());

        await using var db = fixture.NewDbContext();
        var observedTotal = await db.BridgeConditionSeries.SumAsync(s => (int)s.ObservedYears);

        Assert.Equal(observedTotal, rollupTotal);
        Assert.Equal(14, rollupTotal);
    }

    [DockerFact]
    public async Task County_level_returns_the_same_fixture_counts_as_state()
    {
        var county = await GetJsonAsync("/api/trends?level=county&fips=12086");

        Assert.Equal("County", county.GetProperty("level").GetString());
        // The label was a synthesized "County FIPS 086, Florida" until FR-1.5 published a real
        // Census name for this code. The rollups are still keyed on the county code item 3
        // publishes — only the label changed — and the fallback for a code TIGER no longer carries
        // is pinned in CountyJoinIntegrationTests.
        Assert.Equal("Miami-Dade County", county.GetProperty("name").GetString());
        Assert.Equal(6, county.GetProperty("points").GetArrayLength());
    }

    [DockerFact]
    public async Task A_year_range_narrows_the_series()
    {
        var trend = await GetJsonAsync("/api/trends?level=state&fips=12&fromYear=2023&toYear=2024");

        var years = trend.GetProperty("points").EnumerateArray()
            .Select(p => p.GetProperty("year").GetInt32()).ToArray();
        Assert.Equal([2023, 2024], years);
    }

    [DockerFact]
    public async Task An_area_with_no_rows_returns_an_empty_series_not_an_error()
    {
        // American Samoa is a real FIPS code that appears in no NBI vintage: a legitimate question
        // with no published answer, which is not the same as a bad request.
        var trend = await GetJsonAsync("/api/trends?level=state&fips=60");

        Assert.Empty(trend.GetProperty("points").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, trend.GetProperty("provenance").ValueKind);
    }

    [DockerTheory]
    [InlineData("/api/trends", "level")]
    [InlineData("/api/trends?level=planet&fips=12", "level")]
    [InlineData("/api/trends?level=state", "fips")]
    [InlineData("/api/trends?level=state&fips=ZZ", "fips")]
    [InlineData("/api/trends?level=county&fips=12", "fips")]
    [InlineData("/api/trends?level=county&fips=abcde", "fips")]
    [InlineData("/api/trends?level=state&fips=12&fromYear=1900", "fromYear")]
    [InlineData("/api/trends?level=state&fips=12&fromYear=2025&toYear=2000", "fromYear")]
    public async Task Bad_requests_return_a_named_validation_problem(string url, string expectedField)
    {
        var response = await fixture.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _),
            $"Expected a validation error naming '{expectedField}' for {url}.");
    }

    [DockerFact]
    public async Task A_state_can_be_asked_for_by_usps_code()
    {
        var byFips = await GetJsonAsync("/api/trends?level=state&fips=12");
        var byUsps = await GetJsonAsync("/api/trends?level=state&fips=FL");

        Assert.Equal(byFips.GetProperty("points").GetArrayLength(), byUsps.GetProperty("points").GetArrayLength());
        Assert.Equal("12", byUsps.GetProperty("fips").GetString());
    }
}
