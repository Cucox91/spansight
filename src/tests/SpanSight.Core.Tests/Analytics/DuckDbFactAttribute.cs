using System.Diagnostics;

namespace SpanSight.Core.Tests.Analytics;

/// <summary>
/// The trend job is DuckDB SQL, so the tests that hold it against the C# classifier need the
/// DuckDB CLI. On a machine without it the suite skips rather than failing — CI installs it (see
/// <c>.github/workflows/ci.yml</c>) and the dev Mac has it from <c>brew install duckdb</c>.
/// <para>
/// Mirrors <c>DockerFactAttribute</c> in the API integration suite.
/// </para>
/// </summary>
public sealed class DuckDbFactAttribute : FactAttribute
{
    public DuckDbFactAttribute() => Skip = DuckDb.SkipReason;
}

/// <summary>Theory form of <see cref="DuckDbFactAttribute"/>.</summary>
public sealed class DuckDbTheoryAttribute : TheoryAttribute
{
    public DuckDbTheoryAttribute() => Skip = DuckDb.SkipReason;
}

/// <summary>Runs DuckDB the same way <c>tools/trends/build-trends.sh</c> does, from the repo root.</summary>
internal static class DuckDb
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            return Run("SELECT 1;") == "1";
        }
        catch (Exception)
        {
            return false;
        }
    });

    public static bool IsAvailable => Available.Value;

    /// <summary>
    /// Null when the golden tests can run. Both prerequisites are checked here so a machine that
    /// is merely missing a build artefact reports that, rather than a confusing DuckDB error.
    /// </summary>
    public static string? SkipReason =>
        !IsAvailable
            ? "duckdb is not on PATH; the trend golden tests run where it is (dev Mac / CI). brew install duckdb"
            : !FixtureParquetExists
                ? "Fixture Parquet missing — run tools/vintages/convert.sh --fixtures first."
                : null;

    /// <summary>
    /// Repository root, found by walking up from the test binary until the marker file appears.
    /// The trend SQL takes relative paths (it is meant to be run from the root), so the tests do
    /// the same rather than rewriting the paths and testing something subtly different.
    /// </summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Executes SQL and returns stdout in DuckDB's headerless list format.</summary>
    public static string Run(string sql, string? initFile = null)
    {
        var arguments = initFile is null ? string.Empty : $"-init \"{initFile}\" ";
        var startInfo = new ProcessStartInfo("duckdb")
        {
            Arguments = $"{arguments}-noheader -list -c \"{sql.Replace("\"", "\\\"")}\"",
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start duckdb.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"duckdb exited {process.ExitCode}: {stderr}\nSQL: {sql}");
        }

        // -init echoes a "Loading resources from …" banner on stderr in some builds; stdout is clean.
        return stdout.Trim();
    }

    /// <summary>
    /// Writes the init script the trend SQL expects: a <c>nbi_source</c> relation over the fixture
    /// Parquet, then <c>tools/trends/trends.sql</c> itself — byte for byte the file the real job
    /// runs, so a drift in it fails here.
    /// </summary>
    public static string CreateFixtureInitScript()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spansight-trend-test-{Guid.NewGuid():N}.sql");
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot, "tools", "trends", "trends.sql"));
        File.WriteAllText(path,
            "CREATE OR REPLACE VIEW nbi_source AS SELECT * FROM " +
            "read_parquet('data/vintages/fixtures-out/parquet/nbi_*.parquet');\n" + sql);
        return path;
    }

    /// <summary>True when the fixture Parquet has been built (tools/vintages/convert.sh --fixtures).</summary>
    public static bool FixtureParquetExists =>
        Directory.Exists(Path.Combine(RepositoryRoot, "data", "vintages", "fixtures-out", "parquet"))
        && Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "data", "vintages", "fixtures-out", "parquet"), "nbi_*.parquet").Any();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpanSight.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (no SpanSight.slnx above the test binary).");
    }
}
