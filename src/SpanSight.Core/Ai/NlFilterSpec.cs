namespace SpanSight.Core.Ai;

/// <summary>
/// The schema-constrained shape the FR-AI.1 model call is allowed to produce (ADR-008 §2):
/// exactly the filter rail's predicate — conditions, state, structure-type groups, built-before
/// year, minimum traffic — plus a list of request fragments the filter cannot express.
/// The model can only say what the filter form could say; everything else lands in
/// <see cref="Unsupported"/> and is surfaced for correction, never silently dropped.
/// </summary>
public sealed record NlFilterSpec(
    string? State,
    IReadOnlyList<string>? Conditions,
    IReadOnlyList<string>? TypeGroups,
    int? YearBuiltMax,
    int? MinAdt,
    IReadOnlyList<string>? Unsupported);

/// <summary>
/// Structure-type groups as the filter rail presents them. Mirrors <c>TYPE_GROUPS</c> in
/// <c>web/src/state/filters.ts</c> — keep the two in sync (item 43B design codes).
/// </summary>
public static class NlTypeGroups
{
    public static readonly IReadOnlyDictionary<string, string[]> DesignCodesByGroup =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Girder / Stringer"] = ["01", "02", "03", "04", "05", "06", "21", "22"],
            ["Truss / Arch"] = ["09", "10", "11", "12", "13", "14"],
            ["Culvert"] = ["19"],
            ["Other"] = ["00", "07", "08", "15", "16", "17", "18", "20"],
        };
}
