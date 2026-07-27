namespace SpanSight.Core.Tests;

/// <summary>
/// Locates the repository root from the test binary. Several suites need it: the DuckDB jobs take
/// paths relative to the root because that is how they are meant to be run, and the C#/TypeScript
/// parity tests read files out of <c>web/</c>.
/// </summary>
internal static class RepoPath
{
    public static string Root { get; } = FindRoot();

    public static string From(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
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
