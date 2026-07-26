using SpanSight.Core.Domain;
using SpanSight.Core.Domain.Lookups;

using StateFipsLookup = SpanSight.Core.Domain.Lookups.StateFips;

namespace SpanSight.Api;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>Lean row for lists and the map's fallback GeoJSON layer.</summary>
public sealed record BridgeSummaryDto(
    string Id,
    string State,
    string? CountyCode,
    string? FacilityCarried,
    string? FeaturesIntersected,
    double Latitude,
    double Longitude,
    int? YearBuilt,
    int? Adt,
    string? MaterialCode,
    string? DesignCode,
    string ConditionClass,
    int? LowestRating)
{
    public static BridgeSummaryDto From(Bridge b) => new(
        $"{StateFips.ByFips[b.StateCode].Abbreviation}-{b.StructureNumber}",
        StateFips.ByFips[b.StateCode].Abbreviation,
        b.CountyCode,
        b.FacilityCarried,
        b.FeaturesIntersected,
        b.Location.Y,
        b.Location.X,
        b.YearBuilt,
        b.Adt,
        b.MaterialCode,
        b.DesignCode,
        b.ConditionClass.ToString(),
        b.LowestRating);
}

public sealed record ConditionRatingDto(string? Code, string Text);

/// <summary>Full drawer payload — every published code decoded to human-readable text (FR-0.3 AC-2, GR-6 display-only).</summary>
public sealed record BridgeDetailDto(
    string Id,
    string State,
    /// <summary>2-digit state FIPS — the key half of the county FIPS the trends view needs (FR-1.2).</summary>
    string StateFips,
    string StateName,
    string StructureNumber,
    string RecordType,
    string? CountyCode,
    string? FacilityCarried,
    string? FeaturesIntersected,
    string? LocationText,
    double Latitude,
    double Longitude,
    int? YearBuilt,
    int? AgeYears,
    int? Adt,
    string? MaterialCode,
    string Material,
    string? DesignCode,
    string Design,
    decimal? StructureLengthMeters,
    ConditionRatingDto Deck,
    ConditionRatingDto Superstructure,
    ConditionRatingDto Substructure,
    ConditionRatingDto Culvert,
    int? LowestRating,
    string ConditionClass,
    string SourceFormat,
    int SnapshotYear)
{
    public static BridgeDetailDto From(Bridge b, int currentYear)
    {
        // The StateFips *property* above shadows the StateFips lookup class inside this record, so
        // the lookup is reached through its alias here.
        var state = StateFipsLookup.ByFips[b.StateCode];
        return new BridgeDetailDto(
            $"{state.Abbreviation}-{b.StructureNumber}",
            state.Abbreviation,
            state.Fips,
            state.Name,
            b.StructureNumber,
            b.RecordType,
            b.CountyCode,
            b.FacilityCarried,
            b.FeaturesIntersected,
            b.LocationText,
            b.Location.Y,
            b.Location.X,
            b.YearBuilt,
            b.YearBuilt is { } year ? currentYear - year : null,
            b.Adt,
            b.MaterialCode,
            NbiCodes.DecodeMaterial(b.MaterialCode),
            b.DesignCode,
            NbiCodes.DecodeDesign(b.DesignCode),
            b.StructureLengthMeters,
            new ConditionRatingDto(b.DeckCondition, NbiCodes.DecodeConditionRating(b.DeckCondition)),
            new ConditionRatingDto(b.SuperstructureCondition, NbiCodes.DecodeConditionRating(b.SuperstructureCondition)),
            new ConditionRatingDto(b.SubstructureCondition, NbiCodes.DecodeConditionRating(b.SubstructureCondition)),
            new ConditionRatingDto(b.CulvertCondition, NbiCodes.DecodeConditionRating(b.CulvertCondition)),
            b.LowestRating,
            b.ConditionClass.ToString(),
            b.SourceFormat.ToString(),
            b.SnapshotYear);
    }
}

/// <summary>One year of a bridge's published condition history (FR-1.2 AC-2).</summary>
public sealed record ConditionPointDto(int Year, int? LowestRating, string ConditionClass);

/// <summary>
/// A structure's condition history as published, 1992–2025 (FR-1.2 AC-2).
/// <para>
/// <see cref="Points"/> holds only the years FHWA actually published this structure, so a gap in
/// the span shows up as a missing year rather than an invented value — <see cref="ObservedYears"/>
/// against the <see cref="FirstYear"/>–<see cref="LastYear"/> span says how much is missing. This
/// is a record of published ratings, not an assessment or a projection (GR-6).
/// </para>
/// </summary>
public sealed record BridgeHistoryDto(
    string Id,
    string State,
    string StructureNumber,
    int FirstYear,
    int LastYear,
    int ObservedYears,
    IReadOnlyList<ConditionPointDto> Points,
    string Method,
    TrendProvenanceDto Provenance);

/// <summary>Which offline job produced the figures, so any number on screen is traceable (NFR-3).</summary>
public sealed record TrendProvenanceDto(string JobRunId, string CatalogSha256, DateTimeOffset? PublishedUtc);

/// <summary>Good/Fair/Poor counts and shares for one area in one year (FR-1.2 AC-2).</summary>
public sealed record TrendPointDto(
    int Year,
    int Total,
    int Good,
    int Fair,
    int Poor,
    int Unknown,
    double? GoodShare,
    double? FairShare,
    double? PoorShare,
    double? UnknownShare);

/// <summary>
/// County or state condition shares over time (FR-1.2 AC-2). Shares are computed from the stored
/// counts on read, so the two can never disagree; a year with no rows is absent rather than zero.
/// </summary>
public sealed record TrendSeriesDto(
    string Level,
    string Fips,
    string Name,
    int FromYear,
    int ToYear,
    IReadOnlyList<TrendPointDto> Points,
    string Method,
    TrendProvenanceDto? Provenance);

public sealed record StatsSummaryDto(
    int Total,
    IReadOnlyDictionary<string, int> ByCondition,
    double? PercentPoor,
    int? MedianAge,
    int? AverageAdt);

public sealed record ReasonCountDto(string Reason, int Count);

public sealed record StateCountDto(string StateCode, string State, int Count);

public sealed record IngestionRunDto(
    long Id,
    string SourceFile,
    int SnapshotYear,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    string Status,
    int RowsRead,
    int RowsLoaded,
    int RowsQuarantined,
    double RejectRate);

public sealed record QaSummaryDto(
    IngestionRunDto? LatestRun,
    IReadOnlyList<ReasonCountDto> ByReason,
    IReadOnlyList<StateCountDto> ByState);

public sealed record LookupsDto(
    IReadOnlyList<LookupsDto.StateDto> States,
    IReadOnlyDictionary<string, string> Materials,
    IReadOnlyDictionary<string, string> Designs,
    IReadOnlyDictionary<string, string> ConditionRatings,
    IReadOnlyList<string> ConditionClasses)
{
    public sealed record StateDto(string Fips, string Abbreviation, string Name);
}

public sealed record NlQueryRequestDto(string Text);

/// <summary>
/// FR-AI.1 response: the validated predicate in the filter rail's own shape (the SPA applies it
/// directly to its FilterState), the code-rendered interpretation shown for correction, and any
/// request fragments the filter cannot express.
/// </summary>
public sealed record NlQueryResponseDto(
    NlQueryResponseDto.FilterDto Filter,
    string Interpretation,
    IReadOnlyList<string> Unsupported)
{
    public sealed record FilterDto(
        string? State,
        IReadOnlyList<string> Conditions,
        IReadOnlyList<string> TypeGroups,
        int? YearBuiltMax,
        int? MinAdt);

    public static NlQueryResponseDto From(SpanSight.Core.Ai.NlFilterResult result) => new(
        new FilterDto(
            result.Applied.State,
            result.Applied.Conditions ?? [],
            result.Applied.TypeGroups ?? [],
            result.Applied.YearBuiltMax,
            result.Applied.MinAdt),
        result.Interpretation,
        result.Applied.Unsupported ?? []);
}
