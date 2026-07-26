namespace SpanSight.Core.Analytics;

/// <summary>Which of the four independent per-component matrix families a row belongs to (FR-1.3).</summary>
public enum ConditionComponent
{
    /// <summary>Item 58.</summary>
    Deck = 1,

    /// <summary>Item 59.</summary>
    Superstructure = 2,

    /// <summary>Item 60.</summary>
    Substructure = 3,

    /// <summary>Item 62.</summary>
    Culvert = 4,
}

public enum DeteriorationRunStatus
{
    Running = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// One execution of <c>tools/deterioration/build-deterioration.sh</c>, as recorded when its matrices
/// were loaded (FR-1.3 AC-1, NFR-3).
/// <para>
/// Beyond the provenance <see cref="TrendRun"/> already carries, this holds the two published
/// methodology constants — <see cref="SampleFloor"/> and <see cref="MethodologyVersion"/> — so the
/// floor a matrix was judged by and the document version it was computed under travel with the
/// numbers instead of being hardcoded separately in the SQL, the API and the UI.
/// </para>
/// </summary>
public class DeteriorationRun
{
    public long Id { get; set; }

    /// <summary>The job's own run id from <c>manifest.json</c> (e.g. <c>deterioration-20260726T214531Z</c>).</summary>
    public required string JobRunId { get; set; }

    /// <summary>SHA-256 of <c>tools/vintages/catalog.json</c> — pins which Parquet set was read.</summary>
    public required string CatalogSha256 { get; set; }

    /// <summary>Version of <c>docs/METHODOLOGY-DETERIORATION.md</c> these matrices were computed under.</summary>
    public required string MethodologyVersion { get; set; }

    /// <summary>
    /// Rows with fewer pairs than this publish no rates (methodology §4). Stored rather than assumed,
    /// because the API applies it and the UI states it.
    /// </summary>
    public int SampleFloor { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>Earliest from-year of any pair.</summary>
    public short FirstYear { get; set; }

    /// <summary>Latest to-year of any pair.</summary>
    public short LastYear { get; set; }

    /// <summary>Distinct consecutive year-pairs that contributed (33 for the full 1992–2025 set).</summary>
    public int YearPairs { get; set; }

    /// <summary>Structure-year pairs — one structure observed in two consecutive vintages.</summary>
    public long StructurePairs { get; set; }

    /// <summary>Component pairs: the sum of the four families, which is what the matrices count.</summary>
    public long ComponentPairs { get; set; }

    /// <summary>Distinct type group × material group × climate region combinations observed.</summary>
    public int Cohorts { get; set; }

    public int MatrixRows { get; set; }

    public int Cells { get; set; }

    /// <summary>How many cohort rows fall under <see cref="SampleFloor"/> — published, not inferred from an empty UI.</summary>
    public int RowsBelowFloor { get; set; }

    public DeteriorationRunStatus Status { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// One matrix row: a cohort, a component family and a from-rating, with the number of pairs that
/// started there (FR-1.3 AC-1).
/// <para>
/// The row is its own relation because it is every rate's denominator and the unit the sample-size
/// floor applies to (methodology §4). Keeping <see cref="RowTotal"/> here rather than on each cell
/// means it exists once and cannot drift from the cells beneath it.
/// </para>
/// <para>
/// Natural key: (<see cref="Component"/>, <see cref="TypeGroup"/>, <see cref="MaterialGroup"/>,
/// <see cref="Region"/>, <see cref="FromRating"/>). The national all-cohorts context matrix is the
/// same relation with <see cref="Lookups.NbiCohorts.AllCohorts"/> in all three cohort dimensions at
/// once, so it goes through the identical floor code path as every cohort.
/// </para>
/// </summary>
public class DeteriorationMatrixRow
{
    public long Id { get; set; }

    public ConditionComponent Component { get; set; }

    /// <summary>Item 43B group, "Not published", or the national sentinel.</summary>
    public required string TypeGroup { get; set; }

    /// <summary>Item 43A group, "Not published", or the national sentinel.</summary>
    public required string MaterialGroup { get; set; }

    /// <summary>NOAA climate region, "Outside contiguous U.S.", or the national sentinel.</summary>
    public required string Region { get; set; }

    /// <summary>Published rating in year y — 0–9.</summary>
    public short FromRating { get; set; }

    /// <summary>Pairs starting at <see cref="FromRating"/>; the denominator of all ten of this row's rates.</summary>
    public int RowTotal { get; set; }

    /// <summary>
    /// Earliest from-year contributing to this row. All 33 year-pairs are pooled, so the span is what
    /// keeps a pooled cell honest: a row whose evidence is entirely pre-2009 must not read as 34 years
    /// of history (methodology §4, §6.8).
    /// </summary>
    public short FirstFromYear { get; set; }

    /// <summary>Latest from-year contributing to this row.</summary>
    public short LastFromYear { get; set; }

    /// <summary>Distinct year-pairs behind this row — at most <c>LastFromYear - FirstFromYear + 1</c>.</summary>
    public short YearPairsObserved { get; set; }

    public long DeteriorationRunId { get; set; }
}

/// <summary>
/// One non-zero matrix cell: pairs that moved from a row's from-rating to <see cref="ToRating"/>
/// (FR-1.3 AC-1).
/// <para>
/// Cells are sparse — a cohort-component populates 12 of its 100 cells at the median — so "never
/// observed across 34 years" stays distinguishable from an observed zero. The API zero-fills to a
/// full 10×10 grid on read, so a reader still sees ten columns.
/// </para>
/// <para>
/// Rates are not stored. A rate is a division of two stored numbers and storing it would only create
/// something that can disagree with them — the rule <see cref="ConditionRollup"/> already follows.
/// The cohort key is repeated here rather than referencing a row id so this table upserts by natural
/// key in one pass, exactly as the FR-1.2 aggregates do.
/// </para>
/// </summary>
public class DeteriorationMatrixCell
{
    public long Id { get; set; }

    public ConditionComponent Component { get; set; }

    public required string TypeGroup { get; set; }

    public required string MaterialGroup { get; set; }

    public required string Region { get; set; }

    /// <summary>Published rating in year y — 0–9.</summary>
    public short FromRating { get; set; }

    /// <summary>Published rating in year y+1 — 0–9. Improvements are retained as observed (methodology §3.3).</summary>
    public short ToRating { get; set; }

    public int Pairs { get; set; }

    public long DeteriorationRunId { get; set; }
}
