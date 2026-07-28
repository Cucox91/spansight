using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using SpanSight.Api.Export;
using SpanSight.Core.Data;
using SpanSight.Core.Domain;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Endpoints;

/// <summary>
/// FR-1.4 AC-1/AC-3 — rankings computed from published values, with the sort and inclusion rules
/// served alongside the rows, and a server-generated CSV of each.
/// <para>
/// Nothing here scores, weights or prioritises anything. A ranking is an ordering of published NBI
/// values by a stated rule; the rule is in the payload so a view cannot show the order without it,
/// and the counts a rule set aside are published so a reader can see what the list is <i>not</i>
/// (GR-6).
/// </para>
/// </summary>
public static class RankingEndpoints
{
    /// <summary>
    /// The minimum rated structures a group needs before its Poor share is published.
    /// <para>
    /// Deliberately the same 50 that FR-1.3's methodology sets for a transition-matrix row, rather
    /// than a second number invented here: one floor across the product is one thing to explain and
    /// one thing to change. It matters — of the 3,216 counties publishing a structure on the 2025
    /// snapshot, hundreds hold fewer than fifty rated ones, and without a floor a county with three
    /// bridges and one Poor rating tops a national ranking at 33%.
    /// </para>
    /// </summary>
    public const int MinimumGroupSize = 50;

    /// <summary>
    /// Stands in for a cohort dimension whose published code this grouping does not recognise. Not
    /// a bucket the ranking uses — a marker that keeps such rows visible in the excluded counts
    /// instead of being folded into a real cohort.
    /// </summary>
    private const string Unmapped = "unmapped";

    /// <summary>Cap on how many rows a ranking returns, and how many its CSV holds.</summary>
    public const int MaxLimit = 500;

    public const int DefaultLimit = 25;

    /// <summary>
    /// Rendered beside every ranking. The same sentence FR-1.2 and FR-1.3 close with, because it is
    /// the same claim: these are published inspection ratings, ordered.
    /// </summary>
    public const string Note =
        "Good/Fair/Poor is the lowest published NBI condition rating across items 58 (deck), " +
        "59 (superstructure), 60 (substructure) and 62 (culvert): Good 7–9, Fair 5–6, Poor 0–4. " +
        "An ordering of published federal inspection ratings — not a priority list, not an " +
        "assessment of any structure, and not engineering advice.";

    /// <summary>
    /// Why every count here is smaller than the totals on the map. Published in the definition
    /// rather than left for a reader to discover by subtracting.
    /// </summary>
    public const string RecordTypeNote =
        "Counts NBI record type 1 — the structure itself. The published file also carries the " +
        "routes that pass under a structure as separate records, and those carry no condition " +
        "rating, so they are excluded here and from the FR-1.2 trend series alike.";

    public static RouteGroupBuilder MapRankings(this RouteGroupBuilder group)
    {
        group.MapGet("/rankings", RankingAsync)
            .WithSummary("Worst-condition groups, or high-traffic structures in Poor condition")
            .WithDescription(
                "view=worst-condition ranks states, counties or structure cohorts by the share of " +
                "their rated structures published in Poor condition. view=high-adt-poor lists " +
                "individual Poor-condition structures by published average daily traffic (item 29). " +
                "Every response carries the sort rule, what it includes, what it excludes and how " +
                "much was set aside — a share-based ranking publishes nothing for a group with " +
                "fewer than 50 rated structures, and says how many groups that removed.");

        group.MapGet("/rankings.csv", RankingCsvAsync)
            .WithSummary("The same ranking as CSV")
            .WithDescription(
                "Server-generated, identical parameters, and carrying the same definition as leading " +
                "'#' comment lines so the export cannot be read without the rule that produced it.");

        return group;
    }

    private static async Task<Results<Ok<RankingDto>, ValidationProblem>> RankingAsync(
        SpanSightDbContext db,
        string? view,
        string? groupBy,
        string? state,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildQuery(view, groupBy, state, limit, out var query, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await BuildAsync(db, query, cancellationToken));
    }

    private static async Task<IResult> RankingCsvAsync(
        SpanSightDbContext db,
        string? view,
        string? groupBy,
        string? state,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildQuery(view, groupBy, state, limit, out var query, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var ranking = await BuildAsync(db, query, cancellationToken);
        return WriteCsv(ranking).ToResult(FileName(query));
    }

    // ------------------------------------------------------------------ building

    private static async Task<RankingDto> BuildAsync(
        SpanSightDbContext db, RankingQuery query, CancellationToken cancellationToken)
    {
        var snapshotYear = await db.Bridges.AsNoTracking()
            .OrderByDescending(b => b.SnapshotYear)
            .Select(b => b.SnapshotYear)
            .FirstOrDefaultAsync(cancellationToken);

        return query.View == RankingView.HighAdtPoor
            ? await HighAdtPoorAsync(db, query, snapshotYear, cancellationToken)
            : await WorstConditionAsync(db, query, snapshotYear, cancellationToken);
    }

    /// <summary>
    /// Groups ordered by the share of their rated structures published Poor.
    /// <para>
    /// The grouping is done in SQL on the <em>published codes</em> and folded into cohorts in C#
    /// afterwards. That is not a performance choice: <see cref="NbiCohorts"/> owns the code→group
    /// mapping and FR-1.3 already pays to keep one SQL copy of it in step, so a second copy here —
    /// in a different SQL dialect, against a different table — would be a third thing to keep
    /// honest. Grouping on the raw codes leaves exactly one implementation.
    /// </para>
    /// </summary>
    private static async Task<RankingDto> WorstConditionAsync(
        SpanSightDbContext db, RankingQuery query, int snapshotYear, CancellationToken cancellationToken)
    {
        var bridges = db.Bridges.AsNoTracking().Where(b => b.RecordType == "1");
        if (query.StateFips is { } scope)
        {
            bridges = bridges.Where(b => b.StateCode == scope);
        }

        List<Tally> tallies;
        switch (query.GroupBy)
        {
            case RankingGroupBy.State:
                tallies =
                [
                    .. (await bridges
                        .GroupBy(b => b.StateCode)
                        .Select(g => new
                        {
                            Key = g.Key,
                            Good = g.Count(b => b.ConditionClass == ConditionClass.Good),
                            Fair = g.Count(b => b.ConditionClass == ConditionClass.Fair),
                            Poor = g.Count(b => b.ConditionClass == ConditionClass.Poor),
                            Unrated = g.Count(b => b.ConditionClass == ConditionClass.Unknown),
                        })
                        .ToListAsync(cancellationToken))
                    .Select(g => new Tally(
                        g.Key,
                        StateFips.ByFips.TryGetValue(g.Key, out var info) ? info.Name : g.Key,
                        g.Key,
                        g.Good, g.Fair, g.Poor, g.Unrated)),
                ];
                break;

            case RankingGroupBy.County:
                {
                    var rows = await bridges
                        .Where(b => b.CountyCode != null)
                        .GroupBy(b => new { b.StateCode, b.CountyCode })
                        .Select(g => new
                        {
                            g.Key.StateCode,
                            g.Key.CountyCode,
                            Good = g.Count(b => b.ConditionClass == ConditionClass.Good),
                            Fair = g.Count(b => b.ConditionClass == ConditionClass.Fair),
                            Poor = g.Count(b => b.ConditionClass == ConditionClass.Poor),
                            Unrated = g.Count(b => b.ConditionClass == ConditionClass.Unknown),
                        })
                        .ToListAsync(cancellationToken);

                    // Names come from the FR-1.5 join where it has been published. A county code
                    // TIGER no longer carries — every Connecticut one — keeps a label that says
                    // what it knows rather than dropping out of the ranking.
                    var names = await db.CensusCounties.AsNoTracking()
                        .ToDictionaryAsync(c => c.CountyFips, c => c.NameLsad, cancellationToken);

                    tallies =
                    [
                        .. rows.Select(g =>
                        {
                            var fips = g.StateCode + g.CountyCode!.PadLeft(3, '0');
                            var stateName = StateFips.ByFips.TryGetValue(g.StateCode, out var info)
                                ? info.Abbreviation : g.StateCode;
                            return new Tally(
                                fips,
                                names.TryGetValue(fips, out var name)
                                    ? $"{name}, {stateName}"
                                    : $"County FIPS {g.CountyCode}, {stateName}",
                                fips,
                                g.Good, g.Fair, g.Poor, g.Unrated);
                        }),
                    ];
                    break;
                }

            default:
                {
                    var rows = await bridges
                        .GroupBy(b => new { b.DesignCode, b.MaterialCode, b.StateCode })
                        .Select(g => new
                        {
                            g.Key.DesignCode,
                            g.Key.MaterialCode,
                            g.Key.StateCode,
                            Good = g.Count(b => b.ConditionClass == ConditionClass.Good),
                            Fair = g.Count(b => b.ConditionClass == ConditionClass.Fair),
                            Poor = g.Count(b => b.ConditionClass == ConditionClass.Poor),
                            Unrated = g.Count(b => b.ConditionClass == ConditionClass.Unknown),
                        })
                        .ToListAsync(cancellationToken);

                    // NbiCohorts returns null for a published code the grouping has never seen, and
                    // the FR-1.3 job treats that as a hard build failure. A read endpoint cannot
                    // fail a whole ranking over one row, but it must not invent a bucket either —
                    // so an unmapped row lands in a group that says so, which the floor then keeps
                    // out of the ranking while the definition still counts its structures.
                    tallies =
                    [
                        .. rows
                            .GroupBy(g => new
                            {
                                Type = NbiCohorts.TypeGroup(g.DesignCode),
                                Material = NbiCohorts.MaterialGroup(g.MaterialCode),
                                Region = NbiCohorts.Region(g.StateCode),
                            })
                            .Select(c => new Tally(
                                $"{c.Key.Type ?? Unmapped}|{c.Key.Material ?? Unmapped}|{c.Key.Region ?? Unmapped}",
                                c.Key is { Type: { } type, Material: { } material, Region: { } region }
                                    ? $"{material} {type} — {region}"
                                    : "Codes this grouping does not recognise",
                                null,
                                c.Sum(x => x.Good), c.Sum(x => x.Fair), c.Sum(x => x.Poor), c.Sum(x => x.Unrated))),
                    ];
                    break;
                }
        }

        var above = tallies.Where(t => t.Rated >= MinimumGroupSize).ToList();
        var below = tallies.Where(t => t.Rated < MinimumGroupSize).ToList();

        var groups = above
            // Ties broken by the number of Poor structures, then by key — without a total order the
            // same request can return a different page on two identical calls.
            .OrderByDescending(t => t.PoorShare)
            .ThenByDescending(t => t.Poor)
            .ThenBy(t => t.Key, StringComparer.Ordinal)
            .Take(query.Limit)
            .Select((t, i) => new RankingGroupDto(
                i + 1, t.Key, t.Label, t.Fips,
                t.Structures, t.Rated, t.Good, t.Fair, t.Poor, t.Unrated,
                Math.Round(t.PoorShare, 1)))
            .ToList();

        // Both forms are spelled out. Deriving the singular by trimming the plural gave "countie".
        var (unit, units) = query.GroupBy switch
        {
            RankingGroupBy.State => ("state", "states"),
            RankingGroupBy.County => ("county", "counties"),
            _ => ("cohort", "cohorts"),
        };

        var definition = new RankingDefinitionDto(
            $"{Ordinal(query.Limit)} {units} by the share of rated structures published in Poor condition"
                + (query.StateName is { } s ? $", within {s}" : ", nationally"),
            "Percentage of rated structures with a lowest published condition rating of 0–4 (Poor), "
                + "highest first. Ties are broken by the number of Poor structures, then by identifier.",
            $"{RecordTypeNote} A group is ranked when it has at least {MinimumGroupSize} rated structures"
                + (query.GroupBy == RankingGroupBy.County ? " and a published county code (item 3)." : "."),
            $"Structures with no numeric rating on any governing item are counted but excluded from the "
                + $"share. {below.Count} {(below.Count == 1 ? unit : units)} "
                + $"{(below.Count == 1 ? "falls" : "fall")} below the {MinimumGroupSize}-structure minimum "
                + "and are not ranked at all — a share taken from a handful of structures says more about "
                + "the handful than about the group.",
            $"Rated structures in the group — Good + Fair + Poor, not the group's total.",
            MinimumGroupSize,
            below.Count,
            below.Sum(t => (long)t.Structures),
            Note);

        return new RankingDto(
            "worst-condition", unit, snapshotYear, query.StateName,
            definition, groups, [], CsvUrl(query));
    }

    /// <summary>
    /// Individual Poor-condition structures ordered by published traffic. Structure-level by design:
    /// item 29 and items 58/59/60/62 are both published for the structure, so this shows what FHWA
    /// published and nothing derived. (FR-1.3's matrices are the opposite case — a rate computed
    /// across a cohort is never attached to a structure.)
    /// </summary>
    private static async Task<RankingDto> HighAdtPoorAsync(
        SpanSightDbContext db, RankingQuery query, int snapshotYear, CancellationToken cancellationToken)
    {
        var poor = db.Bridges.AsNoTracking()
            .Where(b => b.RecordType == "1" && b.ConditionClass == ConditionClass.Poor);
        if (query.StateFips is { } scope)
        {
            poor = poor.Where(b => b.StateCode == scope);
        }

        var withoutAdt = await poor.CountAsync(b => b.Adt == null, cancellationToken);

        var rows = await poor
            .Where(b => b.Adt != null)
            .OrderByDescending(b => b.Adt)
            .ThenBy(b => b.StateCode)
            .ThenBy(b => b.StructureNumber)
            .Take(query.Limit)
            .Select(b => new
            {
                b.StateCode,
                b.StructureNumber,
                b.CountyCode,
                b.Adt,
                b.LowestRating,
                b.YearBuilt,
                b.FacilityCarried,
                b.FeaturesIntersected,
            })
            .ToListAsync(cancellationToken);

        var fips = rows.Where(r => r.CountyCode != null)
            .Select(r => r.StateCode + r.CountyCode!.PadLeft(3, '0'))
            .Distinct()
            .ToList();
        var names = await db.CensusCounties.AsNoTracking()
            .Where(c => fips.Contains(c.CountyFips))
            .ToDictionaryAsync(c => c.CountyFips, c => c.NameLsad, cancellationToken);

        var structures = rows.Select((r, i) =>
        {
            var countyFips = r.CountyCode is null ? null : r.StateCode + r.CountyCode.PadLeft(3, '0');
            return new RankingStructureDto(
                i + 1,
                StateFips.ByFips.TryGetValue(r.StateCode, out var info) ? info.Abbreviation : r.StateCode,
                r.StateCode,
                r.StructureNumber,
                countyFips,
                countyFips is not null && names.TryGetValue(countyFips, out var name) ? name : null,
                r.Adt!.Value,
                nameof(ConditionClass.Poor),
                r.LowestRating,
                r.YearBuilt,
                r.FacilityCarried,
                r.FeaturesIntersected);
        }).ToList();

        var definition = new RankingDefinitionDto(
            $"{Ordinal(query.Limit)} structures published in Poor condition by average daily traffic"
                + (query.StateName is { } s ? $", within {s}" : ", nationally"),
            "Published average daily traffic (item 29), highest first. Ties are broken by state then "
                + "structure number.",
            $"{RecordTypeNote} A structure is listed when its lowest published condition rating across "
                + "items 58/59/60/62 is 0–4 (Poor) and item 29 carries a traffic count.",
            $"{withoutAdt} structures are in Poor condition but publish no traffic count, so they cannot "
                + "be placed in this order and are not listed. Traffic is as published in this snapshot "
                + "and is not adjusted for the year it was counted.",
            null,
            null,
            0,
            withoutAdt,
            Note);

        return new RankingDto(
            "high-adt-poor", null, snapshotYear, query.StateName, definition, [], structures, CsvUrl(query));
    }

    // ------------------------------------------------------------------ CSV (AC-3)

    public static CsvExport WriteCsv(RankingDto ranking)
    {
        var csv = new CsvExport();
        csv.Comment("SpanSight ranking (FR-1.4)")
            .Comment("View", ranking.View)
            .Comment("Snapshot year", ranking.SnapshotYear.ToString(CultureInfo.InvariantCulture))
            .Comment("Ranking", ranking.Definition.Headline)
            .Comment("Sorted by", ranking.Definition.SortedBy)
            .Comment("Includes", ranking.Definition.Includes)
            .Comment("Excludes", ranking.Definition.Excludes);

        if (ranking.Definition.Denominator is { } denominator)
        {
            csv.Comment("Share denominator", denominator);
        }

        if (ranking.Definition.MinimumGroupSize is { } minimum)
        {
            csv.Comment("Minimum group size", $"{minimum} rated structures");
        }

        csv.Comment("Set aside by the rules above",
                $"{ranking.Definition.ExcludedGroups} " +
                $"{(ranking.Definition.ExcludedGroups == 1 ? "group" : "groups")}, " +
                $"{ranking.Definition.ExcludedStructures} " +
                $"{(ranking.Definition.ExcludedStructures == 1 ? "structure" : "structures")}")
            .Comment(ranking.Definition.Note);

        if (ranking.Groups.Count > 0)
        {
            csv.Header("rank", "key", "label", "fips", "structures", "rated", "good", "fair", "poor",
                "unrated", "poor_percent");
            foreach (var g in ranking.Groups)
            {
                csv.Row(
                    CsvExport.Number(g.Rank), g.Key, g.Label, g.Fips,
                    CsvExport.Number(g.Structures), CsvExport.Number(g.Rated), CsvExport.Number(g.Good),
                    CsvExport.Number(g.Fair), CsvExport.Number(g.Poor), CsvExport.Number(g.Unrated),
                    CsvExport.Percent(g.PoorPercent));
            }
        }
        else
        {
            csv.Header("rank", "state", "structure_number", "county_fips", "county_name", "adt",
                "condition_class", "lowest_rating", "year_built", "facility_carried", "features_intersected");
            foreach (var s in ranking.Structures)
            {
                csv.Row(
                    CsvExport.Number(s.Rank), s.State, s.StructureNumber, s.CountyFips, s.CountyName,
                    CsvExport.Number(s.Adt), s.ConditionClass, CsvExport.Number(s.LowestRating),
                    CsvExport.Number(s.YearBuilt), s.FacilityCarried, s.FeaturesIntersected);
            }
        }

        return csv;
    }

    private static string FileName(RankingQuery query)
    {
        var scope = query.StateFips is null ? "national" : query.StateAbbreviation!.ToLowerInvariant();
        var by = query.View == RankingView.HighAdtPoor
            ? "high-adt-poor"
            : $"worst-condition-by-{query.GroupBy.ToString().ToLowerInvariant()}";
        return $"spansight-{by}-{scope}.csv";
    }

    private static string CsvUrl(RankingQuery query)
    {
        var parts = new List<string>
        {
            $"view={(query.View == RankingView.HighAdtPoor ? "high-adt-poor" : "worst-condition")}",
        };

        if (query.View == RankingView.WorstCondition)
        {
            parts.Add($"groupBy={query.GroupBy.ToString().ToLowerInvariant()}");
        }

        if (query.StateFips is { } fips)
        {
            parts.Add($"state={fips}");
        }

        parts.Add($"limit={query.Limit}");
        return $"/api/rankings.csv?{string.Join('&', parts)}";
    }

    private static string Ordinal(int limit) => $"Top {limit}";

    // ------------------------------------------------------------------ validation

    private readonly record struct Tally(
        string Key, string Label, string? Fips, int Good, int Fair, int Poor, int Unrated)
    {
        public int Rated => Good + Fair + Poor;

        public int Structures => Rated + Unrated;

        /// <summary>
        /// Share of the <em>rated</em> structures, never of the total. Unrated structures are
        /// counted and shown, but a structure FHWA did not rate cannot be evidence that a group is
        /// or is not in poor condition, and dividing by the total would make a group look better
        /// exactly in proportion to how much of it went unrated (GR-6).
        /// </summary>
        public double PoorShare => Rated == 0 ? 0 : 100d * Poor / Rated;
    }

    public enum RankingView
    {
        WorstCondition = 1,
        HighAdtPoor = 2,
    }

    public enum RankingGroupBy
    {
        State = 1,
        County = 2,
        Cohort = 3,
    }

    /// <summary>A validated ranking request. Public so the rules can be unit-tested without a database.</summary>
    public readonly record struct RankingQuery(
        RankingView View,
        RankingGroupBy GroupBy,
        string? StateFips,
        string? StateAbbreviation,
        string? StateName,
        int Limit);

    /// <summary>
    /// Validates and normalises a ranking request, returning field-keyed errors for ProblemDetails.
    /// </summary>
    public static bool TryBuildQuery(
        string? view,
        string? groupBy,
        string? state,
        int? limit,
        out RankingQuery query,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        query = default;

        var parsedView = view?.Trim().ToLowerInvariant() switch
        {
            "worst-condition" => RankingView.WorstCondition,
            "high-adt-poor" => RankingView.HighAdtPoor,
            null or "" => RankingView.WorstCondition,
            _ => (RankingView?)null,
        };

        if (parsedView is null)
        {
            errors["view"] = [$"Unknown view '{view}' — expected 'worst-condition' or 'high-adt-poor'."];
        }

        var parsedGroupBy = groupBy?.Trim().ToLowerInvariant() switch
        {
            "state" => RankingGroupBy.State,
            "county" => RankingGroupBy.County,
            "cohort" => RankingGroupBy.Cohort,
            null or "" => RankingGroupBy.County,
            _ => (RankingGroupBy?)null,
        };

        if (parsedGroupBy is null)
        {
            errors["groupBy"] = [$"Unknown groupBy '{groupBy}' — expected 'state', 'county' or 'cohort'."];
        }
        else if (parsedView == RankingView.HighAdtPoor && !string.IsNullOrWhiteSpace(groupBy))
        {
            // Rejected rather than ignored: high-adt-poor lists structures, so a caller who asked to
            // group them has misunderstood the response they are about to get, and a silently
            // dropped parameter is how that misunderstanding survives.
            errors["groupBy"] = ["groupBy does not apply to view=high-adt-poor, which ranks individual structures."];
        }

        StateInfo? stateInfo = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            stateInfo = StateFips.Resolve(state);
            if (stateInfo is null)
            {
                errors["state"] = [$"Unknown state '{state}'."];
            }
        }

        var parsedLimit = limit ?? DefaultLimit;
        if (parsedLimit is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"limit must be between 1 and {MaxLimit}."];
        }

        if (errors.Count > 0)
        {
            return false;
        }

        query = new RankingQuery(
            parsedView!.Value,
            parsedGroupBy!.Value,
            stateInfo?.Fips,
            stateInfo?.Abbreviation,
            stateInfo?.Name,
            parsedLimit);
        return true;
    }
}
