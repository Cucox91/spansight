using System.Globalization;
using System.Text;

namespace SpanSight.Core.Vintages;

/// <summary>Row-level outcome: exactly one of <see cref="Values"/> / <see cref="RejectReason"/> is set.</summary>
public sealed record VintageRowResult
{
    /// <summary>1-based physical line number in the source file (header = line 1).</summary>
    public required int LineNumber { get; init; }

    /// <summary>Normalized values, parallel to <see cref="VintageSchema.Columns"/>; null where absent.</summary>
    public string?[]? Values { get; init; }

    public string? RejectReason { get; init; }

    public string? RejectDetail { get; init; }
}

/// <summary>Reconciliation counts for one converted vintage (FR-1.1 AC-2).</summary>
public sealed record VintageConversionSummary
{
    public required int Year { get; init; }

    public required string Era { get; init; }

    public required string SourceFile { get; init; }

    public required string SourceSha256 { get; init; }

    /// <summary>Data rows read from the source (excludes the header and trailing blank lines).</summary>
    public required long RowsRead { get; init; }

    public required long RowsConverted { get; init; }

    public required long RowsRejected { get; init; }

    public required int SourceColumns { get; init; }

    public required IReadOnlyList<string> AbsentColumns { get; init; }

    public required IReadOnlyDictionary<string, long> RejectsByReason { get; init; }

    /// <summary>Rows in plus rows out must agree — the check AC-2 actually asks for.</summary>
    public bool Reconciles => RowsConverted + RowsRejected == RowsRead;
}

/// <summary>
/// Converts one NBI vintage into the normalized superset (FR-1.1 AC-1/AC-2).
/// <para>
/// Output is an RFC 4180 CSV with provenance columns prepended, which DuckDB then writes to
/// Parquet — the same shape as the tile build, where .NET exports an intermediate and the
/// specialist tool produces the artifact (<c>tools/build-tiles.sh</c>). Doing the parse here keeps
/// every era quirk in tested C# rather than in a shell pipeline.
/// </para>
/// </summary>
public sealed class VintageConverter
{
    /// <summary>Header of the normalized intermediate: provenance first, then the superset in canonical order.</summary>
    public static IReadOnlyList<string> OutputColumns { get; } =
        [.. VintageSchema.ProvenanceColumns, .. VintageSchema.Columns];

    /// <summary>
    /// Streams <paramref name="source"/>, writing normalized rows to <paramref name="normalized"/> and
    /// itemized rejects to <paramref name="rejects"/>. Nothing is dropped: every data row lands in
    /// exactly one of the two.
    /// </summary>
    public async Task<VintageConversionSummary> ConvertAsync(
        Stream source,
        TextWriter normalized,
        TextWriter rejects,
        int year,
        string sourceFileName,
        string sourceSha256,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(source, leaveOpen: true);

        var headerLine = await reader.ReadLineAsync(cancellationToken)
            ?? throw new VintageFormatException("Source file is empty — no header row.");

        var header = VintageHeader.Bind(headerLine, year);

        await normalized.WriteLineAsync(string.Join(",", OutputColumns.Select(Csv)));
        await rejects.WriteLineAsync("vintage_year,source_row,reason,detail,raw_line");

        var rowsRead = 0L;
        var rowsConverted = 0L;
        var rejectCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var lineNumber = 1;
        var builder = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue; // trailing blank lines are common and are not data
            }

            rowsRead++;
            var result = Normalize(line, lineNumber, header);

            if (result.Values is null)
            {
                var reason = result.RejectReason!;
                rejectCounts[reason] = rejectCounts.GetValueOrDefault(reason) + 1;
                await rejects.WriteLineAsync(string.Join(",",
                [
                    year.ToString(CultureInfo.InvariantCulture),
                    result.LineNumber.ToString(CultureInfo.InvariantCulture),
                    Csv(reason),
                    Csv(result.RejectDetail ?? string.Empty),
                    Csv(line),
                ]));
                continue;
            }

            rowsConverted++;
            builder.Clear();
            builder.Append(year.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(Csv(sourceFileName)).Append(',')
                   .Append(Csv(sourceSha256)).Append(',')
                   .Append(result.LineNumber.ToString(CultureInfo.InvariantCulture));
            foreach (var value in result.Values)
            {
                builder.Append(',').Append(value is null ? string.Empty : Csv(value));
            }

            await normalized.WriteLineAsync(builder.ToString());
        }

        return new VintageConversionSummary
        {
            Year = year,
            Era = header.Era.ToString(),
            SourceFile = sourceFileName,
            SourceSha256 = sourceSha256,
            RowsRead = rowsRead,
            RowsConverted = rowsConverted,
            RowsRejected = rowsRead - rowsConverted,
            SourceColumns = header.FieldCount,
            AbsentColumns = header.AbsentColumns,
            RejectsByReason = rejectCounts,
        };
    }

    /// <summary>Maps one source line onto the superset, or explains why it cannot be.</summary>
    public static VintageRowResult Normalize(string line, int lineNumber, VintageHeader header)
    {
        var fields = VintageLine.Split(line);
        if (fields.Length != header.FieldCount)
        {
            return new VintageRowResult
            {
                LineNumber = lineNumber,
                RejectReason = VintageRejectReasons.FieldCountMismatch,
                RejectDetail = $"got {fields.Length} fields, header declares {header.FieldCount}",
            };
        }

        var map = header.SourceIndexByColumn;
        var values = new string?[map.Length];
        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] < 0)
            {
                values[i] = null; // this vintage does not carry the column
                continue;
            }

            var value = fields[map[i]];
            values[i] = value.Length == 0 ? null : value;
        }

        var state = values[IndexOfRequired(VintageSchema.StateCode)];
        var structure = values[IndexOfRequired(VintageSchema.StructureNumber)];
        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(structure))
        {
            return new VintageRowResult
            {
                LineNumber = lineNumber,
                RejectReason = VintageRejectReasons.MissingKeyField,
                RejectDetail = $"state='{state}' structure='{structure}'",
            };
        }

        return new VintageRowResult { LineNumber = lineNumber, Values = values };
    }

    private static int IndexOfRequired(string column)
    {
        var index = RequiredIndexes.GetValueOrDefault(column, -1);
        return index >= 0 ? index : throw new InvalidOperationException($"{column} is not in the superset schema.");
    }

    private static readonly Dictionary<string, int> RequiredIndexes = BuildRequiredIndexes();

    private static Dictionary<string, int> BuildRequiredIndexes()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < VintageSchema.Columns.Count; i++)
        {
            map[VintageSchema.Columns[i]] = i;
        }

        return map;
    }

    private static readonly System.Buffers.SearchValues<char> NeedsQuoting =
        System.Buffers.SearchValues.Create(",\"\n\r");

    /// <summary>RFC 4180 quoting for the intermediate — DuckDB reads it with standard settings.</summary>
    private static string Csv(string value) =>
        value.AsSpan().IndexOfAny(NeedsQuoting) >= 0
            ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : value;
}
