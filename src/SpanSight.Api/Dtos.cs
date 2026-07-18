using SpanSight.Core.Domain;
using SpanSight.Core.Domain.Lookups;

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
        var state = StateFips.ByFips[b.StateCode];
        return new BridgeDetailDto(
            $"{state.Abbreviation}-{b.StructureNumber}",
            state.Abbreviation,
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
