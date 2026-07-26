using System.Security.Cryptography;
using System.Text.Json;

using SpanSight.Core.Vintages;

namespace SpanSight.Ingestion;

/// <summary>
/// <c>convert-vintage</c>: normalize one NBI annual vintage to the superset schema (FR-1.1).
/// <para>
/// Writes an RFC 4180 intermediate plus an itemized reject file, and prints a JSON summary that
/// <c>tools/vintages/convert.sh</c> folds into the catalog manifest. Touches no database — the
/// historical set never enters the serving Postgres (ADR-005).
/// </para>
/// </summary>
public static class VintageConvertCommand
{
    public static async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        var sourcePath = options.File!;
        var year = options.SnapshotYear!.Value;

        if (!File.Exists(sourcePath))
        {
            await Console.Error.WriteLineAsync($"Source file not found: {sourcePath}");
            return 1;
        }

        var outDir = options.Output ?? Path.Combine("data", "vintages");
        var normalizedDir = Path.Combine(outDir, "normalized");
        var rejectsDir = Path.Combine(outDir, "rejects");
        Directory.CreateDirectory(normalizedDir);
        Directory.CreateDirectory(rejectsDir);

        var normalizedPath = Path.Combine(normalizedDir, $"nbi_{year}.csv");
        var rejectsPath = Path.Combine(rejectsDir, $"{year}.csv");

        var sha = await Sha256Async(sourcePath, cancellationToken);

        await using var source = File.OpenRead(sourcePath);
        await using var normalized = new StreamWriter(normalizedPath);
        await using var rejects = new StreamWriter(rejectsPath);

        var summary = await new VintageConverter().ConvertAsync(
            source, normalized, rejects, year, Path.GetFileName(sourcePath), sha, cancellationToken);

        await normalized.FlushAsync(cancellationToken);
        await rejects.FlushAsync(cancellationToken);

        var payload = new
        {
            summary.Year,
            summary.Era,
            summary.SourceFile,
            summary.SourceSha256,
            summary.RowsRead,
            summary.RowsConverted,
            summary.RowsRejected,
            summary.SourceColumns,
            OutputColumns = VintageConverter.OutputColumns.Count,
            summary.AbsentColumns,
            summary.RejectsByReason,
            summary.Reconciles,
            NormalizedFile = normalizedPath,
            RejectsFile = rejectsPath,
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        if (!summary.Reconciles)
        {
            await Console.Error.WriteLineAsync(
                $"Reconciliation failed for {year}: {summary.RowsConverted} converted + " +
                $"{summary.RowsRejected} rejected != {summary.RowsRead} read.");
            return 1;
        }

        return 0;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
