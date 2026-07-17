namespace SpanSight.Core.Ingestion.Validation;

/// <summary>
/// Machine-readable reason codes stored on quarantine rows (FR-0.2 AC-2) and aggregated by the
/// QA report. Stable strings — treat like an API contract; add, never rename.
/// </summary>
public static class QuarantineReasons
{
    public const string StructuralFault = "row_structural_fault";
    public const string UnknownStateCode = "unknown_state_code";
    public const string StructureNumberInvalid = "structure_number_invalid";
    public const string CoordinateMissingOrZero = "coordinate_missing_or_zero";
    public const string CoordinateInvalid = "coordinate_invalid";
    public const string CoordinateOutsideState = "coordinate_outside_state";
    public const string YearBuiltImpossible = "year_built_impossible";
    public const string AdtInvalid = "adt_invalid";
    public const string StructureLengthInvalid = "structure_length_invalid";
    public const string ConditionCodeInvalid = "condition_code_invalid";
    public const string DuplicateKey = "duplicate_key";
}
