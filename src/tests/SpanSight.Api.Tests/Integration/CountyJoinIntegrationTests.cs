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

        // The retired-code headline is published against both denominators. Publishing only the row
        // count under the word "structures" over-stated Connecticut by 1,282 bridges that do not
        // exist — 5,644 served rows against 4,362 structures.
        Assert.Equal(1, coverage.GetProperty("rowsUnderRetiredCodes").GetInt64());
        Assert.Equal(1, coverage.GetProperty("structuresUnderRetiredCodes").GetInt64());
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

        // Rows, not structures, is what `unmatched` counts — the same noun discipline the coverage
        // block uses. Nationally these differ nearly three to one (55 rows, 19 structures), so the
        // two fields are asserted separately rather than trusted to agree.
        Assert.Equal(
            coverage.GetProperty("unmatched").GetInt64(),
            reasons.Sum(r => (long)r.GetProperty("rows").GetInt32()));

        var boundary = reasons.Single(r => r.GetProperty("reason").GetString() == "on_county_boundary");
        Assert.Equal(1, boundary.GetProperty("rows").GetInt32());
        Assert.Equal(1, boundary.GetProperty("structures").GetInt32());
        Assert.Equal(0, boundary.GetProperty("maxDistanceMeters").GetInt64());

        var outside = reasons.Single(r => r.GetProperty("reason").GetString() == "outside_all_county_polygons");
        Assert.Equal(2, outside.GetProperty("rows").GetInt32());
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
        // Both denominators reach the payload. They are equal in this fixture and are not
        // nationally, which is exactly why each is asserted rather than one standing for both.
        Assert.Equal(1, retired.GetProperty("rows").GetInt32());
        Assert.Equal(1, retired.GetProperty("structures").GetInt32());
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
    /// <para>
    /// Asserted by snapshotting every published county code, running the loader again, and comparing
    /// the whole set. A shape assertion — "no code is longer than three characters" — passes for any
    /// three-character value regardless of what wrote it, and would not notice the join swapping
    /// Connecticut's legacy codes for planning-region ones, which is the exact override it guards.
    /// </para>
    /// </summary>
    [DockerFact]
    public async Task The_join_leaves_the_published_county_code_untouched()
    {
        await using var db = fixture.NewDbContext();

        var before = await db.Bridges.AsNoTracking()
            .OrderBy(b => b.Id)
            .Select(b => new { b.StateCode, b.StructureNumber, b.RecordType, b.CountyCode })
            .ToListAsync();

        Assert.Equal(fixture.SeedSummary.RowsLoaded, before.Count);

        await fixture.LoadCountyJoinAsync(force: true);

        await using var after = fixture.NewDbContext();
        var reloaded = await after.Bridges.AsNoTracking()
            .OrderBy(b => b.Id)
            .Select(b => new { b.StateCode, b.StructureNumber, b.RecordType, b.CountyCode })
            .ToListAsync();

        Assert.Equal(before, reloaded);
    }

    /// <summary>
    /// Provenance follows the rows being served, not the newest completed run.
    /// <para>
    /// Publishing a second job and then re-publishing the first is what makes this assertion able to
    /// fail. With one run in the database the run stamped on the rows and the newest completed run
    /// are the same row, and the test passes under either implementation — which is precisely the
    /// bug FR-1.3 shipped and had to fix, so a test that cannot distinguish them is no guard at all.
    /// </para>
    /// </summary>
    [DockerFact]
    public async Task Provenance_follows_the_rows_being_served_not_the_newest_completed_run()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"spansight-county-join-second-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "fixtures", "census-join-aggregates");
            foreach (var name in (ReadOnlySpan<string>)["manifest.json", "county.csv", "miss.csv", "disagreement.csv"])
            {
                File.Copy(Path.Combine(source, name), Path.Combine(staging, name));
            }

            var manifestPath = Path.Combine(staging, "manifest.json");
            await File.WriteAllTextAsync(manifestPath,
                (await File.ReadAllTextAsync(manifestPath))
                    .Replace("\"county-join-fixture\"", "\"county-join-fixture-second\""));

            // Publish the second job, then republish the first. The second run stays Completed and
            // is the newest, but its rows were swept away by the convergence delete.
            var second = await fixture.LoadCountyJoinAsync(force: false, staging);
            Assert.False(second.Skipped);

            var first = await fixture.LoadCountyJoinAsync(force: true);
            Assert.False(first.Skipped);

            await using var db = fixture.NewDbContext();

            var newest = await db.CountyJoinRuns.AsNoTracking()
                .Where(r => r.Status == CountyJoinRunStatus.Completed)
                .OrderByDescending(r => r.Id)
                .FirstAsync();
            Assert.Equal("county-join-fixture-second", newest.JobRunId);

            var serving = await db.CensusCounties.AsNoTracking()
                .Select(c => c.CountyJoinRunId).Distinct().SingleAsync();
            Assert.NotEqual(newest.Id, serving);

            // The API must name the run whose rows it just returned, not the newest one.
            var coverage = await CoverageAsync();
            Assert.Equal(
                "county-join-fixture",
                coverage.GetProperty("provenance").GetProperty("jobRunId").GetString());
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            // Restore the collection's state for every other test in it.
            await fixture.LoadCountyJoinAsync(force: true);
        }
    }

    /// <summary>
    /// A load that fails partway leaves the serving tables exactly as they were, and says so on the
    /// run row.
    /// <para>
    /// This is the only test that reaches the pipeline's failure path at all. Every negative test in
    /// the CLI suite passes <c>dryRun: true</c>, where <c>run</c> is null and the
    /// <c>catch (Exception ex) when (run is not null)</c> filter excludes the handler entirely — so
    /// the transaction rollback and the Failed-status write had no coverage whatsoever until this.
    /// </para>
    /// </summary>
    [DockerFact]
    public async Task A_failed_load_rolls_back_and_records_why_without_disturbing_the_served_rows()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"spansight-county-join-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "fixtures", "census-join-aggregates");
            foreach (var name in (ReadOnlySpan<string>)["manifest.json", "county.csv", "miss.csv", "disagreement.csv"])
            {
                File.Copy(Path.Combine(source, name), Path.Combine(staging, name));
            }

            var manifestPath = Path.Combine(staging, "manifest.json");
            await File.WriteAllTextAsync(manifestPath,
                (await File.ReadAllTextAsync(manifestPath))
                    .Replace("\"county-join-fixture\"", "\"county-join-fixture-doomed\"")
                    // Counties reconcile; the disagreement count does not. The failure therefore
                    // happens *after* two of the three files are already upserted, which is exactly
                    // the state the transaction exists to undo.
                    .Replace("\"disagreements\": 3", "\"disagreements\": 99"));

            await using (var probe = fixture.NewDbContext())
            {
                var before = await probe.CensusCounties.AsNoTracking()
                    .Select(c => c.CountyJoinRunId).Distinct().SingleAsync();

                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => fixture.LoadCountyJoinAsync(force: true, staging));

                await using var after = fixture.NewDbContext();

                // The counties the doomed run upserted were rolled back: every served county still
                // carries the run id it had before, and the counts are unchanged.
                Assert.Equal(before, await after.CensusCounties.AsNoTracking()
                    .Select(c => c.CountyJoinRunId).Distinct().SingleAsync());
                Assert.Equal(6, await after.CensusCounties.CountAsync());
                Assert.Equal(3, await after.CountyJoinDisagreements.CountAsync());

                // The run row survives the rollback — it was written before the transaction opened —
                // and carries the reason, so an operator can see what happened.
                var failed = await after.CountyJoinRuns.AsNoTracking()
                    .SingleAsync(r => r.JobRunId == "county-join-fixture-doomed");
                Assert.Equal(CountyJoinRunStatus.Failed, failed.Status);
                Assert.Contains("disagreement.csv", failed.Error);

                // And the API still serves the good run, not the failed one.
                var coverage = await CoverageAsync();
                Assert.Equal(
                    "county-join-fixture",
                    coverage.GetProperty("provenance").GetProperty("jobRunId").GetString());
            }
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            await fixture.LoadCountyJoinAsync(force: true);
        }
    }
}
