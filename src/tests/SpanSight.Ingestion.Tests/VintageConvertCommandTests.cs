using System.Text.Json;

using SpanSight.Core.Vintages;
using SpanSight.Ingestion;

namespace SpanSight.Ingestion.Tests;

/// <summary>
/// End-to-end behaviour of <c>convert-vintage</c> against the committed era fixtures (FR-1.1):
/// output layout, provenance hashing, reject file shape and exit codes. File work only — the
/// command never touches a database (ADR-005).
/// </summary>
public class VintageConvertCommandTests : IDisposable
{
    private readonly string _outDir = Path.Combine(
        Path.GetTempPath(), "spansight-vintage-tests", Guid.NewGuid().ToString("n"));

    private static string Fixture(int year) => Path.Combine("fixtures", "vintages", $"nbi_{year}.txt");

    private CliOptions Options(int year, string? file = null) => new()
    {
        Command = "convert-vintage",
        File = file ?? Fixture(year),
        SnapshotYear = year,
        Output = _outDir,
    };

    [Theory]
    [InlineData(1992)]
    [InlineData(2010)]
    [InlineData(2025)]
    public async Task Writes_normalized_and_reject_files_and_reports_reconciling_counts(int year)
    {
        var exit = await VintageConvertCommand.RunAsync(Options(year));

        Assert.Equal(0, exit);

        var normalized = Path.Combine(_outDir, "normalized", $"nbi_{year}.csv");
        var rejects = Path.Combine(_outDir, "rejects", $"{year}.csv");
        Assert.True(File.Exists(normalized), $"missing {normalized}");
        Assert.True(File.Exists(rejects), $"missing {rejects}");

        // 300 fixture rows + the header.
        Assert.Equal(301, File.ReadAllLines(normalized).Length);

        // Rejects always carry their header even when empty — an empty file and a missing file
        // must not look the same to whoever reads it.
        var rejectLines = File.ReadAllLines(rejects);
        Assert.Equal("vintage_year,source_row,reason,detail,raw_line", rejectLines[0]);
        Assert.Single(rejectLines);
    }

    [Fact]
    public async Task Summary_json_carries_the_provenance_the_catalog_needs()
    {
        var stdout = new StringWriter();
        var original = Console.Out;
        Console.SetOut(stdout);
        try
        {
            Assert.Equal(0, await VintageConvertCommand.RunAsync(Options(2025)));
        }
        finally
        {
            Console.SetOut(original);
        }

        using var doc = JsonDocument.Parse(stdout.ToString());
        var root = doc.RootElement;

        Assert.Equal(2025, root.GetProperty("Year").GetInt32());
        Assert.Equal(nameof(VintageEra.PerformanceMeasures), root.GetProperty("Era").GetString());
        Assert.Equal("nbi_2025.txt", root.GetProperty("SourceFile").GetString());
        Assert.Equal(300, root.GetProperty("RowsRead").GetInt64());
        Assert.Equal(300, root.GetProperty("RowsConverted").GetInt64());
        Assert.Equal(0, root.GetProperty("RowsRejected").GetInt64());
        Assert.True(root.GetProperty("Reconciles").GetBoolean());

        // SHA-256 of the source file, lowercase hex — the catalog's provenance key.
        var sha = root.GetProperty("SourceSha256").GetString()!;
        Assert.Equal(64, sha.Length);
        Assert.All(sha, c => Assert.True(char.IsAsciiDigit(c) || (c is >= 'a' and <= 'f')));
        Assert.Equal(sha, Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Fixture(2025)))));

        Assert.Contains("SUFFICIENCY_RATING", root.GetProperty("AbsentColumns").EnumerateArray()
            .Select(e => e.GetString()));
    }

    [Fact]
    public async Task Same_input_produces_the_same_output_so_a_rerun_is_safe()
    {
        Assert.Equal(0, await VintageConvertCommand.RunAsync(Options(2010)));
        var first = await File.ReadAllTextAsync(Path.Combine(_outDir, "normalized", "nbi_2010.csv"));

        Assert.Equal(0, await VintageConvertCommand.RunAsync(Options(2010)));
        var second = await File.ReadAllTextAsync(Path.Combine(_outDir, "normalized", "nbi_2010.csv"));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Missing_source_file_exits_non_zero_without_creating_output()
    {
        var exit = await VintageConvertCommand.RunAsync(Options(2025, file: "does/not/exist.txt"));

        Assert.Equal(1, exit);
        Assert.False(Directory.Exists(Path.Combine(_outDir, "normalized")));
    }

    [Fact]
    public async Task Wrong_era_for_the_declared_year_throws_and_writes_no_rows()
    {
        // The acceptance case, through the command surface: a 1992 file declared as 2025.
        var options = Options(2025, file: Fixture(1992));

        await Assert.ThrowsAsync<VintageFormatException>(() => VintageConvertCommand.RunAsync(options));

        var normalized = Path.Combine(_outDir, "normalized", "nbi_2025.csv");
        Assert.True(!File.Exists(normalized) || File.ReadAllLines(normalized).Length <= 1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outDir))
        {
            Directory.Delete(_outDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
