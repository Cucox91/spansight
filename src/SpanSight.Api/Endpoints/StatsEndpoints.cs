using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using SpanSight.Api.Querying;
using SpanSight.Core.Data;
using SpanSight.Core.Domain;

namespace SpanSight.Api.Endpoints;

public static class StatsEndpoints
{
    public static RouteGroupBuilder MapStats(this RouteGroupBuilder group)
    {
        group.MapGet("/stats/summary", SummaryAsync)
            .WithSummary("KPI summary for the current filter")
            .WithDescription("Total, condition breakdown, % Poor, median age, and average ADT over the same filter set as /bridges — the KPI strip and the map share one predicate (DESIGN.md).");

        return group;
    }

    private static async Task<Results<Ok<StatsSummaryDto>, ValidationProblem>> SummaryAsync(
        SpanSightDbContext db,
        TimeProvider timeProvider,
        string? state,
        string? county,
        string[]? condition,
        string[]? material,
        string[]? design,
        int? yearBuiltMin,
        int? yearBuiltMax,
        int? minAdt,
        string? bbox,
        CancellationToken cancellationToken = default)
    {
        if (!BridgeEndpoints.TryBuildFilter(state, county, condition, material, design, yearBuiltMin, yearBuiltMax,
                minAdt, bbox, page: 1, pageSize: 1, out var filter, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var query = BridgeQueryBuilder.Apply(db.Bridges.AsNoTracking(), filter);

        var byCondition = await query
            .GroupBy(b => b.ConditionClass)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var total = byCondition.Sum(g => g.Count);
        var poor = byCondition.FirstOrDefault(g => g.Key == ConditionClass.Poor)?.Count ?? 0;

        // Median via ordered skip on the year_built index — two cheap queries instead of an
        // untranslatable percentile; good enough for a KPI card at national scale.
        int? medianAge = null;
        var withYear = query.Where(b => b.YearBuilt != null);
        var yearCount = await withYear.CountAsync(cancellationToken);
        if (yearCount > 0)
        {
            var medianYear = await withYear
                .OrderBy(b => b.YearBuilt)
                .Skip(yearCount / 2)
                .Select(b => b.YearBuilt)
                .FirstAsync(cancellationToken);
            medianAge = timeProvider.GetUtcNow().Year - medianYear;
        }

        var averageAdt = await query.AverageAsync(b => (double?)b.Adt, cancellationToken);

        return TypedResults.Ok(new StatsSummaryDto(
            total,
            byCondition.ToDictionary(g => g.Key.ToString(), g => g.Count),
            total == 0 ? null : Math.Round(100d * poor / total, 1),
            medianAge,
            averageAdt is { } avg ? (int)Math.Round(avg) : null));
    }
}
