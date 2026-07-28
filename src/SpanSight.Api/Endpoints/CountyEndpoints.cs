using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using SpanSight.Api.Export;
using SpanSight.Core.Analytics;
using SpanSight.Core.Data;
using SpanSight.Core.Domain;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Endpoints;

/// <summary>
/// FR-1.4 AC-2 / FR-1.5 AC-3 — the deep-linkable county report card: current counts and condition
/// shares, the FR-1.2 condition history for the same county, and the population served.
/// <para>
/// Keyed on the county code FHWA publishes in item 3, not on the polygon a coordinate falls inside.
/// That is deliberate and it is what keeps the card internally consistent: the trend series is
/// keyed the same way, the map's county filter is keyed the same way, and SpanSight displays
/// published data rather than substituting its own geography for the publisher's (GR-6). The
/// FR-1.5 join measures how often the two disagree and publishes that on the QA page.
/// </para>
/// </summary>
public static class CountyEndpoints
{
    /// <summary>
    /// Why the card's counts and the last point of its trend series can differ by a few structures.
    /// Stated rather than reconciled away, because they are answers to different questions.
    /// </summary>
    public const string CountsNote =
        "Counts describe the structures currently served for this county — NBI record type 1 from " +
        "the loaded snapshot. The condition history beneath them is the published record from the " +
        "1992–2025 vintages, so its most recent year can differ by a handful of structures: rows " +
        "the current load quarantined (a bad coordinate, a malformed row) are absent from the " +
        "counts and present in the history. Every quarantined row is itemised on the data-quality " +
        "page.";

    /// <summary>The population caveat, rendered wherever the figure appears (FR-1.5 AC-3, GR-6).</summary>
    public const string PopulationNote =
        "An American Community Survey 5-year estimate, not a count. It is published with a margin " +
        "of error, and the Census suppresses that margin for most counties. It describes the people " +
        "living in the county, which is not the same as the people who use these structures — many " +
        "are crossed mostly by traffic from somewhere else.";

    public static RouteGroupBuilder MapCounties(this RouteGroupBuilder group)
    {
        group.MapGet("/counties/{fips}", ReportCardAsync)
            .WithSummary("County report card")
            .WithDescription(
                "Counts and condition shares for one county from the current snapshot, its published " +
                "condition history (FR-1.2), and the ACS population estimate with its vintage and " +
                "margin of error (FR-1.5). Keyed on the county code published in NBI item 3, which is " +
                "how the trend series and the map filter are keyed — the point-in-polygon join is a " +
                "cross-check published on /api/qa/summary, never an override.");

        group.MapGet("/counties/{fips}.csv", ReportCardCsvAsync)
            .WithSummary("The same report card as CSV")
            .WithDescription(
                "Server-generated, carrying the counts, the shares, the population with its ACS " +
                "vintage, and the full condition history as one row per published year.");

        return group;
    }

    private static async Task<Results<Ok<CountyReportCardDto>, ValidationProblem, NotFound<ProblemDetails>>>
        ReportCardAsync(
            SpanSightDbContext db,
            string fips,
            CancellationToken cancellationToken = default)
    {
        if (!TryParseFips(fips, out var parsed, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var card = await BuildAsync(db, parsed, cancellationToken);
        if (card is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "No such county",
                Detail =
                    $"County FIPS '{parsed.Fips}' publishes no structures and is not in the Census " +
                    "county set. A county with a boundary but no structures returns a card with " +
                    "zero counts; this code matches neither.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        return TypedResults.Ok(card);
    }

    private static async Task<IResult> ReportCardCsvAsync(
        SpanSightDbContext db,
        string fips,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFips(fips, out var parsed, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var card = await BuildAsync(db, parsed, cancellationToken);
        return card is null
            ? TypedResults.NotFound()
            : WriteCsv(card).ToResult($"spansight-county-{parsed.Fips}.csv");
    }

    // ------------------------------------------------------------------ building

    private static async Task<CountyReportCardDto?> BuildAsync(
        SpanSightDbContext db, CountyQuery query, CancellationToken cancellationToken)
    {
        var county = await db.CensusCounties.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CountyFips == query.Fips, cancellationToken);

        var countyCode = query.Fips[2..];
        var bridges = db.Bridges.AsNoTracking()
            .Where(b => b.RecordType == "1" && b.StateCode == query.StateFips && b.CountyCode == countyCode);

        var tally = await bridges
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Good = g.Count(b => b.ConditionClass == ConditionClass.Good),
                Fair = g.Count(b => b.ConditionClass == ConditionClass.Fair),
                Poor = g.Count(b => b.ConditionClass == ConditionClass.Poor),
                Unrated = g.Count(b => b.ConditionClass == ConditionClass.Unknown),
                // Summed as a long: a large county's traffic counts overflow an int when added up,
                // and the sum is a published-traffic total, not an average anyone should compute.
                TotalAdt = g.Sum(b => (long?)b.Adt),
                WithYear = g.Count(b => b.YearBuilt != null),
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Neither the Census nor NBI knows this code: a genuine 404 rather than an empty card, which
        // would otherwise make a typo indistinguishable from a real county with no structures.
        if (county is null && tally is null)
        {
            return null;
        }

        int? medianYearBuilt = null;
        if (tally is { WithYear: > 0 })
        {
            medianYearBuilt = await bridges
                .Where(b => b.YearBuilt != null)
                .OrderBy(b => b.YearBuilt)
                .Skip(tally.WithYear / 2)
                .Select(b => b.YearBuilt)
                .FirstAsync(cancellationToken);
        }

        var snapshotYear = await db.Bridges.AsNoTracking()
            .OrderByDescending(b => b.SnapshotYear)
            .Select(b => b.SnapshotYear)
            .FirstOrDefaultAsync(cancellationToken);

        var good = tally?.Good ?? 0;
        var fair = tally?.Fair ?? 0;
        var poor = tally?.Poor ?? 0;
        var unrated = tally?.Unrated ?? 0;
        var rated = good + fair + poor;

        // The history comes from the FR-1.2 endpoint's own aggregates, by the same key. Null when
        // the trend job has not been published, so the card renders without inventing a series.
        TrendSeriesDto? trend = null;
        var rollups = await db.ConditionRollups.AsNoTracking()
            .Where(r => r.Level == RollupLevel.County && r.Fips == query.Fips)
            .OrderBy(r => r.VintageYear)
            .ToListAsync(cancellationToken);

        if (rollups.Count > 0)
        {
            var provenance = await db.TrendRuns.AsNoTracking()
                .Where(r => r.Id == rollups[0].TrendRunId)
                .Select(r => new TrendProvenanceDto(r.JobRunId, r.CatalogSha256, r.CompletedUtc))
                .FirstOrDefaultAsync(cancellationToken);

            trend = new TrendSeriesDto(
                nameof(RollupLevel.County),
                query.Fips,
                county?.NameLsad ?? query.FallbackName,
                rollups[0].VintageYear,
                rollups[^1].VintageYear,
                [.. rollups.Select(r => new TrendPointDto(
                    r.VintageYear, r.Total, r.Good, r.Fair, r.Poor, r.Unknown,
                    Share(r.Good, r.Total), Share(r.Fair, r.Total),
                    Share(r.Poor, r.Total), Share(r.Unknown, r.Total)))],
                TrendEndpoints.MethodNote,
                provenance);
        }

        return new CountyReportCardDto(
            query.Fips,
            county?.NameLsad ?? query.FallbackName,
            query.StateFips,
            query.StateAbbreviation,
            query.StateName,
            snapshotYear,
            rated + unrated,
            rated,
            good,
            fair,
            poor,
            unrated,
            Share(poor, rated),
            Share(good, rated),
            Share(fair, rated),
            medianYearBuilt,
            tally?.TotalAdt,
            new CountyPopulationDto(
                county?.Population,
                county?.PopulationMoe,
                county?.AcsVintage,
                county?.AcsPeriod,
                county?.AcsTable,
                Citation(county),
                PopulationNote),
            trend,
            CountsNote,
            RankingEndpoints.Note,
            $"/api/counties/{query.Fips}.csv");
    }

    /// <summary>
    /// The ACS citation, built from the vintage actually stored so it can never name a release the
    /// figure did not come from (FR-1.5 AC-3, NFR-8). A county with a boundary and no estimate says
    /// so rather than citing a source for a number it does not have.
    /// </summary>
    /// <remarks>
    /// The two absences are different facts and get different sentences. A county the Census still
    /// publishes but ACS does not cover is one of the four island areas. A county code the Census
    /// no longer publishes at all is Connecticut, whose eight legacy counties NBI item 3 still
    /// carries — saying "the Census publishes a boundary for it" there would be untrue of every one
    /// of those five thousand structures.
    /// </remarks>
    private static string Citation(CensusCounty? county) => county switch
    {
        { Population: not null, AcsPeriod: not null } =>
            $"U.S. Census Bureau, {county.AcsPeriod} American Community Survey 5-Year Estimates, " +
            $"table {county.AcsTable} (Total Population).",
        not null =>
            "No American Community Survey population estimate is published for this county. The " +
            "Census publishes a boundary for it, but ACS 5-year coverage stops at the 50 states, " +
            "DC and Puerto Rico.",
        _ =>
            "The Census no longer publishes a county with this code, so there is no boundary and no " +
            "population estimate to cite. NBI item 3 still publishes it — Connecticut's eight legacy " +
            "county codes are the whole of this case, after the Census replaced them with nine " +
            "planning regions in 2022.",
    };

    /// <summary>
    /// Shares are of the <em>rated</em> structures. Null rather than zero when nothing is rated: a
    /// county whose every structure went unrated has no Poor share, and publishing 0.0% would read
    /// as "none of them are in poor condition" (GR-6).
    /// </summary>
    private static double? Share(int part, int total) =>
        total == 0 ? null : Math.Round(100d * part / total, 1);

    // ------------------------------------------------------------------ CSV (AC-3)

    public static CsvExport WriteCsv(CountyReportCardDto card)
    {
        var csv = new CsvExport();
        csv.Comment("SpanSight county report card (FR-1.4)")
            .Comment("County", $"{card.CountyName} ({card.CountyFips}), {card.StateName}")
            .Comment("Snapshot year", card.SnapshotYear.ToString(CultureInfo.InvariantCulture))
            .Comment("Counts", card.CountsNote)
            .Comment("Population", card.Population.Citation)
            .Comment("Population caveat", card.Population.Note)
            .Comment(card.MethodNote);

        csv.Header("metric", "value");
        csv.Row("county_fips", card.CountyFips);
        csv.Row("county_name", card.CountyName);
        csv.Row("state", card.State);
        csv.Row("snapshot_year", CsvExport.Number(card.SnapshotYear));
        csv.Row("structures", CsvExport.Number(card.Structures));
        csv.Row("rated", CsvExport.Number(card.Rated));
        csv.Row("good", CsvExport.Number(card.Good));
        csv.Row("fair", CsvExport.Number(card.Fair));
        csv.Row("poor", CsvExport.Number(card.Poor));
        csv.Row("unrated", CsvExport.Number(card.Unrated));
        csv.Row("good_percent_of_rated", CsvExport.Percent(card.GoodPercent));
        csv.Row("fair_percent_of_rated", CsvExport.Percent(card.FairPercent));
        csv.Row("poor_percent_of_rated", CsvExport.Percent(card.PoorPercent));
        csv.Row("median_year_built", CsvExport.Number(card.MedianYearBuilt));
        csv.Row("total_published_adt", CsvExport.Number(card.TotalAdt));
        csv.Row("population_estimate", CsvExport.Number(card.Population.Estimate));
        csv.Row("population_margin_of_error", CsvExport.Number(card.Population.MarginOfError));
        csv.Row("population_acs_vintage", CsvExport.Number(card.Population.AcsVintage));
        csv.Row("population_acs_period", card.Population.AcsPeriod);

        if (card.Trend is { Points.Count: > 0 } trend)
        {
            csv.Comment("Condition history follows — one row per year FHWA published this county")
                .Comment(trend.Method)
                .Header("year", "total", "good", "fair", "poor", "unrated",
                    "good_percent", "fair_percent", "poor_percent", "unrated_percent");
            foreach (var p in trend.Points)
            {
                csv.Row(
                    CsvExport.Number(p.Year), CsvExport.Number(p.Total), CsvExport.Number(p.Good),
                    CsvExport.Number(p.Fair), CsvExport.Number(p.Poor), CsvExport.Number(p.Unknown),
                    CsvExport.Percent(p.GoodShare), CsvExport.Percent(p.FairShare),
                    CsvExport.Percent(p.PoorShare), CsvExport.Percent(p.UnknownShare));
            }
        }

        return csv;
    }

    // ------------------------------------------------------------------ validation

    /// <summary>A validated report-card request. Public so the rules can be unit-tested without a database.</summary>
    public readonly record struct CountyQuery(
        string Fips, string StateFips, string StateAbbreviation, string StateName, string FallbackName);

    /// <summary>
    /// Validates a 5-digit county FIPS. Deliberately the same shape and the same messages the
    /// FR-1.2 trends endpoint uses, so <c>/trends?level=county</c> and <c>/counties/{fips}</c> reject
    /// the same inputs the same way.
    /// </summary>
    public static bool TryParseFips(string? fips, out CountyQuery query, out Dictionary<string, string[]> errors)
    {
        errors = [];
        query = default;

        var trimmed = fips?.Trim() ?? string.Empty;
        if (trimmed.Length != 5 || !trimmed.All(char.IsAsciiDigit))
        {
            errors["fips"] = ["A county fips is 5 digits: 2-digit state FIPS then 3-digit county FIPS (e.g. 12086)."];
            return false;
        }

        if (StateFips.Resolve(trimmed[..2]) is not { } state)
        {
            errors["fips"] = [$"'{trimmed[..2]}' is not a state FIPS code."];
            return false;
        }

        query = new CountyQuery(
            trimmed,
            state.Fips,
            state.Abbreviation,
            state.Name,
            // The label when the Census carries no name for this code — Connecticut's eight legacy
            // county codes on every published vintage. It says what it knows rather than dropping
            // five thousand structures out of the product.
            $"County FIPS {trimmed[2..]}, {state.Name}");
        return true;
    }
}
