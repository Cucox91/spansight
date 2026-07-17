namespace SpanSight.Core.Data;

public enum IngestionRunStatus
{
    Running = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// One execution of the ingestion pipeline (FR-0.1): the run summary that idempotency checks,
/// the QA report, and observability metrics all read from.
/// </summary>
public class IngestionRun
{
    public long Id { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>File name only — never a local path (keeps machine details out of the DB).</summary>
    public required string SourceFile { get; set; }

    /// <summary>SHA-256 of the source file; the idempotency key (FR-0.1 AC-2).</summary>
    public required string SourceSha256 { get; set; }

    public int SnapshotYear { get; set; }

    public int RowsRead { get; set; }

    public int RowsLoaded { get; set; }

    public int RowsQuarantined { get; set; }

    public IngestionRunStatus Status { get; set; }

    public string? Error { get; set; }
}
