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

/// <summary>
/// For the FR-1.3 tests that run the deterioration job over the committed synthetic CSV rather than
/// over the era fixtures: those need the DuckDB CLI but no built Parquet, so they can run on a
/// machine that has never converted a vintage.
/// </summary>
public sealed class DuckDbOnlyFactAttribute : FactAttribute
{
    public DuckDbOnlyFactAttribute() => Skip = DuckDb.CliSkipReason;
}

/// <summary>Theory form of <see cref="DuckDbOnlyFactAttribute"/>.</summary>
public sealed class DuckDbOnlyTheoryAttribute : TheoryAttribute
{
    public DuckDbOnlyTheoryAttribute() => Skip = DuckDb.CliSkipReason;
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
        CliSkipReason
        ?? (!FixtureParquetExists
            ? "Fixture Parquet missing — run tools/vintages/convert.sh --fixtures first."
            : null);

    /// <summary>Null when the DuckDB CLI is present; the only prerequisite for the CSV-driven jobs.</summary>
    public static string? CliSkipReason =>
        IsAvailable
            ? null
            : "duckdb is not on PATH; the SQL golden tests run where it is (dev Mac / CI). brew install duckdb";

    /// <summary>
    /// Repository root, found by walking up from the test binary until the marker file appears.
    /// The trend SQL takes relative paths (it is meant to be run from the root), so the tests do
    /// the same rather than rewriting the paths and testing something subtly different.
    /// </summary>
    public static string RepositoryRoot { get; } = RepoPath.Root;

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

    /// <summary>
    /// The FR-1.3 job over the same era fixtures — the anti-drift half, running the published SQL.
    /// </summary>
    public static string CreateDeteriorationFixtureInitScript() => WriteInitScript(
        "CREATE OR REPLACE VIEW nbi_source AS SELECT * FROM " +
        "read_parquet('data/vintages/fixtures-out/parquet/nbi_*.parquet');",
        Path.Combine("tools", "deterioration", "deterioration.sql"));

    /// <summary>
    /// The FR-1.3 job over the committed synthetic vintage CSV — the hand-computed half (AC-3).
    /// Wired exactly as <c>build-deterioration.sh --synthetic</c> wires it, so the test and the script
    /// cannot drift into reading the fixture differently.
    /// </summary>
    public static string CreateSyntheticInitScript() => WriteInitScript(
        """
        CREATE OR REPLACE VIEW nbi_source AS SELECT * REPLACE (
            CAST(VINTAGE_YEAR AS SMALLINT) AS VINTAGE_YEAR, CAST(SOURCE_ROW AS INTEGER) AS SOURCE_ROW)
          FROM read_csv('src/tests/fixtures/deterioration/synthetic_vintages.csv', all_varchar=true);
        """,
        Path.Combine("tools", "deterioration", "deterioration.sql"));

    private static string WriteInitScript(string sourceSql, string relativeSqlPath)
    {
        var path = Path.Combine(Path.GetTempPath(), $"spansight-sql-test-{Guid.NewGuid():N}.sql");
        File.WriteAllText(path, sourceSql + "\n" + File.ReadAllText(Path.Combine(RepositoryRoot, relativeSqlPath)));
        return path;
    }

    /// <summary>True when the fixture Parquet has been built (tools/vintages/convert.sh --fixtures).</summary>
    public static bool FixtureParquetExists =>
        Directory.Exists(Path.Combine(RepositoryRoot, "data", "vintages", "fixtures-out", "parquet"))
        && Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "data", "vintages", "fixtures-out", "parquet"), "nbi_*.parquet").Any();
}
