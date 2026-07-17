using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using SpanSight.Api.Querying;
using SpanSight.Core.Data;
using SpanSight.Core.Domain.Lookups;
using SpanSight.Core.Filtering;

namespace SpanSight.Api.Endpoints;

public static class BridgeEndpoints
{
    /// <summary>Page-size ceiling for list queries (documented in OpenAPI; FR-0.3 AC-1).</summary>
    public const int MaxPageSize = 200;

    /// <summary>Feature cap for the fallback GeoJSON layer — beyond this the map should be on vector tiles (ADR-002).</summary>
    public const int MaxGeoJsonFeatures = 5000;

    public static RouteGroupBuilder MapBridges(this RouteGroupBuilder group)
    {
        group.MapGet("/bridges", ListAsync)
            .WithSummary("Query bridges")
            .WithDescription(
                "Filter by state (USPS or FIPS), county FIPS, condition class (repeatable), material/design codes " +
                $"(repeatable), year-built range, minimum ADT, and bounding box. Page size caps at {MaxPageSize}.");

        group.MapGet("/bridges/geojson", GeoJsonAsync)
            .WithSummary("Query bridges as GeoJSON")
            .WithDescription(
                $"Same filters as /bridges, returned as a FeatureCollection capped at {MaxGeoJsonFeatures} features " +
                "(the 'meta' member reports truncation). The production map path is pre-built vector tiles (ADR-002); " +
                "this endpoint powers the filtered overlay and small-cohort views.");

        group.MapGet("/bridges/{state}/{structureNumber}", DetailAsync)
            .WithSummary("Bridge detail")
            .WithDescription("One structure with every published code decoded to human-readable text. Display-only decoding — not engineering advice.");

        return group;
    }

    private static async Task<Results<Ok<PagedResponse<BridgeSummaryDto>>, ValidationProblem>> ListAsync(
        SpanSightDbContext db,
        string? state,
        string? county,
        string[]? condition,
        string[]? material,
        string[]? design,
        int? yearBuiltMin,
        int? yearBuiltMax,
        int? minAdt,
        string? bbox,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildFilter(state, county, condition, material, design, yearBuiltMin, yearBuiltMax, minAdt, bbox,
                page, pageSize, out var filter, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var query = BridgeQueryBuilder.Apply(db.Bridges.AsNoTracking(), filter);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.StateCode).ThenBy(b => b.StructureNumber).ThenBy(b => b.RecordType)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<BridgeSummaryDto>(
            items.Select(BridgeSummaryDto.From).ToList(), page, pageSize, total));
    }

    private static async Task<Results<JsonHttpResult<object>, ValidationProblem>> GeoJsonAsync(
        SpanSightDbContext db,
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
        if (!TryBuildFilter(state, county, condition, material, design, yearBuiltMin, yearBuiltMax, minAdt, bbox,
                page: 1, pageSize: 1, out var filter, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var query = BridgeQueryBuilder.Apply(db.Bridges.AsNoTracking(), filter);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.Id)
            .Take(MaxGeoJsonFeatures)
            .ToListAsync(cancellationToken);

        var features = items.Select(b => (object)new
        {
            type = "Feature",
            geometry = new { type = "Point", coordinates = new[] { b.Location.X, b.Location.Y } },
            properties = BridgeSummaryDto.From(b),
        }).ToList();

        return TypedResults.Json<object>(new
        {
            type = "FeatureCollection",
            features,
            meta = new { total, returned = features.Count, truncated = total > features.Count },
        });
    }

    private static async Task<Results<Ok<BridgeDetailDto>, ValidationProblem, NotFound<ProblemDetails>>> DetailAsync(
        SpanSightDbContext db,
        string state,
        string structureNumber,
        TimeProvider timeProvider,
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

        var bridge = await db.Bridges.AsNoTracking()
            .Where(b => b.StateCode == stateInfo.Fips && b.StructureNumber == structureNumber)
            .OrderBy(b => b.RecordType)
            .FirstOrDefaultAsync(cancellationToken);

        if (bridge is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Bridge not found",
                Detail = $"No structure '{structureNumber}' in {stateInfo.Abbreviation}.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        return TypedResults.Ok(BridgeDetailDto.From(bridge, timeProvider.GetUtcNow().Year));
    }

    internal static bool TryBuildFilter(
        string? state,
        string? county,
        string[]? condition,
        string[]? material,
        string[]? design,
        int? yearBuiltMin,
        int? yearBuiltMax,
        int? minAdt,
        string? bbox,
        int page,
        int pageSize,
        out BridgeFilter filter,
        out Dictionary<string, string[]> errors)
    {
        BridgeFilter.TryCreate(state, county, condition, material, design, yearBuiltMin, yearBuiltMax, minAdt, bbox,
            out filter, out errors);

        if (page < 1)
        {
            errors["page"] = ["page must be ≥ 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        }

        return errors.Count == 0;
    }
}
