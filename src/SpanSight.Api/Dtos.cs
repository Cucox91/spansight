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

/// <summary>Which offline deterioration job produced the matrices, and under which methodology (FR-1.3, NFR-3).</summary>
public sealed record DeteriorationProvenanceDto(
    string JobRunId,
    string CatalogSha256,
    DateTimeOffset? PublishedUtc,
    int FirstYear,
    int LastYear,
    int YearPairs,
    long ComponentPairs);

/// <summary>
/// A published NBI code and its decoded label. Cohort groups collapse several codes, so every group
/// ships its members: "Other" is honest about containing aluminium and cast iron rather than leaving
/// the reader to assume it means "miscellaneous" (methodology §5).
/// </summary>
public sealed record CohortCodeDto(string Code, string Label);

/// <summary>One cohort dimension value together with the published codes it collapses.</summary>
public sealed record CohortGroupDto(string Name, IReadOnlyList<CohortCodeDto> Codes);

/// <summary>The vocabulary of every cohort dimension, so a view can label a matrix without hardcoding groups.</summary>
public sealed record DeteriorationDimensionsDto(
    IReadOnlyList<string> Components,
    IReadOnlyList<CohortGroupDto> TypeGroups,
    IReadOnlyList<CohortGroupDto> MaterialGroups,
    IReadOnlyList<string> Regions);

/// <summary>How many pairs one cohort holds in one component family, and how many of its rows clear the floor.</summary>
public sealed record CohortComponentDto(string Component, int Pairs, int Rows, int RowsAboveFloor);

/// <summary>
/// One cohort in the published matrices (FR-1.3 AC-1). <see cref="IsNational"/> marks the
/// all-cohorts context matrix, which is the exact sum of every cohort including the
/// "Outside contiguous U.S." and "Not published" buckets.
/// </summary>
public sealed record DeteriorationCohortDto(
    string TypeGroup,
    string MaterialGroup,
    string Region,
    bool IsNational,
    IReadOnlyList<CohortComponentDto> Components);

/// <summary>The cohort inventory a Patterns view picks from, plus the dimension vocabulary.</summary>
public sealed record DeteriorationCatalogDto(
    DeteriorationDimensionsDto Dimensions,
    IReadOnlyList<DeteriorationCohortDto> Cohorts,
    int SampleFloor,
    string Method,
    string MethodologyVersion,
    string MethodologyUrl,
    DeteriorationProvenanceDto? Provenance);

/// <summary>
/// One cell of a transition matrix (FR-1.3 AC-1).
/// <para>
/// <see cref="Rate"/> is null whenever the containing row is below the sample-size floor — the API
/// never emits a rate a view could render as a percentage for evidence too thin to support one
/// (AC-3). <see cref="Pairs"/> is always present: suppression removes the rate, never the evidence.
/// </para>
/// </summary>
public sealed record DeteriorationCellDto(int ToRating, int Pairs, double? Rate);

/// <summary>
/// One row of a transition matrix: every pair that started at <see cref="FromRating"/> (FR-1.3 AC-1).
/// <para>
/// The row is the unit the floor applies to, because <see cref="RowTotal"/> is the denominator of all
/// ten of its rates. <see cref="Sufficient"/> false means the view must render "insufficient data"
/// rather than a number. All ten to-ratings are present whether observed or not, so a reader can tell
/// an observed zero from a cell the 34 years never produced.
/// </para>
/// <para>
/// The span (<see cref="FirstFromYear"/>–<see cref="LastFromYear"/> over
/// <see cref="YearPairsObserved"/> year-pairs) travels with the row because all 33 year-pairs are
/// pooled: without it a cell whose evidence is entirely pre-2009 coding practice would read as 34
/// years of history (methodology §4, §6.8).
/// </para>
/// </summary>
public sealed record DeteriorationRowDto(
    int FromRating,
    int RowTotal,
    bool Sufficient,
    int? FirstFromYear,
    int? LastFromYear,
    int YearPairsObserved,
    IReadOnlyList<DeteriorationCellDto> Cells);

/// <summary>
/// A cohort's transition matrix for one component family (FR-1.3 AC-1/AC-4).
/// <para>
/// A descriptive count of how often published ratings moved between consecutive NBI vintages in this
/// cohort — never a prediction, never a statement about any individual structure (GR-6). The method
/// note, the methodology version and its link travel with every response so no view can render a
/// matrix without them.
/// </para>
/// </summary>
public sealed record DeteriorationMatrixDto(
    string Component,
    DeteriorationCohortDto Cohort,
    IReadOnlyList<DeteriorationRowDto> Rows,
    int Pairs,
    int SampleFloor,
    int RowsBelowFloor,
    double? UnchangedShare,
    string Method,
    string CadenceCaption,
    string MethodologyVersion,
    string MethodologyUrl,
    DeteriorationProvenanceDto? Provenance);

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
    IReadOnlyList<StateCountDto> ByState,
    CountyJoinCoverageDto? CountyJoin);

// FR-1.5 AC-2 — the bridge→county join coverage, published on the QA page beside the ingestion
// rejects because it is the same kind of fact: a measured share of the served inventory that the
// product could not place, itemised rather than rounded away.

/// <summary>
/// How many quarantined misses share one reason, and how far from a county they fell.
/// <c>NearestDistanceMeters</c> is null where no county lay inside the job's bounded search radius —
/// "not looked far enough to say", which is a different fact from "no county nearby".
/// </summary>
public sealed record CountyJoinMissReasonDto(
    string Reason,
    int Rows,
    int Structures,
    long? MedianDistanceMeters,
    long? MaxDistanceMeters);

/// <summary>
/// One (published county code → containing polygon) pair that disagrees.
/// <c>NbiFipsInTiger</c> false is the fact that explains most disagreement — the published code
/// names a county the boundary file no longer carries. Null when no code was published at all, since
/// the question does not apply.
/// <para>
/// <c>Rows</c> counts every served row taking this path; <c>Structures</c> counts the record-type-1
/// subset. Both, because they differ by nearly a fifth and a label that says "structures" over the
/// first number is simply wrong.
/// </para>
/// </summary>
public sealed record CountyJoinDisagreementDto(
    string? NbiCountyFips,
    string? NbiCountyName,
    string CountyFips,
    string CountyName,
    string Kind,
    int Rows,
    int Structures,
    bool? NbiFipsInTiger);

public sealed record CountyJoinProvenanceDto(
    string JobRunId,
    string CatalogSha256,
    string MethodVersion,
    string ContainmentPredicate,
    DateTimeOffset? CompletedUtc);

/// <summary>
/// FR-1.5 AC-2: the share of bridges matched to a county polygon, with the misses itemised.
/// <para>
/// Two denominators are published, not one. <see cref="Structures"/> counts NBI record type 1 — the
/// structure itself, and what "bridge" means in FR-1.2 and FR-1.3 — while <see cref="Bridges"/>
/// counts every row the serving table holds, including the route records published *under* a
/// structure. Quoting only the first would understate what was measured; only the second would
/// answer a different question than AC-2 asks.
/// </para>
/// </summary>
public sealed record CountyJoinCoverageDto(
    long Bridges,
    long Matched,
    long Unmatched,
    double CoveragePercent,
    long Structures,
    long StructuresMatched,
    double StructureCoveragePercent,
    long Agree,
    double? AgreePercent,
    long DifferentCountySameState,
    long DifferentState,
    long CountyNotPublished,
    int Counties,
    int CountiesWithoutPopulation,
    IReadOnlyList<CountyJoinMissReasonDto> MissesByReason,
    IReadOnlyList<CountyJoinDisagreementDto> LargestDisagreements,
    long RowsUnderRetiredCodes,
    long StructuresUnderRetiredCodes,
    string MethodNote,
    CountyJoinProvenanceDto Provenance);

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
