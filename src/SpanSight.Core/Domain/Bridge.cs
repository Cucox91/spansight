using NetTopologySuite.Geometries;

namespace SpanSight.Core.Domain;

/// <summary>
/// Canonical serving record for one NBI structure (ARCHITECTURE §4.1 <c>core</c> layer).
/// Holds the latest loaded vintage; 30-year history lives in Parquet (ADR-005).
/// Natural key: (<see cref="StateCode"/>, <see cref="StructureNumber"/>, <see cref="RecordType"/>).
/// </summary>
public class Bridge
{
    public long Id { get; set; }

    /// <summary>Item 1 — two-digit state FIPS code (e.g. "12" = Florida).</summary>
    public required string StateCode { get; set; }

    /// <summary>Item 8 — structure number, trimmed of surrounding whitespace, as published.</summary>
    public required string StructureNumber { get; set; }

    /// <summary>Item 5A — record type ("1" = route on structure). Part of the natural key.</summary>
    public required string RecordType { get; set; }

    /// <summary>Item 3 — three-digit county FIPS code within the state.</summary>
    public string? CountyCode { get; set; }

    /// <summary>Item 6A — features intersected (free text as published).</summary>
    public string? FeaturesIntersected { get; set; }

    /// <summary>Item 7 — facility carried by the structure (free text as published).</summary>
    public string? FacilityCarried { get; set; }

    /// <summary>Item 9 — location description (free text as published).</summary>
    public string? LocationText { get; set; }

    /// <summary>WGS84 point decoded from items 16/17 (DMS). Always present in core; rejects are quarantined (FR-0.2).</summary>
    public required Point Location { get; set; }

    /// <summary>Item 27 — year built.</summary>
    public int? YearBuilt { get; set; }

    /// <summary>Item 29 — average daily traffic.</summary>
    public int? Adt { get; set; }

    /// <summary>Item 43A — kind of material/design code ("1"–"0").</summary>
    public string? MaterialCode { get; set; }

    /// <summary>Item 43B — type of design/construction code ("01"–"22", "00").</summary>
    public string? DesignCode { get; set; }

    /// <summary>Item 49 — structure length in meters, as published.</summary>
    public decimal? StructureLengthMeters { get; set; }

    /// <summary>Item 58 — deck condition rating ("0"–"9", "N" = not applicable).</summary>
    public string? DeckCondition { get; set; }

    /// <summary>Item 59 — superstructure condition rating.</summary>
    public string? SuperstructureCondition { get; set; }

    /// <summary>Item 60 — substructure condition rating.</summary>
    public string? SubstructureCondition { get; set; }

    /// <summary>Item 62 — culvert condition rating.</summary>
    public string? CulvertCondition { get; set; }

    /// <summary>Lowest applicable numeric rating across items 58/59/60/62; null when none are numeric.</summary>
    public int? LowestRating { get; set; }

    /// <summary>Good/Fair/Poor derived from <see cref="LowestRating"/> — display-only decoding (GR-6).</summary>
    public ConditionClass ConditionClass { get; set; }

    /// <summary>Which source era produced this row (Coding Guide vs SNBI crosswalk).</summary>
    public SourceFormat SourceFormat { get; set; }

    /// <summary>NBI snapshot vintage this row was last refreshed from (e.g. 2025).</summary>
    public int SnapshotYear { get; set; }
}
