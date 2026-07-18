using System.Text.RegularExpressions;

using SpanSight.Core.Ai;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Ai;

/// <summary>
/// Keyword-based assistant for keyless local development and tests (Ai:Provider=stub). Produces
/// the same <see cref="NlFilterSpec"/> shape as the Anthropic adapter and runs through the same
/// validation path, so the whole FR-AI.1 pipeline is exercisable without an API key or spend.
/// Never registered in cloud config (ADR-008 — the real provider is configured at the 0.5 gate).
/// </summary>
public sealed partial class StubAssistant : ISpanSightAssistant
{
    [GeneratedRegex(@"\bbefore\s+(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex BeforeYear();

    public Task<NlFilterResult> TranslateQueryAsync(string text, CancellationToken cancellationToken = default)
    {
        var lower = text.ToLowerInvariant();

        var conditions = new List<string>();
        if (lower.Contains("good")) conditions.Add("Good");
        if (lower.Contains("fair")) conditions.Add("Fair");
        if (lower.Contains("poor") || lower.Contains("bad")) conditions.Add("Poor");

        var typeGroups = new List<string>();
        if (lower.Contains("girder") || lower.Contains("stringer")) typeGroups.Add("Girder / Stringer");
        if (lower.Contains("truss") || lower.Contains("arch")) typeGroups.Add("Truss / Arch");
        if (lower.Contains("culvert")) typeGroups.Add("Culvert");

        string? state = null;
        foreach (var info in StateFips.ByAbbreviation.Values)
        {
            if (lower.Contains(info.Name.ToLowerInvariant()))
            {
                state = info.Abbreviation;
                break;
            }
        }

        int? yearBuiltMax = BeforeYear().Match(text) is { Success: true } match
            ? int.Parse(match.Groups[1].Value) - 1
            : null;

        int? minAdt = lower.Contains("busy") || lower.Contains("high traffic") ? 10_000 : null;

        var spec = new NlFilterSpec(state, conditions, typeGroups, yearBuiltMax, minAdt, Unsupported: null);
        return Task.FromResult(NlFilterTranslator.Translate(spec));
    }
}
