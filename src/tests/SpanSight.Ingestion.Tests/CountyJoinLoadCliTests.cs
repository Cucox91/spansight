using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using SpanSight.Core.Data;

namespace SpanSight.Ingestion.Tests;

/// <summary>
/// <c>load-county-join</c> argument handling and the checks that run before any database is touched
/// (FR-1.5 AC-2). The database-backed half lives in the API integration suite, against real PostGIS.
/// </summary>
public class CountyJoinLoadCliTests
{
    private static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "census-join-aggregates");

    [Fact]
    public void Load_county_join_defaults_to_where_the_job_writes()
    {
        var (options, error) = CliOptions.Parse(["load-county-join"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal("load-county-join", options.Command);
        Assert.Equal(Path.Combine("data", "census", "join"), options.File);
    }

    [Fact]
    public void Load_county_join_accepts_an_explicit_directory_and_flags()
    {
        var (options, error) = CliOptions.Parse(
            ["load-county-join", "--file", "data/census/join/fixtures-out", "--force", "--dry-run"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal("data/census/join/fixtures-out", options.File);
        Assert.True(options.Force);
        Assert.True(options.DryRun);
    }

    /// <summary>
    /// The verb allowlist in <c>CliOptions.Parse</c> and the switch in <c>Program.cs</c> are two
    /// independent lists; missing the first gives "Unknown command", missing the second falls
    /// through to Usage. Both are silent-ish, so the allowlist half is pinned here.
    /// </summary>
    [Fact]
    public void Usage_documents_the_command_and_names_the_job_that_produces_its_input()
    {
        Assert.Contains("load-county-join", CliOptions.Usage);
        Assert.Contains("join-counties.sh", CliOptions.Usage);
    }

    [Fact]
    public async Task A_dry_run_validates_the_whole_fixture_without_a_database()
    {
        // No reachable database at all: if the dry-run path touched one this would throw rather than
        // return a summary.
        var pipeline = new CountyJoinLoadPipeline(NoDatabase(), NullLogger<CountyJoinLoadPipeline>.Instance);

        var summary = await pipeline.RunAsync(FixtureDir, dryRun: true, force: false);

        Assert.False(summary.Skipped);
        Assert.Null(summary.RunId);
        Assert.Equal(6, summary.Counties);
        Assert.Equal(3, summary.Misses);
        Assert.Equal(3, summary.Disagreements);
        Assert.Equal("county-join-fixture", summary.JobRunId);
    }

    [Fact]
    public async Task A_missing_output_directory_says_which_file_and_how_to_make_it()
    {
        var pipeline = new CountyJoinLoadPipeline(NoDatabase(), NullLogger<CountyJoinLoadPipeline>.Instance);

        var error = await Assert.ThrowsAsync<FileNotFoundException>(
            () => pipeline.RunAsync(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}"),
                dryRun: true, force: false));

        Assert.Contains("join-counties.sh", error.Message);
    }

    /// <summary>
    /// The failure that would make coverage look honest while hiding structures: a short quarantine
    /// file. Coverage would still report 3 unmatched while only 2 of them could be inspected, which
    /// is precisely what AC-2 exists to prevent.
    /// </summary>
    [Fact]
    public async Task A_quarantine_that_does_not_account_for_every_unmatched_bridge_is_refused()
    {
        // The manifest's `misses` count is taken off the written miss file while `unmatched` comes
        // from the coverage row, so a job bug can make the two disagree while each file is
        // internally consistent. Both are lowered here, leaving `unmatched` at 3: that gets past the
        // per-file row-count reconciliation and onto the check this test is actually about.
        var staging = await StageAsync(
            miss:
                "state_code,structure_number,record_type,nbi_county_fips,reason,touching_counties," +
                "nearest_county_fips,nearest_distance_m,lon,lat\n" +
                "12,OF-0001,1,12086,outside_all_county_polygons,0,12086,9543,-80.0,25.5\n",
            manifestEdit: json => json.Replace("\"misses\": 3", "\"misses\": 1"));
        try
        {
            var pipeline = new CountyJoinLoadPipeline(NoDatabase(), NullLogger<CountyJoinLoadPipeline>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(staging, dryRun: true, force: false));

            Assert.Contains("quarantined with a reason", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// A boundary reason on a structure that touches nothing is not a shape the join can produce, so
    /// the loader refuses it rather than letting the QA page render a reason its own evidence denies.
    /// </summary>
    [Fact]
    public async Task A_miss_reason_the_evidence_contradicts_is_refused()
    {
        var staging = await StageAsync(miss:
            "state_code,structure_number,record_type,nbi_county_fips,reason,touching_counties," +
            "nearest_county_fips,nearest_distance_m,lon,lat\n" +
            "12,OF-0001,1,12086,on_county_boundary,0,12086,9543,-80.0,25.5\n" +
            "15,PC-0001,1,15003,outside_all_county_polygons,0,,,-155.0,20.0\n" +
            "48,BD-0001,1,48301,on_county_boundary,1,48301,0,-103.98403,31.993588\n");
        try
        {
            var pipeline = new CountyJoinLoadPipeline(NoDatabase(), NullLogger<CountyJoinLoadPipeline>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(staging, dryRun: true, force: false));

            Assert.Contains("OF-0001", error.Message);
            Assert.Contains("on_county_boundary", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// A county key the report card could not resolve must not reach the serving tables. The job
    /// checks the same shape; this is the second door on the same room, because the CSV between them
    /// can be hand-edited.
    /// </summary>
    [Fact]
    public async Task A_county_key_that_is_not_its_own_state_fips_is_refused()
    {
        var staging = await StageAsync(county:
            "county_fips,state_fips,name,name_lsad,land_area_m2,water_area_m2,tiger_vintage," +
            "population,population_moe,acs_vintage,acs_period,acs_table\n" +
            "99086,12,Miami-Dade,Miami-Dade County,4920945684,1267367219,2025,2738356,,2024,2020-2024,B01003\n");
        try
        {
            var pipeline = new CountyJoinLoadPipeline(NoDatabase(), NullLogger<CountyJoinLoadPipeline>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(staging, dryRun: true, force: false));

            Assert.Contains("99086", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// ACS jam values are large negative sentinels. The staging converter already nulls them, so one
    /// arriving here means that step was bypassed — and a negative population would flow straight
    /// into a per-capita figure.
    /// </summary>
    [Fact]
    public async Task A_negative_acs_population_is_refused_as_a_jam_value()
    {
        var staging = await StageAsync(county:
            "county_fips,state_fips,name,name_lsad,land_area_m2,water_area_m2,tiger_vintage," +
            "population,population_moe,acs_vintage,acs_period,acs_table\n" +
            "12086,12,Miami-Dade,Miami-Dade County,4920945684,1267367219,2025,-555555555,,2024,2020-2024,B01003\n");
        try
        {
            var pipeline = new CountyJoinLoadPipeline(NoDatabase(), NullLogger<CountyJoinLoadPipeline>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(staging, dryRun: true, force: false));

            Assert.Contains("jam code", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// A manifest whose matched and unmatched counts do not add up to the bridges it claims to have
    /// joined is refused before anything is read: the coverage share is the published deliverable of
    /// AC-2, and one computed from inconsistent parts is worse than none.
    /// </summary>
    [Fact]
    public async Task A_manifest_whose_coverage_does_not_add_up_is_refused()
    {
        var staging = await StageAsync();
        try
        {
            var manifestPath = Path.Combine(staging, "manifest.json");
            var manifest = await File.ReadAllTextAsync(manifestPath);
            await File.WriteAllTextAsync(manifestPath, manifest.Replace("\"matched\": 13", "\"matched\": 12"));

            var pipeline = new CountyJoinLoadPipeline(NoDatabase(), NullLogger<CountyJoinLoadPipeline>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(staging, dryRun: true, force: false));

            Assert.Contains("not the 16 bridges", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>Copies the fixture to a temp directory, overriding only what a test varies.</summary>
    private static async Task<string> StageAsync(
        string? county = null, string? miss = null, Func<string, string>? manifestEdit = null)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"spansight-county-join-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        foreach (var name in (ReadOnlySpan<string>)["manifest.json", "county.csv", "miss.csv", "disagreement.csv"])
        {
            File.Copy(Path.Combine(FixtureDir, name), Path.Combine(staging, name));
        }

        if (county is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "county.csv"), county);
        }

        if (miss is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "miss.csv"), miss);
        }

        if (manifestEdit is not null)
        {
            var path = Path.Combine(staging, "manifest.json");
            await File.WriteAllTextAsync(path, manifestEdit(await File.ReadAllTextAsync(path)));
        }

        return staging;
    }

    /// <summary>A context pointed at nothing: any query would throw, which is the point.</summary>
    private static SpanSightDbContext NoDatabase() => new(
        new DbContextOptionsBuilder<SpanSightDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none")
            .Options);
}
