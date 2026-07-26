namespace SpanSight.Core.Vintages;

/// <summary>Raised when a source file is not a usable NBI vintage, or is not the vintage claimed.</summary>
public sealed class VintageFormatException(string message) : Exception(message);

/// <summary>
/// A source header bound to the normalized superset (FR-1.1 AC-1): which source field, if any,
/// feeds each <see cref="VintageSchema.Columns"/> entry.
/// </summary>
public sealed class VintageHeader
{
    private VintageHeader(VintageEra era, IReadOnlyList<string> sourceColumns, int[] sourceIndexByColumn)
    {
        Era = era;
        SourceColumns = sourceColumns;
        SourceIndexByColumn = sourceIndexByColumn;
    }

    public VintageEra Era { get; }

    /// <summary>Column names as they appear in the source file, in file order.</summary>
    public IReadOnlyList<string> SourceColumns { get; }

    /// <summary>
    /// Parallel to <see cref="VintageSchema.Columns"/>: the source field index feeding each output
    /// column, or -1 where this vintage does not carry it (converts to NULL).
    /// </summary>
    public int[] SourceIndexByColumn { get; }

    public int FieldCount => SourceColumns.Count;

    /// <summary>Output columns this vintage does not carry — recorded in the manifest per vintage.</summary>
    public IReadOnlyList<string> AbsentColumns { get; private set; } = [];

    /// <summary>
    /// Binds a header line, failing loudly on anything that would otherwise produce silently wrong
    /// rows: an unrecognisable layout, a missing identity column, an unknown column, or a file
    /// whose era contradicts the year it is being converted as.
    /// </summary>
    /// <param name="headerLine">The raw first line of the source file.</param>
    /// <param name="declaredYear">The vintage year the operator is converting this file as.</param>
    public static VintageHeader Bind(string headerLine, int declaredYear)
    {
        var sourceColumns = VintageLine.Split(headerLine).Select(c => c.Trim()).ToList();
        if (sourceColumns.Count < 2)
        {
            throw new VintageFormatException(
                $"Source header has {sourceColumns.Count} column(s); an NBI vintage has 120+. " +
                "Check the file is the national delimited export and not a zip, a per-state file, or HTML.");
        }

        var era = VintageEraClassifier.Classify(sourceColumns)
            ?? throw new VintageFormatException(
                $"Source header matches no known NBI era (no STATUS_WITH_10YR_RULE, BRIDGE_CONDITION or " +
                $"SUFFICIENCY_RATING among {sourceColumns.Count} columns). First columns: " +
                $"{string.Join(", ", sourceColumns.Take(5))}. Expected an FHWA national delimited vintage.");

        // The year is a claim; the signature is evidence. Where the year's era is pinned, they must agree —
        // this is what stops a 1992 file being converted as 2025 and quietly producing 30-year-wrong rows.
        var expected = VintageYearEra.Expected(declaredYear);
        if (expected is { } expectedEra && expectedEra != era)
        {
            throw new VintageFormatException(
                $"Vintage year/era mismatch: --year {declaredYear} is the {VintageEraClassifier.Describe(expectedEra)}, " +
                $"but this file is the {VintageEraClassifier.Describe(era)}. " +
                "Either the year is wrong or the wrong file was downloaded; nothing was converted.");
        }

        var unknown = sourceColumns.Where(c => !VintageSchema.IsKnown(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
        {
            throw new VintageFormatException(
                $"Source header carries {unknown.Count} column(s) absent from the normalized superset: " +
                $"{string.Join(", ", unknown)}. A new NBI column is a deliberate schema change — add it to " +
                $"{nameof(VintageSchema)}.{nameof(VintageSchema.Columns)} (and re-convert earlier vintages so the " +
                "catalog stays one relation) rather than dropping it.");
        }

        var index = new Dictionary<string, int>(sourceColumns.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sourceColumns.Count; i++)
        {
            index.TryAdd(sourceColumns[i], i); // first occurrence wins, as in the Phase 0 parser
        }

        var missingRequired = VintageSchema.Required.Where(c => !index.ContainsKey(c)).ToList();
        if (missingRequired.Count > 0)
        {
            throw new VintageFormatException(
                $"Source header is missing required column(s): {string.Join(", ", missingRequired)}.");
        }

        var map = new int[VintageSchema.Columns.Count];
        var absent = new List<string>();
        for (var i = 0; i < VintageSchema.Columns.Count; i++)
        {
            var column = VintageSchema.Columns[i];
            if (index.TryGetValue(column, out var source))
            {
                map[i] = source;
            }
            else
            {
                map[i] = -1;
                absent.Add(column);
            }
        }

        return new VintageHeader(era, sourceColumns, map) { AbsentColumns = absent };
    }
}

/// <summary>
/// Which era a given vintage year is known to use. Only years verified against the real FHWA file
/// are pinned — the rest return null, are classified from the file itself, and have their detected
/// era recorded in the catalog manifest. The full 1992–2025 run (W2) is what pins the remainder;
/// guessing them here would invent evidence the project does not have.
/// </summary>
public static class VintageYearEra
{
    private static readonly Dictionary<int, VintageEra> Pinned = new()
    {
        [1992] = VintageEra.TenYearRule,
        [2010] = VintageEra.SufficiencyRating,
        [2025] = VintageEra.PerformanceMeasures,
    };

    public static VintageEra? Expected(int year) => Pinned.TryGetValue(year, out var era) ? era : null;

    public static IReadOnlyCollection<int> PinnedYears => Pinned.Keys;
}
