namespace SpanSight.Core.Vintages;

/// <summary>
/// The normalized superset schema for NBI annual vintages (FR-1.1 AC-1).
/// <para>
/// A column exists here if it appeared in <b>any</b> sampled vintage; a vintage that lacks it
/// converts to NULL rather than to a narrower file, so every vintage Parquet has identical
/// columns in identical order and DuckDB can read the whole catalog as one relation (AC-4).
/// </para>
/// <para>
/// The list is deliberately explicit rather than discovered at run time: an unknown column in a
/// source file is a schema change that deserves a human decision, so
/// <see cref="VintageHeader.Bind"/> fails loudly instead of silently dropping it.
/// Verified 2026-07-26 against the real FHWA national files for all 34 vintages, 1992–2025:
/// five distinct published layouts, and exactly three columns (<c>CAT10</c>, <c>CAT23</c>,
/// <c>CAT29</c>) that the 1992/2010/2025 sample had not seen.
/// </para>
/// </summary>
public static class VintageSchema
{
    /// <summary>Every column, in canonical output order (newest-era layout first, older-era extras appended).</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        "STATE_CODE_001",
        "STRUCTURE_NUMBER_008",
        "RECORD_TYPE_005A",
        "ROUTE_PREFIX_005B",
        "SERVICE_LEVEL_005C",
        "ROUTE_NUMBER_005D",
        "DIRECTION_005E",
        "HIGHWAY_DISTRICT_002",
        "COUNTY_CODE_003",
        "PLACE_CODE_004",
        "FEATURES_DESC_006A",
        "CRITICAL_FACILITY_006B",
        "FACILITY_CARRIED_007",
        "LOCATION_009",
        "MIN_VERT_CLR_010",
        "KILOPOINT_011",
        "BASE_HWY_NETWORK_012",
        "LRS_INV_ROUTE_013A",
        "SUBROUTE_NO_013B",
        "LAT_016",
        "LONG_017",
        "DETOUR_KILOS_019",
        "TOLL_020",
        "MAINTENANCE_021",
        "OWNER_022",
        "FUNCTIONAL_CLASS_026",
        "YEAR_BUILT_027",
        "TRAFFIC_LANES_ON_028A",
        "TRAFFIC_LANES_UND_028B",
        "ADT_029",
        "YEAR_ADT_030",
        "DESIGN_LOAD_031",
        "APPR_WIDTH_MT_032",
        "MEDIAN_CODE_033",
        "DEGREES_SKEW_034",
        "STRUCTURE_FLARED_035",
        "RAILINGS_036A",
        "TRANSITIONS_036B",
        "APPR_RAIL_036C",
        "APPR_RAIL_END_036D",
        "HISTORY_037",
        "NAVIGATION_038",
        "NAV_VERT_CLR_MT_039",
        "NAV_HORR_CLR_MT_040",
        "OPEN_CLOSED_POSTED_041",
        "SERVICE_ON_042A",
        "SERVICE_UND_042B",
        "STRUCTURE_KIND_043A",
        "STRUCTURE_TYPE_043B",
        "APPR_KIND_044A",
        "APPR_TYPE_044B",
        "MAIN_UNIT_SPANS_045",
        "APPR_SPANS_046",
        "HORR_CLR_MT_047",
        "MAX_SPAN_LEN_MT_048",
        "STRUCTURE_LEN_MT_049",
        "LEFT_CURB_MT_050A",
        "RIGHT_CURB_MT_050B",
        "ROADWAY_WIDTH_MT_051",
        "DECK_WIDTH_MT_052",
        "VERT_CLR_OVER_MT_053",
        "VERT_CLR_UND_REF_054A",
        "VERT_CLR_UND_054B",
        "LAT_UND_REF_055A",
        "LAT_UND_MT_055B",
        "LEFT_LAT_UND_MT_056",
        "DECK_COND_058",
        "SUPERSTRUCTURE_COND_059",
        "SUBSTRUCTURE_COND_060",
        "CHANNEL_COND_061",
        "CULVERT_COND_062",
        "OPR_RATING_METH_063",
        "OPERATING_RATING_064",
        "INV_RATING_METH_065",
        "INVENTORY_RATING_066",
        "STRUCTURAL_EVAL_067",
        "DECK_GEOMETRY_EVAL_068",
        "UNDCLRENCE_EVAL_069",
        "POSTING_EVAL_070",
        "WATERWAY_EVAL_071",
        "APPR_ROAD_EVAL_072",
        "WORK_PROPOSED_075A",
        "WORK_DONE_BY_075B",
        "IMP_LEN_MT_076",
        "DATE_OF_INSPECT_090",
        "INSPECT_FREQ_MONTHS_091",
        "FRACTURE_092A",
        "UNDWATER_LOOK_SEE_092B",
        "SPEC_INSPECT_092C",
        "FRACTURE_LAST_DATE_093A",
        "UNDWATER_LAST_DATE_093B",
        "SPEC_LAST_DATE_093C",
        "BRIDGE_IMP_COST_094",
        "ROADWAY_IMP_COST_095",
        "TOTAL_IMP_COST_096",
        "YEAR_OF_IMP_097",
        "OTHER_STATE_CODE_098A",
        "OTHER_STATE_PCNT_098B",
        "OTHR_STATE_STRUC_NO_099",
        "STRAHNET_HIGHWAY_100",
        "PARALLEL_STRUCTURE_101",
        "TRAFFIC_DIRECTION_102",
        "TEMP_STRUCTURE_103",
        "HIGHWAY_SYSTEM_104",
        "FEDERAL_LANDS_105",
        "YEAR_RECONSTRUCTED_106",
        "DECK_STRUCTURE_TYPE_107",
        "SURFACE_TYPE_108A",
        "MEMBRANE_TYPE_108B",
        "DECK_PROTECTION_108C",
        "PERCENT_ADT_TRUCK_109",
        "NATIONAL_NETWORK_110",
        "PIER_PROTECTION_111",
        "BRIDGE_LEN_IND_112",
        "SCOUR_CRITICAL_113",
        "FUTURE_ADT_114",
        "YEAR_OF_FUTURE_ADT_115",
        "MIN_NAV_CLR_MT_116",
        "FED_AGENCY",
        "SUBMITTED_BY",
        "BRIDGE_CONDITION",
        "LOWEST_RATING",
        "DECK_AREA",
        "DATE_LAST_UPDATE",
        "TYPE_LAST_UPDATE",
        "DEDUCT_CODE",
        "REMARKS",
        "PROGRAM_CODE",
        "PROJ_NO",
        "PROJ_SUFFIX",
        "NBI_TYPE_OF_IMP",
        "DTL_TYPE_OF_IMP",
        "SPECIAL_CODE",
        "STEP_CODE",
        "STATUS",
        "SUFFICIENCY_ASTERC",
        "SUFFICIENCY_RATING",
        "STATUS_WITH_10YR_RULE",
        "STATUS_NO_10YR_RULE",

        // FHWA's computed "category" fields, published only in 2016 (CAT10) and 2017–2018 (all three).
        // They are the same three quantities the 2019+ layout publishes under readable names, which
        // was verified against the real files rather than assumed (see VintageCatColumns).
        "CAT10",
        "CAT23",
        "CAT29",
    ];

    /// <summary>
    /// The 2016–2018 computed columns and the 2019+ column each one is the predecessor of.
    /// <para>
    /// FHWA published these three under opaque names before the performance-measures layout gave
    /// them readable ones. They are carried as themselves — the vintage Parquet is a faithful copy
    /// of published text, so nothing is silently renamed into <c>BRIDGE_CONDITION</c> — and the
    /// DuckDB catalog (<c>tools/vintages/catalog.sql</c>) is where the two names are coalesced into
    /// one continuous 2016–2025 series, deliberately and visibly.
    /// </para>
    /// <para>
    /// Equivalence verified 2026-07-26 against the real 2017/2018/2019 national files:
    /// <c>CAT10</c> agreed with FHWA's Good/Fair/Poor rule over items 58/59/60/62 on 299,947 of
    /// 299,947 rows carrying condition data; <c>CAT23</c> agreed with the minimum of those same four
    /// items on 299,947 of 299,947; <c>CAT29</c> matched <c>DECK_AREA</c>'s distribution (identical
    /// maximum, 284,739) at a median ratio of exactly 1.0000 to
    /// <c>STRUCTURE_LEN_MT_049 × DECK_WIDTH_MT_052</c>, so it carries the same square metres.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CatColumnSuccessors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CAT10"] = "BRIDGE_CONDITION",
            ["CAT23"] = "LOWEST_RATING",
            ["CAT29"] = "DECK_AREA",
        };

    /// <summary>Columns the converter refuses to run without — identity and provenance keys.</summary>
    public static readonly IReadOnlyList<string> Required =
    [
        StateCode,
        StructureNumber,
    ];

    public const string StateCode = "STATE_CODE_001";
    public const string StructureNumber = "STRUCTURE_NUMBER_008";
    public const string RecordType = "RECORD_TYPE_005A";

    /// <summary>
    /// The four condition items the Phase 0 <c>ConditionClassifier</c> consumes. FR-1.2 replays that
    /// classifier over these, so they are carried as first-class typed columns, never text.
    /// </summary>
    public static readonly IReadOnlyList<string> ConditionItems =
    [
        "DECK_COND_058",
        "SUPERSTRUCTURE_COND_059",
        "SUBSTRUCTURE_COND_060",
        "CULVERT_COND_062",
    ];

    /// <summary>Provenance columns the converter adds to every row (not present in any source file).</summary>
    public static readonly IReadOnlyList<string> ProvenanceColumns =
    [
        "VINTAGE_YEAR",
        "SOURCE_FILE",
        "SOURCE_SHA256",
        "SOURCE_ROW",
    ];

    public static bool IsKnown(string column) => Lookup.Contains(column);

    private static readonly HashSet<string> Lookup = new(Columns, StringComparer.OrdinalIgnoreCase);
}
