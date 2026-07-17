namespace SpanSight.Core.Ingestion;

/// <summary>
/// Column names of the FHWA NBI delimited snapshot (Coding Guide era). Names follow the
/// published delimited-file convention <c>NAME_ITEMNUMBER</c>; matching is case-insensitive
/// and unknown columns are ignored so minor vintage drift does not break the parser.
/// </summary>
public static class NbiColumns
{
    public const string StateCode = "STATE_CODE_001";
    public const string CountyCode = "COUNTY_CODE_003";
    public const string RecordType = "RECORD_TYPE_005A";
    public const string FeaturesDescription = "FEATURES_DESC_006A";
    public const string FacilityCarried = "FACILITY_CARRIED_007";
    public const string StructureNumber = "STRUCTURE_NUMBER_008";
    public const string Location = "LOCATION_009";
    public const string Latitude = "LAT_016";
    public const string Longitude = "LONG_017";
    public const string YearBuilt = "YEAR_BUILT_027";
    public const string Adt = "ADT_029";
    public const string StructureKind = "STRUCTURE_KIND_043A";
    public const string StructureType = "STRUCTURE_TYPE_043B";
    public const string StructureLengthMeters = "STRUCTURE_LEN_MT_049";
    public const string DeckCondition = "DECK_COND_058";
    public const string SuperstructureCondition = "SUPERSTRUCTURE_COND_059";
    public const string SubstructureCondition = "SUBSTRUCTURE_COND_060";
    public const string CulvertCondition = "CULVERT_COND_062";

    /// <summary>Columns the parser refuses to run without (everything else degrades gracefully).</summary>
    public static readonly IReadOnlyList<string> Required =
    [
        StateCode,
        StructureNumber,
        Latitude,
        Longitude,
    ];
}
