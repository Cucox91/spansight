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
/// Structure-type groups as the filter rail presents them, for the FR-AI.1 translator — a
/// case-insensitive projection of <see cref="SpanSight.Core.Domain.Lookups.NbiCohorts.TypeCodesByGroup"/>,
/// which is the one C# definition of the item-43B grouping. Model output arrives with unpredictable
/// casing, so lookups here ignore case while the canonical spelling comes from the group key.
/// <para>
/// <c>web/src/state/filters.ts</c> <c>TYPE_GROUPS</c> is the TypeScript copy the SPA needs; the two
/// are held together by a parity test (FR-1.3 made a third reader of this rule, so it stopped being
/// a comment and became a test).
/// </para>
/// </summary>
public static class NlTypeGroups
{
    public static readonly IReadOnlyDictionary<string, string[]> DesignCodesByGroup =
        SpanSight.Core.Domain.Lookups.NbiCohorts.TypeCodesByGroup.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.OrdinalIgnoreCase);
}
