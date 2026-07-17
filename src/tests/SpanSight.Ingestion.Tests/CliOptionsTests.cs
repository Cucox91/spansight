using SpanSight.Ingestion;

namespace SpanSight.Ingestion.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Load_parses_required_and_optional_flags()
    {
        var (options, error) = CliOptions.Parse(
            ["load", "--file", "nbi2025.csv", "--snapshot-year", "2025", "--dry-run", "--limit", "500"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal("load", options.Command);
        Assert.Equal("nbi2025.csv", options.File);
        Assert.Equal(2025, options.SnapshotYear);
        Assert.True(options.DryRun);
        Assert.Equal(500, options.Limit);
        Assert.False(options.Force);
    }

    [Theory]
    [InlineData(new string[0], "No command")]
    [InlineData(new[] { "explode" }, "Unknown command")]
    [InlineData(new[] { "load" }, "--file")]
    [InlineData(new[] { "load", "--file", "x.csv" }, "--snapshot-year")]
    [InlineData(new[] { "load", "--file", "x.csv", "--snapshot-year", "yes" }, "--snapshot-year")]
    [InlineData(new[] { "load", "--file", "x.csv", "--snapshot-year", "2025", "--limit", "0" }, "--limit")]
    [InlineData(new[] { "export-geojson" }, "--out")]
    [InlineData(new[] { "load", "--file", "x.csv", "--snapshot-year", "2025", "--wat" }, "Unknown option")]
    public void Invalid_input_reports_a_usable_error(string[] args, string expectedFragment)
    {
        var (options, error) = CliOptions.Parse(args);

        Assert.Null(options);
        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error);
    }

    [Fact]
    public void Migrate_needs_no_further_options()
    {
        var (options, error) = CliOptions.Parse(["migrate"]);

        Assert.Null(error);
        Assert.Equal("migrate", options!.Command);
    }
}
