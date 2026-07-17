namespace SpanSight.Core.Domain;

/// <summary>
/// Derives the FHWA Good/Fair/Poor class from published NBI condition ratings.
/// Pure decode of published values — no engineering judgment is computed here (GR-6).
/// </summary>
public static class ConditionClassifier
{
    /// <summary>
    /// Lowest numeric rating across deck (58), superstructure (59), substructure (60) and
    /// culvert (62). "N"/blank/non-numeric codes are ignored; returns null when nothing is numeric
    /// (the FHWA convention: culvert records carry "N" for 58–60 and are classified by item 62).
    /// </summary>
    public static int? LowestRating(string? deck, string? superstructure, string? substructure, string? culvert)
    {
        int? lowest = null;
        foreach (var raw in (ReadOnlySpan<string?>)[deck, superstructure, substructure, culvert])
        {
            var value = raw?.Trim();
            if (value is { Length: 1 } && value[0] is >= '0' and <= '9')
            {
                var rating = value[0] - '0';
                if (lowest is null || rating < lowest)
                {
                    lowest = rating;
                }
            }
        }

        return lowest;
    }

    /// <summary>Good ≥ 7 · Fair 5–6 · Poor ≤ 4 · Unknown when no numeric rating exists.</summary>
    public static ConditionClass Classify(int? lowestRating) => lowestRating switch
    {
        null => ConditionClass.Unknown,
        >= 7 => ConditionClass.Good,
        >= 5 => ConditionClass.Fair,
        _ => ConditionClass.Poor,
    };
}
