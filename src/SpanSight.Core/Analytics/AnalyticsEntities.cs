namespace SpanSight.Core.Analytics;

/// <summary>Geographic level a <see cref="ConditionRollup"/> summarises.</summary>
public enum RollupLevel
{
    /// <summary>Keyed by 2-digit state FIPS.</summary>
    State = 1,

    /// <summary>Keyed by 5-digit state+county FIPS.</summary>
    County = 2,
}

public enum TrendRunStatus
{
    Running = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// One execution of <c>tools/trends/build-trends.sh</c>, as recorded when its outputs were loaded
/// (FR-1.2 AC-1, NFR-3). Every analytics row points at the run that produced it, so any figure on
/// the site traces back to a job id and the exact vintage catalog it read.
/// </summary>
public class TrendRun
{
    public long Id { get; set; }

    /// <summary>The job's own run id from <c>manifest.json</c> (e.g. <c>trends-20260726T183452Z</c>).</summary>
    public required string JobRunId { get; set; }

    /// <summary>SHA-256 of <c>tools/vintages/catalog.json</c> — pins which Parquet set was read.</summary>
    public required string CatalogSha256 { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    public short FirstYear { get; set; }

    public short LastYear { get; set; }

    public int Vintages { get; set; }

    /// <summary>Structures in the per-bridge series — one row each.</summary>
    public int Bridges { get; set; }

    /// <summary>Published condition observations across every structure and year.</summary>
    public long Observations { get; set; }

    public int RollupRows { get; set; }

    public TrendRunStatus Status { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// A structure's published condition history, 1992–2025 (FR-1.2 AC-1). One row per structure;
/// <see cref="Ratings"/> is packed by <see cref="ConditionSeriesCodec"/>.
/// Natural key: (<see cref="StateCode"/>, <see cref="StructureNumber"/>) — the trend job keeps one
/// record-type-'1' row per structure per vintage (see <c>tools/trends/trends.sql</c>).
/// </summary>
public class BridgeConditionSeries
{
    public long Id { get; set; }

    /// <summary>Item 1 — two-digit state FIPS.</summary>
    public required string StateCode { get; set; }

    /// <summary>Item 8 — structure number as published.</summary>
    public required string StructureNumber { get; set; }

    /// <summary>First vintage this structure appears in.</summary>
    public short FirstYear { get; set; }

    /// <summary>Last vintage this structure appears in.</summary>
    public short LastYear { get; set; }

    /// <summary>Years actually published — less than the span when the structure has gap years.</summary>
    public short ObservedYears { get; set; }

    /// <summary>One character per year from <see cref="FirstYear"/> to <see cref="LastYear"/>.</summary>
    public required string Ratings { get; set; }

    public long TrendRunId { get; set; }
}

/// <summary>
/// Good/Fair/Poor counts for one area in one year (FR-1.2 AC-1). Summarises exactly the rows behind
/// <see cref="BridgeConditionSeries"/>, so the two must reconcile (AC-3).
/// <para>
/// Counts are stored; shares are computed on read. A share is a division of two stored numbers and
/// storing it would only create something that can disagree with them.
/// </para>
/// </summary>
public class ConditionRollup
{
    public long Id { get; set; }

    public RollupLevel Level { get; set; }

    /// <summary>2-digit state FIPS, or 5-digit state+county FIPS, as published in that vintage.</summary>
    public required string Fips { get; set; }

    public short VintageYear { get; set; }

    public int Total { get; set; }

    public int Good { get; set; }

    public int Fair { get; set; }

    public int Poor { get; set; }

    /// <summary>Published that year with no numeric rating — a real state, not a missing year.</summary>
    public int Unknown { get; set; }

    public long TrendRunId { get; set; }
}
