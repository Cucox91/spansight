using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using SpanSight.Core.Data;

namespace SpanSight.Ingestion.Tests;

/// <summary>
/// <c>load-trends</c> argument handling and the checks that run before any database is touched
/// (FR-1.2). The database-backed half lives in the API integration suite, against real PostGIS.
/// </summary>
public class TrendLoadCliTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "fixtures", "trends");

    [Fact]
    public void Load_trends_defaults_to_where_the_job_writes()
    {
        var (options, error) = CliOptions.Parse(["load-trends"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal("load-trends", options.Command);
        Assert.Equal(Path.Combine("data", "trends"), options.File);
    }

    [Fact]
    public void Load_trends_accepts_an_explicit_directory_and_flags()
    {
        var (options, error) = CliOptions.Parse(
            ["load-trends", "--file", "data/trends/fixtures-out", "--force", "--dry-run"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal("data/trends/fixtures-out", options.File);
        Assert.True(options.Force);
        Assert.True(options.DryRun);
    }

    [Fact]
    public void Usage_documents_the_command()
    {
        Assert.Contains("load-trends", CliOptions.Usage);
        Assert.Contains("build-trends.sh", CliOptions.Usage);
    }

    [Fact]
    public async Task A_dry_run_validates_the_whole_fixture_without_a_database()
    {
        // No connection string is configured here at all: if the dry-run path touched the database
        // this would throw rather than return a summary.
        var pipeline = new TrendLoadPipeline(NoDatabase(), NullLogger<TrendLoadPipeline>.Instance);

        var summary = await pipeline.RunAsync(FixtureDir, dryRun: true, force: false);

        Assert.False(summary.Skipped);
        Assert.Null(summary.RunId);
        Assert.Equal(3, summary.SeriesRows);
        Assert.Equal(12, summary.RollupRows);
        Assert.Equal("trends-fixture-0001", summary.JobRunId);
    }

    [Fact]
    public async Task A_missing_output_directory_says_which_file_and_how_to_make_it()
    {
        var pipeline = new TrendLoadPipeline(NoDatabase(), NullLogger<TrendLoadPipeline>.Instance);

        var error = await Assert.ThrowsAsync<FileNotFoundException>(
            () => pipeline.RunAsync(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}"),
                dryRun: true, force: false));

        Assert.Contains("build-trends.sh", error.Message);
    }

    [Fact]
    public async Task A_series_that_does_not_match_its_span_is_refused()
    {
        // The exact corruption the API could not explain: a span of six years with five characters.
        var staging = await StageAsync(
            series: "state_code,structure_number,first_year,last_year,observed_years,ratings\n" +
                    "12,1213483000,2020,2025,5,77665\n");
        try
        {
            var pipeline = new TrendLoadPipeline(NoDatabase(), NullLogger<TrendLoadPipeline>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(staging, dryRun: true, force: false));

            Assert.Contains("Malformed series", error.Message);
            Assert.Contains("1213483000", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public async Task A_rollup_whose_classes_do_not_sum_to_its_total_is_refused()
    {
        var staging = await StageAsync(
            rollup: "level,fips,vintage_year,total,good,fair,poor,unknown\n" +
                    "state,12,2025,10,1,1,1,1\n");
        try
        {
            var pipeline = new TrendLoadPipeline(NoDatabase(), NullLogger<TrendLoadPipeline>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(staging, dryRun: true, force: false));

            Assert.Contains("!= 10", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public async Task An_unknown_rollup_level_is_refused_rather_than_silently_dropped()
    {
        var staging = await StageAsync(
            rollup: "level,fips,vintage_year,total,good,fair,poor,unknown\n" +
                    "planet,12,2025,1,1,0,0,0\n");
        try
        {
            var pipeline = new TrendLoadPipeline(NoDatabase(), NullLogger<TrendLoadPipeline>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(staging, dryRun: true, force: false));

            Assert.Contains("planet", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>Copies the fixture to a temp directory, overriding one file.</summary>
    private static async Task<string> StageAsync(string? series = null, string? rollup = null)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"spansight-trend-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        foreach (var name in (ReadOnlySpan<string>)["manifest.json", "bridge_series.csv", "rollup.csv"])
        {
            File.Copy(Path.Combine(FixtureDir, name), Path.Combine(staging, name));
        }

        if (series is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "bridge_series.csv"), series);
        }

        if (rollup is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "rollup.csv"), rollup);
        }

        return staging;
    }

    /// <summary>A context pointed at nothing: any query would throw, which is the point.</summary>
    private static SpanSightDbContext NoDatabase() => new(
        new DbContextOptionsBuilder<SpanSightDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none")
            .Options);
}
