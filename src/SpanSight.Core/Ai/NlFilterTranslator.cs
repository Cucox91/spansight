using SpanSight.Core.Domain.Lookups;
using SpanSight.Core.Filtering;

namespace SpanSight.Core.Ai;

/// <summary>
/// Deterministic half of FR-AI.1: takes the model's schema-constrained <see cref="NlFilterSpec"/>
/// and funnels it through <see cref="BridgeFilter.TryCreate"/> — the same validation path as
/// hand-typed filters (ADR-008 §2). Fails closed: any field the validator rejects is dropped and
/// reported, never applied. The interpretation string is rendered here from the validated values,
/// so nothing model-authored is ever displayed verbatim (GR-6).
/// </summary>
public static class NlFilterTranslator
{
    public static NlFilterResult Translate(NlFilterSpec spec)
    {
        var notes = new List<string>(spec.Unsupported ?? []);

        var state = spec.State;
        if (state is not null && StateFips.Resolve(state) is null)
        {
            notes.Add($"unknown state '{state}'");
            state = null;
        }

        var conditions = new List<string>();
        foreach (var condition in spec.Conditions ?? [])
        {
            if (condition is "Good" or "Fair" or "Poor")
            {
                if (!conditions.Contains(condition))
                {
                    conditions.Add(condition);
                }
            }
            else
            {
                notes.Add($"unknown condition '{condition}'");
            }
        }

        var typeGroups = new List<string>();
        var designCodes = new List<string>();
        foreach (var group in spec.TypeGroups ?? [])
        {
            if (NlTypeGroups.DesignCodesByGroup.TryGetValue(group.Trim(), out var codes))
            {
                var canonical = NlTypeGroups.DesignCodesByGroup.Keys.First(
                    k => string.Equals(k, group.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!typeGroups.Contains(canonical))
                {
                    typeGroups.Add(canonical);
                    designCodes.AddRange(codes);
                }
            }
            else
            {
                notes.Add($"unknown structure type '{group}'");
            }
        }

        var yearBuiltMax = spec.YearBuiltMax;
        if (yearBuiltMax is < 1600 or > 2100)
        {
            notes.Add($"implausible year '{yearBuiltMax}'");
            yearBuiltMax = null;
        }

        var minAdt = spec.MinAdt;
        if (minAdt is < 0)
        {
            notes.Add($"invalid traffic floor '{minAdt}'");
            minAdt = null;
        }

        // The one validated funnel — identical to a hand-typed query string (FR-0.3 AC-4).
        if (!BridgeFilter.TryCreate(
                state, county: null, conditions, materials: null, designCodes,
                yearBuiltMin: null, yearBuiltMax, minAdt, bbox: null,
                out var filter, out var errors))
        {
            // Should be unreachable after the pre-checks above; fail closed to an empty filter.
            foreach (var (field, messages) in errors)
            {
                notes.Add($"{field}: {string.Join("; ", messages)}");
            }

            filter = BridgeFilter.Empty;
            state = null;
            conditions.Clear();
            typeGroups.Clear();
            yearBuiltMax = null;
            minAdt = null;
        }

        var applied = new NlFilterSpec(state, conditions, typeGroups, yearBuiltMax, minAdt, notes);
        return new NlFilterResult(filter, applied, RenderInterpretation(applied));
    }

    /// <summary>Code-rendered summary shown for correction — never model text (ADR-008 §2).</summary>
    private static string RenderInterpretation(NlFilterSpec applied)
    {
        var parts = new List<string>();

        if (applied.Conditions is { Count: > 0 })
        {
            parts.Add($"{string.Join(" or ", applied.Conditions)} condition");
        }

        if (applied.TypeGroups is { Count: > 0 })
        {
            parts.Add(string.Join(" or ", applied.TypeGroups));
        }

        if (applied.State is { } state && StateFips.Resolve(state) is { } info)
        {
            parts.Add(info.Name);
        }

        if (applied.YearBuiltMax is { } year)
        {
            parts.Add($"built in or before {year}");
        }

        if (applied.MinAdt is > 0 and { } adt)
        {
            parts.Add($"traffic ≥ {adt:N0}/day");
        }

        var summary = parts.Count > 0
            ? $"Showing: {string.Join(" · ", parts)}"
            : "No filters recognized — showing all bridges";

        return applied.Unsupported is { Count: > 0 }
            ? $"{summary} (couldn't translate: {string.Join("; ", applied.Unsupported)})"
            : summary;
    }
}
