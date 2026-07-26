using SpanSight.Core.Domain;

namespace SpanSight.Core.Analytics;

/// <summary>One year of a bridge's published condition history (FR-1.2).</summary>
/// <param name="Year">The NBI vintage the observation comes from.</param>
/// <param name="LowestRating">
/// Lowest published rating across items 58/59/60/62 that year, or null when the record carried no
/// numeric rating at all.
/// </param>
/// <param name="ConditionClass">Good/Fair/Poor decoded from <paramref name="LowestRating"/>.</param>
public readonly record struct ConditionObservation(int Year, int? LowestRating, ConditionClass ConditionClass);

/// <summary>
/// Packs and unpacks the per-bridge condition series (FR-1.2 AC-1).
/// <para>
/// The series is stored as one character per year across the bridge's own observed span, so a
/// 34-year history is one ~40-byte row instead of 34 rows. At 1.04M structures that is the
/// difference between a table the B1ms serving instance holds comfortably and 20.6M skinny rows it
/// does not (ADR-005: the serving database gets compact aggregates; the full history stays in
/// Parquet).
/// </para>
/// <para>
/// The trade is deliberate and bounded: the packed column cannot be aggregated in SQL, which is
/// exactly why the county/state rollups are stored relationally alongside it. This encoding is only
/// ever read one whole bridge at a time, by the drawer.
/// </para>
/// <para>
/// The alphabet is produced by <c>tools/trends/trends.sql</c> and must match it:
/// <c>'0'</c>–<c>'9'</c> the lowest published rating · <c>'U'</c> published but unrated ·
/// <c>'.'</c> absent from that vintage. An absent year is a gap in what FHWA published and is never
/// interpolated or carried forward (GR-6).
/// </para>
/// </summary>
public static class ConditionSeriesCodec
{
    /// <summary>The bridge was not in that vintage at all.</summary>
    public const char Absent = '.';

    /// <summary>The bridge was published that year with no numeric rating in items 58/59/60/62.</summary>
    public const char Unrated = 'U';

    /// <summary>Longest series the encoding can describe — one char per vintage, 1992–2025 plus room to grow.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Expands a packed series into one observation per year the bridge was actually published.
    /// Gap years are omitted rather than emitted as nulls: the caller gets what FHWA published and
    /// nothing else, and <c>firstYear</c>/<c>lastYear</c> still describe the full span.
    /// </summary>
    public static IReadOnlyList<ConditionObservation> Decode(int firstYear, string ratings)
    {
        ArgumentNullException.ThrowIfNull(ratings);

        var observations = new List<ConditionObservation>(ratings.Length);
        for (var i = 0; i < ratings.Length; i++)
        {
            var mark = ratings[i];
            if (mark == Absent)
            {
                continue;
            }

            // The class is derived here, by the Phase 0 classifier, rather than stored — so the
            // Good/Fair/Poor thresholds exist in exactly one place in the C# and cannot drift
            // between the drawer and the map (FR-1.2 AC-1).
            int? rating = mark is >= '0' and <= '9' ? mark - '0' : null;
            observations.Add(new ConditionObservation(firstYear + i, rating, ConditionClassifier.Classify(rating)));
        }

        return observations;
    }

    /// <summary>
    /// Packs observations into the stored form, filling unobserved years in the span with
    /// <see cref="Absent"/>. The inverse of <see cref="Decode"/>; used by the tests that hold the
    /// two implementations together, and by anything that needs to state an expected series.
    /// </summary>
    public static string Encode(int firstYear, int lastYear, IEnumerable<ConditionObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (lastYear < firstYear)
        {
            throw new ArgumentOutOfRangeException(nameof(lastYear), $"lastYear {lastYear} precedes firstYear {firstYear}.");
        }

        var marks = new char[lastYear - firstYear + 1];
        Array.Fill(marks, Absent);
        foreach (var observation in observations)
        {
            if (observation.Year < firstYear || observation.Year > lastYear)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(observations), $"Year {observation.Year} is outside the span {firstYear}–{lastYear}.");
            }

            marks[observation.Year - firstYear] = observation.LowestRating switch
            {
                null => Unrated,
                >= 0 and <= 9 => (char)('0' + observation.LowestRating.Value),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(observations), $"Rating {observation.LowestRating} is not a published NBI rating (0–9)."),
            };
        }

        return new string(marks);
    }

    /// <summary>
    /// True when a stored series is self-consistent: known alphabet, length matching the span, and
    /// an observation at each end (a leading or trailing gap would mean the span was mis-recorded).
    /// The loader refuses anything that fails this rather than serving a series it cannot explain.
    /// </summary>
    public static bool IsWellFormed(int firstYear, int lastYear, string? ratings)
    {
        if (string.IsNullOrEmpty(ratings) || lastYear < firstYear || ratings.Length != lastYear - firstYear + 1)
        {
            return false;
        }

        if (ratings[0] == Absent || ratings[^1] == Absent)
        {
            return false;
        }

        foreach (var mark in ratings)
        {
            if (mark is not (Absent or Unrated) && mark is not (>= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Number of years the bridge was actually published — the non-gap characters.</summary>
    public static int CountObserved(string ratings)
    {
        ArgumentNullException.ThrowIfNull(ratings);
        var observed = 0;
        foreach (var mark in ratings)
        {
            if (mark != Absent)
            {
                observed++;
            }
        }

        return observed;
    }
}
