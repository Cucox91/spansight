using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Analytics;
using SpanSight.Core.Data;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Endpoints;

/// <summary>
/// FR-1.2 AC-2 — condition history over the <c>analytics</c> aggregates published by
/// <c>tools/trends/build-trends.sh</c>. Every response is a count or a decode of published NBI
/// ratings; nothing here forecasts, scores or assesses a structure (GR-6).
/// </summary>
public static class TrendEndpoints
{
    /// <summary>The full published NBI vintage range — requests outside it are a client error, not an empty series.</summary>
    public const int MinYear = 1992;

    /// <summary>Upper bound for validation only; the real last year comes from the loaded aggregates.</summary>
    public const int MaxYear = 2100;

    /// <summary>
    /// Stated next to every series the API returns and rendered in the UI beside the chart, so the
    /// method travels with the numbers rather than living only in a doc (GR-6, FR-1.2 AC-2).
    /// </summary>
    public const string MethodNote =
        "Good/Fair/Poor is the lowest published NBI condition rating across items 58 (deck), " +
        "59 (superstructure), 60 (substructure) and 62 (culvert): Good 7–9, Fair 5–6, Poor 0–4. " +
        "Years FHWA did not publish are omitted, never estimated or carried forward. " +
        "A descriptive history of published inspection ratings — not a prediction, not engineering advice.";

    public static RouteGroupBuilder MapTrends(this RouteGroupBuilder group)
    {
        group.MapGet("/bridges/{state}/{structureNumber}/history", HistoryAsync)
            .WithSummary("Condition history for one structure")
            .WithDescription(
                "Year-by-year Good/Fair/Poor from the 1992–2025 NBI vintages, derived by the same " +
                "lowest-of-governing-components rule as the current-condition view. Years the structure " +
                "was not published are omitted — the response reports the observed count against the span " +
                "so gaps are visible. Display of published ratings only; not engineering advice.");

        group.MapGet("/trends", TrendsAsync)
            .WithSummary("Condition shares over time for a state or county")
            .WithDescription(
                "Good/Fair/Poor counts and shares per year for one area. level=state takes a 2-digit " +
                "state FIPS or USPS abbreviation; level=county takes a 5-digit state+county FIPS. " +
                "Shares are computed from the stored counts, so they always sum to the total. " +
                "County codes are those published in each vintage — Connecticut's 2022 switch from " +
                "counties to planning regions breaks its county series by construction.");

        return group;
    }

    private static async Task<Results<Ok<BridgeHistoryDto>, ValidationProblem, NotFound<ProblemDetails>>> HistoryAsync(
        SpanSightDbContext db,
        string state,
        string structureNumber,
        CancellationToken cancellationToken = default)
    {
        var stateInfo = StateFips.Resolve(state);
        if (stateInfo is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["state"] = [$"Unknown state '{state}'."],
            });
        }

        var series = await db.BridgeConditionSeries.AsNoTracking()
            .Where(s => s.StateCode == stateInfo.Fips && s.StructureNumber == structureNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (series is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "No condition history",
                Detail =
                    $"No published condition history for structure '{structureNumber}' in {stateInfo.Abbreviation}. " +
                    "The structure may post-date the loaded vintages, or the aggregates may not be published yet.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var run = await db.TrendRuns.AsNoTracking()
            .Where(r => r.Id == series.TrendRunId)
            .Select(r => new TrendProvenanceDto(r.JobRunId, r.CatalogSha256, r.CompletedUtc))
            .FirstOrDefaultAsync(cancellationToken);

        // Decoding — including the Good/Fair/Poor thresholds — runs through the Phase 0 classifier,
        // so the drawer's sparkline and its condition badge can never disagree (FR-1.2 AC-1).
        var points = ConditionSeriesCodec.Decode(series.FirstYear, series.Ratings)
            .Select(o => new ConditionPointDto(o.Year, o.LowestRating, o.ConditionClass.ToString()))
            .ToList();

        return TypedResults.Ok(new BridgeHistoryDto(
            $"{stateInfo.Abbreviation}-{series.StructureNumber}",
            stateInfo.Abbreviation,
            series.StructureNumber,
            series.FirstYear,
            series.LastYear,
            series.ObservedYears,
            points,
            MethodNote,
            run ?? new TrendProvenanceDto("unknown", "unknown", null)));
    }

    private static async Task<Results<Ok<TrendSeriesDto>, ValidationProblem>> TrendsAsync(
        SpanSightDbContext db,
        string? level,
        string? fips,
        int? fromYear,
        int? toYear,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildQuery(level, fips, fromYear, toYear, out var parsed, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var rows = await db.ConditionRollups.AsNoTracking()
            .Where(r => r.Level == parsed.Level
                     && r.Fips == parsed.Fips
                     && r.VintageYear >= parsed.FromYear
                     && r.VintageYear <= parsed.ToYear)
            .OrderBy(r => r.VintageYear)
            .ToListAsync(cancellationToken);

        TrendProvenanceDto? provenance = null;
        if (rows.Count > 0)
        {
            provenance = await db.TrendRuns.AsNoTracking()
                .Where(r => r.Id == rows[0].TrendRunId)
                .Select(r => new TrendProvenanceDto(r.JobRunId, r.CatalogSha256, r.CompletedUtc))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var points = rows.Select(r => new TrendPointDto(
            r.VintageYear, r.Total, r.Good, r.Fair, r.Poor, r.Unknown,
            Share(r.Good, r.Total), Share(r.Fair, r.Total), Share(r.Poor, r.Total), Share(r.Unknown, r.Total)))
            .ToList();

        return TypedResults.Ok(new TrendSeriesDto(
            parsed.Level.ToString(),
            parsed.Fips,
            parsed.Name,
            parsed.FromYear,
            parsed.ToYear,
            points,
            MethodNote,
            provenance));
    }

    /// <summary>Percentage to one decimal; null rather than a division by zero when the year is empty.</summary>
    private static double? Share(int part, int total) =>
        total == 0 ? null : Math.Round(100d * part / total, 1);

    /// <summary>A validated trends request. Public so the validation rules can be unit-tested without a database.</summary>
    public readonly record struct TrendQuery(RollupLevel Level, string Fips, string Name, short FromYear, short ToYear);

    /// <summary>
    /// Validates and normalises a trends request, returning field-keyed errors for ProblemDetails.
    /// </summary>
    public static bool TryBuildQuery(
        string? level,
        string? fips,
        int? fromYear,
        int? toYear,
        out TrendQuery query,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        query = default;

        var parsedLevel = level?.Trim().ToLowerInvariant() switch
        {
            "state" => RollupLevel.State,
            "county" => RollupLevel.County,
            null or "" => (RollupLevel?)null,
            _ => null,
        };

        if (parsedLevel is null)
        {
            errors["level"] = [string.IsNullOrWhiteSpace(level)
                ? "level is required — 'state' or 'county'."
                : $"Unknown level '{level}' — expected 'state' or 'county'."];
        }

        var trimmedFips = fips?.Trim() ?? string.Empty;
        var name = string.Empty;
        var resolvedFips = string.Empty;

        if (trimmedFips.Length == 0)
        {
            errors["fips"] = ["fips is required — a 2-digit state FIPS (or USPS code) for level=state, 5-digit state+county FIPS for level=county."];
        }
        else if (parsedLevel == RollupLevel.State)
        {
            // Accept 'FL' as readily as '12' — the same courtesy /bridges already extends.
            var stateInfo = StateFips.Resolve(trimmedFips);
            if (stateInfo is null)
            {
                errors["fips"] = [$"Unknown state '{trimmedFips}'."];
            }
            else
            {
                resolvedFips = stateInfo.Fips;
                name = stateInfo.Name;
            }
        }
        else if (parsedLevel == RollupLevel.County)
        {
            if (trimmedFips.Length != 5 || !trimmedFips.All(char.IsAsciiDigit))
            {
                errors["fips"] = ["A county fips is 5 digits: 2-digit state FIPS then 3-digit county FIPS (e.g. 12086)."];
            }
            else if (StateFips.Resolve(trimmedFips[..2]) is not { } countyState)
            {
                errors["fips"] = [$"'{trimmedFips[..2]}' is not a state FIPS code."];
            }
            else
            {
                resolvedFips = trimmedFips;
                // County names arrive with the TIGER join (FR-1.5, W5). Until then the label says
                // exactly what it knows rather than inventing a name.
                name = $"County FIPS {trimmedFips[2..]}, {countyState.Name}";
            }
        }

        var from = fromYear ?? MinYear;
        var to = toYear ?? MaxYear;

        if (from is < MinYear or > MaxYear)
        {
            errors["fromYear"] = [$"fromYear must be between {MinYear} and {MaxYear}."];
        }

        if (to is < MinYear or > MaxYear)
        {
            errors["toYear"] = [$"toYear must be between {MinYear} and {MaxYear}."];
        }

        if (errors.Count == 0 && from > to)
        {
            errors["fromYear"] = [$"fromYear {from} is after toYear {to}."];
        }

        if (errors.Count > 0)
        {
            return false;
        }

        query = new TrendQuery(parsedLevel!.Value, resolvedFips, name, (short)from, (short)to);
        return true;
    }
}
