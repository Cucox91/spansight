namespace SpanSight.Core.Domain.Lookups;

/// <summary>
/// The three cohort dimensions FR-1.3 groups transition frequencies by
/// (<c>docs/METHODOLOGY-DETERIORATION.md</c> §5): structure type group (item 43B), material group
/// (item 43A), and NOAA/NCEI climate region (item 1).
/// <para>
/// These are groupings of published code values, nothing more — no judgment is computed here (GR-6).
/// Each group's member codes are exposed so a view can show what a group actually contains rather
/// than asking the reader to trust the label.
/// </para>
/// <para>
/// The same three assignments also exist in <c>tools/deterioration/deterioration.sql</c>, because the
/// matrices are computed offline by DuckDB. Two implementations of one rule is a standing drift risk,
/// so <c>DeteriorationCohortGoldenTests</c> executes the job's own assignment over every published
/// code and asserts it agrees with this class in both directions — the same guard
/// <c>ConditionTrendGoldenTests</c> puts on the Good/Fair/Poor rule.
/// </para>
/// </summary>
public static class NbiCohorts
{
    /// <summary>
    /// Reserved cohort value marking the national all-cohorts context matrix (§4). Set in all three
    /// dimensions together or in none — never partially. No published group may collide with it, which
    /// <see cref="ReservedValueCollidesWithAGroup"/> asserts.
    /// </summary>
    public const string AllCohorts = "All";

    /// <summary>
    /// Item 43A or 43B carried no code. A labelled bucket, not a silent drop and not folded into
    /// "Other": FHWA publishing nothing is not FHWA publishing code 0 (§2 "never imputed", §5
    /// "reported, never silently dropped"). 7,316 component pairs have no 43A and 9,499 no 43B, all
    /// with from-years 1992–2014.
    /// </summary>
    public const string NotPublished = "Not published";

    /// <summary>
    /// Everything FHWA publishes outside the contiguous 48 states + DC, kept as one labelled bucket
    /// (§5). Populated as the *else branch* of <see cref="RegionByStateFips"/> rather than as a fixed
    /// list of territories, so a jurisdiction FHWA has not published yet cannot be dropped silently.
    /// </summary>
    public const string OutsideContiguousUs = "Outside contiguous U.S.";

    /// <summary>
    /// Item 43A material groups. The Phase 0 decode (<see cref="NbiCodes.Materials"/>) stays
    /// authoritative for labelling an individual code; this collapses it on the continuous/simple-span
    /// distinction NBI itself draws, purely to reach viable cohort sample sizes.
    /// <para>
    /// The group names are §5's verbatim. "Other" holds code 0 (Other) and code 9 (Aluminum, wrought
    /// iron, or cast iron) — the label is the methodology's, and because it under-describes code 9 the
    /// UI renders each group's member codes with their published decodes beside it.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> MaterialCodesByGroup =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Concrete"] = ["1", "2"],
            ["Steel"] = ["3", "4"],
            ["Prestressed concrete"] = ["5", "6"],
            ["Timber"] = ["7"],
            ["Masonry"] = ["8"],
            ["Other"] = ["0", "9"],
        };

    /// <summary>
    /// Item 43B type groups — the grouping the filter rail already presents, not a new one (§5).
    /// <para>
    /// This is now the single C# definition: <c>SpanSight.Core.Ai.NlTypeGroups</c> projects it for the
    /// FR-AI.1 translator instead of holding a second copy. <c>web/src/state/filters.ts</c> holds the
    /// TypeScript copy the SPA needs; a parity test pins the two together, which nothing did before.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> TypeCodesByGroup =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Girder / Stringer"] = ["01", "02", "03", "04", "05", "06", "21", "22"],
            ["Truss / Arch"] = ["09", "10", "11", "12", "13", "14"],
            ["Culvert"] = ["19"],
            ["Other"] = ["00", "07", "08", "15", "16", "17", "18", "20"],
        };

    /// <summary>
    /// The nine NOAA/NCEI U.S. climate regions over the 48 contiguous states, keyed by USPS
    /// abbreviation exactly as §5's table lists them. DC is folded into Northeast by NOAA's own
    /// convention. Anything not here is <see cref="OutsideContiguousUs"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> StatesByRegion =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Northeast"] = ["CT", "DE", "MA", "MD", "ME", "NH", "NJ", "NY", "PA", "RI", "VT", "DC"],
            ["Upper Midwest"] = ["IA", "MI", "MN", "WI"],
            ["Ohio Valley"] = ["IL", "IN", "KY", "MO", "OH", "TN", "WV"],
            ["Southeast"] = ["AL", "FL", "GA", "NC", "SC", "VA"],
            ["Northern Rockies & Plains"] = ["MT", "ND", "NE", "SD", "WY"],
            ["South"] = ["AR", "KS", "LA", "MS", "OK", "TX"],
            ["Southwest"] = ["AZ", "CO", "NM", "UT"],
            ["Northwest"] = ["ID", "OR", "WA"],
            ["West"] = ["CA", "NV"],
        };

    /// <summary>Material group per published 43A code; codes are the single characters NBI publishes.</summary>
    public static readonly IReadOnlyDictionary<string, string> MaterialGroupByCode = Invert(MaterialCodesByGroup);

    /// <summary>Type group per published 43B code; codes are the zero-padded two-character strings NBI publishes.</summary>
    public static readonly IReadOnlyDictionary<string, string> TypeGroupByCode = Invert(TypeCodesByGroup);

    /// <summary>
    /// Climate region per 2-digit state FIPS — the form <c>STATE_CODE_001</c> carries, verified
    /// zero-padded in all 34 vintages. Every jurisdiction in <see cref="StateFips"/> is present:
    /// the 49 CONUS+DC entries in their region, the rest under <see cref="OutsideContiguousUs"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RegionByStateFips = BuildRegionLookup();

    /// <summary>
    /// Every cohort value the job can emit in each dimension, in display order — the reserved
    /// national sentinel excluded. Ordered "Not published" last because it is an absence of evidence,
    /// not a kind of bridge.
    /// </summary>
    public static IReadOnlyList<string> MaterialGroups { get; } =
        [.. MaterialCodesByGroup.Keys, NotPublished];

    /// <inheritdoc cref="MaterialGroups"/>
    public static IReadOnlyList<string> TypeGroups { get; } =
        [.. TypeCodesByGroup.Keys, NotPublished];

    /// <inheritdoc cref="MaterialGroups"/>
    public static IReadOnlyList<string> Regions { get; } =
        [.. StatesByRegion.Keys, OutsideContiguousUs];

    /// <summary>
    /// Material group for a published 43A code, or <see cref="NotPublished"/> when the record carried
    /// nothing. A code that is present but unrecognised returns null — the caller must fail rather
    /// than inventing a bucket, because an unrecognised code means NBI published something this
    /// grouping has never seen.
    /// </summary>
    public static string? MaterialGroup(string? code) => Group(MaterialGroupByCode, code);

    /// <inheritdoc cref="MaterialGroup"/>
    /// <remarks>
    /// Absence is checked <em>before</em> the zero-pad, because <c>"".PadLeft(2, '0')</c> is <c>"00"</c>
    /// — a real published 43B code meaning "Other". Padding first would silently convert "FHWA
    /// published nothing" into "FHWA published Other". The job's SQL guards the same edge.
    /// </remarks>
    public static string? TypeGroup(string? code) =>
        string.IsNullOrWhiteSpace(code) ? NotPublished : Group(TypeGroupByCode, code.Trim().PadLeft(2, '0'));

    /// <summary>
    /// Climate region for a 2-digit state FIPS. Null for a code <see cref="StateFips"/> does not
    /// know: a state code SpanSight cannot resolve must fail the build, never land in an unnamed
    /// bucket (§5).
    /// </summary>
    public static string? Region(string? stateFips) =>
        stateFips is null ? null : RegionByStateFips.GetValueOrDefault(stateFips.Trim().PadLeft(2, '0'));

    /// <summary>
    /// True when <paramref name="value"/> is the reserved national sentinel. Guards the invariant that
    /// a matrix row is either fully national or fully a cohort.
    /// </summary>
    public static bool IsAllCohorts(string? value) => string.Equals(value, AllCohorts, StringComparison.Ordinal);

    /// <summary>
    /// Whether any published group name collides with <see cref="AllCohorts"/>. Must stay false — the
    /// sentinel is what lets the national matrix live in the same tables and go through the same
    /// sample-size floor as every cohort.
    /// </summary>
    public static bool ReservedValueCollidesWithAGroup =>
        MaterialGroups.Concat(TypeGroups).Concat(Regions).Any(IsAllCohorts);

    private static string? Group(IReadOnlyDictionary<string, string> lookup, string? code) =>
        string.IsNullOrWhiteSpace(code) ? NotPublished : lookup.GetValueOrDefault(code.Trim());

    private static Dictionary<string, string> Invert(IReadOnlyDictionary<string, string[]> byGroup)
    {
        var byCode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (group, codes) in byGroup)
        {
            foreach (var code in codes)
            {
                // A code in two groups would make cohorts overlap and the national matrix stop being
                // their sum, so it fails at class initialization rather than skewing a published rate.
                if (!byCode.TryAdd(code, group))
                {
                    throw new InvalidOperationException(
                        $"NBI code '{code}' is in both '{byCode[code]}' and '{group}' — cohort groups must be disjoint.");
                }
            }
        }

        return byCode;
    }

    private static Dictionary<string, string> BuildRegionLookup()
    {
        var regionByAbbreviation = Invert(StatesByRegion);
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        // Driven from StateFips rather than from the region table, so the bucket is genuinely the else
        // branch: every jurisdiction SpanSight knows gets a cohort, including territories that have
        // never appeared in a published vintage (AS, MP).
        foreach (var (fips, state) in StateFips.ByFips)
        {
            lookup[fips] = regionByAbbreviation.GetValueOrDefault(state.Abbreviation, OutsideContiguousUs);
        }

        return lookup;
    }
}
