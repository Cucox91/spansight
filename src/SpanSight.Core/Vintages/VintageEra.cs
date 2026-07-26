namespace SpanSight.Core.Vintages;

/// <summary>
/// The three NBI record layouts observed across 1992–2025. Eras are told apart by columns that
/// only one of them carries, not by year: the year is what the operator <i>claims</i>, the
/// signature is what the file actually <i>is</i>, and <see cref="VintageHeader"/> refuses to
/// convert when the two disagree (FR-1.1 AC-1).
/// </summary>
public enum VintageEra
{
    /// <summary>
    /// Carries <c>STATUS_WITH_10YR_RULE</c> / <c>STATUS_NO_10YR_RULE</c>: the era of the since-cancelled
    /// FHWA "10-year rule", which excluded bridges built or reconstructed in the prior ten years from
    /// deficiency status. Sampled at 1992 (134 columns).
    /// </summary>
    TenYearRule,

    /// <summary>
    /// Single <c>STATUS</c> column alongside <c>SUFFICIENCY_RATING</c>, no performance-measure columns.
    /// Sampled at 2010 (133 columns).
    /// </summary>
    SufficiencyRating,

    /// <summary>
    /// Carries <c>BRIDGE_CONDITION</c> / <c>LOWEST_RATING</c> / <c>DECK_AREA</c>: the layout adjusted for the
    /// Pavement and Bridge Condition Performance Measures final rule. Sampled at 2025 (123 columns).
    /// </summary>
    PerformanceMeasures,
}

/// <summary>Classifies a source header into a <see cref="VintageEra"/> by signature columns.</summary>
public static class VintageEraClassifier
{
    private const string TenYearRuleSignature = "STATUS_WITH_10YR_RULE";
    private const string PerformanceMeasuresSignature = "BRIDGE_CONDITION";
    private const string SufficiencyRatingSignature = "SUFFICIENCY_RATING";

    /// <summary>
    /// Returns the era a header belongs to, or null when it matches none of them — which is the
    /// signal that the file is not an NBI national delimited vintage at all.
    /// </summary>
    public static VintageEra? Classify(IEnumerable<string> columns)
    {
        var present = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);

        // Order matters: the 10-year-rule era also carries SUFFICIENCY_RATING, so test the
        // narrower signature first.
        if (present.Contains(TenYearRuleSignature))
        {
            return VintageEra.TenYearRule;
        }

        if (present.Contains(PerformanceMeasuresSignature))
        {
            return VintageEra.PerformanceMeasures;
        }

        return present.Contains(SufficiencyRatingSignature) ? VintageEra.SufficiencyRating : null;
    }

    /// <summary>
    /// Human-readable description used in error messages, so a mismatch tells the operator what
    /// they actually handed the converter.
    /// </summary>
    public static string Describe(VintageEra era) => era switch
    {
        VintageEra.TenYearRule => "10-year-rule era (STATUS_WITH_10YR_RULE present)",
        VintageEra.SufficiencyRating => "sufficiency-rating era (STATUS + SUFFICIENCY_RATING, no BRIDGE_CONDITION)",
        VintageEra.PerformanceMeasures => "performance-measures era (BRIDGE_CONDITION present)",
        _ => era.ToString(),
    };
}
