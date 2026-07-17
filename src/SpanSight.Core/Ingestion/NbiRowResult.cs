namespace SpanSight.Core.Ingestion;

/// <summary>
/// Outcome of parsing one data line. Exactly one of <see cref="Record"/> /
/// <see cref="StructuralFault"/> is set: a fault means the line never became a record and goes
/// straight to quarantine (FR-0.2).
/// </summary>
public sealed record NbiRowResult
{
    /// <summary>1-based physical line number in the source file (header = line 1).</summary>
    public required int LineNumber { get; init; }

    /// <summary>The raw source line, preserved verbatim for the quarantine table.</summary>
    public required string RawLine { get; init; }

    public NbiRawRecord? Record { get; init; }

    public string? StructuralFault { get; init; }
}

/// <summary>The snapshot file itself is unusable (e.g. a required column is missing).</summary>
public sealed class NbiFormatException(string message) : Exception(message);
