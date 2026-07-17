namespace SpanSight.Core.Data;

/// <summary>
/// A source row rejected by validation (FR-0.2): verbatim raw line + machine-readable reasons,
/// tied to the ingestion run that produced it. Feeds the QA report (UC-0.6).
/// </summary>
public class QuarantineRow
{
    public long Id { get; set; }

    public long IngestionRunId { get; set; }

    public IngestionRun? IngestionRun { get; set; }

    /// <summary>1-based physical line number in the source file.</summary>
    public int LineNumber { get; set; }

    /// <summary>State FIPS if the row got far enough to have one (aggregation key for the QA report).</summary>
    public string? StateCode { get; set; }

    public string? StructureNumber { get; set; }

    /// <summary>Reason codes from <see cref="Ingestion.Validation.QuarantineReasons"/> (Postgres text[]).</summary>
    public List<string> Reasons { get; set; } = [];

    /// <summary>The raw source line, verbatim.</summary>
    public required string RawLine { get; set; }
}
