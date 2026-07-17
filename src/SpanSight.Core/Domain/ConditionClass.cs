namespace SpanSight.Core.Domain;

/// <summary>
/// FHWA Good/Fair/Poor classification derived from the lowest applicable NBI condition rating
/// (items 58/59/60, or 62 for culverts). Display-only decoding of published values (GR-6).
/// </summary>
public enum ConditionClass
{
    /// <summary>No numeric condition rating present on the record.</summary>
    Unknown = 0,

    /// <summary>Lowest applicable rating ≥ 7.</summary>
    Good = 1,

    /// <summary>Lowest applicable rating 5–6.</summary>
    Fair = 2,

    /// <summary>Lowest applicable rating ≤ 4.</summary>
    Poor = 3,
}
