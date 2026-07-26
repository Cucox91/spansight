using SpanSight.Ingestion;

namespace SpanSight.Ingestion.Tests;

/// <summary>Argument contract for <c>convert-vintage</c> (FR-1.1). The command runs without a database.</summary>
public class VintageConvertCliTests
{
    [Fact]
    public void Parses_a_complete_convert_vintage_invocation()
    {
        var (options, error) = CliOptions.Parse(
            ["convert-vintage", "--file", "raw/1992.txt", "--snapshot-year", "1992", "--out", "data/vintages"]);

        Assert.Null(error);
        Assert.Equal("convert-vintage", options!.Command);
        Assert.Equal("raw/1992.txt", options.File);
        Assert.Equal(1992, options.SnapshotYear);
        Assert.Equal("data/vintages", options.Output);
    }

    [Fact]
    public void Out_is_optional_and_defaults_downstream()
    {
        var (options, error) = CliOptions.Parse(["convert-vintage", "--file", "x.txt", "--snapshot-year", "2010"]);

        Assert.Null(error);
        Assert.Null(options!.Output);
    }

    [Theory]
    [InlineData(new[] { "convert-vintage", "--snapshot-year", "1992" }, "--file")]
    [InlineData(new[] { "convert-vintage", "--file", "x.txt" }, "--snapshot-year")]
    public void Missing_required_option_is_reported(string[] args, string expected)
    {
        var (options, error) = CliOptions.Parse(args);

        Assert.Null(options);
        Assert.Contains(expected, error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_numeric_year_is_rejected()
    {
        var (options, error) = CliOptions.Parse(["convert-vintage", "--file", "x.txt", "--snapshot-year", "ninety-two"]);

        Assert.Null(options);
        Assert.Contains("--snapshot-year", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_documents_the_command() =>
        Assert.Contains("convert-vintage", CliOptions.Usage, StringComparison.Ordinal);
}
