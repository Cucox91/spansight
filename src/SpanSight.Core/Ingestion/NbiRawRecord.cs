namespace SpanSight.Core.Ingestion;

/// <summary>
/// One structurally-parsed row of an NBI delimited snapshot: fields extracted and trimmed,
/// no semantic validation applied yet (that is <see cref="Validation.BridgeRowValidator"/>'s job).
/// </summary>
public sealed record NbiRawRecord
{
    public required string StateCode { get; init; }

    public required string StructureNumber { get; init; }

    /// <summary>Item 5A; defaults to "1" (route on structure) when the column is absent/blank.</summary>
    public string RecordType { get; init; } = "1";

    public string? CountyCode { get; init; }

    public string? FeaturesDescription { get; init; }

    public string? FacilityCarried { get; init; }

    public string? LocationText { get; init; }

    public string? RawLatitude { get; init; }

    public string? RawLongitude { get; init; }

    public string? RawYearBuilt { get; init; }

    public string? RawAdt { get; init; }

    public string? MaterialCode { get; init; }

    public string? DesignCode { get; init; }

    public string? RawStructureLengthMeters { get; init; }

    public string? DeckCondition { get; init; }

    public string? SuperstructureCondition { get; init; }

    public string? SubstructureCondition { get; init; }

    public string? CulvertCondition { get; init; }
}
