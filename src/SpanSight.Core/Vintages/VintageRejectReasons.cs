namespace SpanSight.Core.Vintages;

/// <summary>
/// Machine-readable reject codes for the vintage conversion (FR-1.1 AC-2), in the same style and
/// with the same contract as the Phase 0 <c>QuarantineReasons</c>: stable strings, add but never
/// rename.
/// <para>
/// Deliberately narrow. The vintage Parquet is a faithful normalized copy of published data, so
/// only rows that cannot be turned into a record at all are rejected. Semantic screening
/// (implausible coordinates, impossible build years) stays out: those rows are still published
/// history, and discarding them here would silently change every downstream statistic. FR-1.2
/// applies the Phase 0 validator when it replays the classifier.
/// </para>
/// </summary>
public static class VintageRejectReasons
{
    /// <summary>Field count does not match the header — the line cannot be split into columns.</summary>
    public const string FieldCountMismatch = "row_field_count_mismatch";

    /// <summary>State code and/or structure number blank: the row has no identity.</summary>
    public const string MissingKeyField = "missing_key_field";
}
