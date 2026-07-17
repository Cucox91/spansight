using NetTopologySuite.Geometries;

using SpanSight.Core.Domain;
using SpanSight.Core.Filtering;

namespace SpanSight.Api.Querying;

/// <summary>
/// Translates the validated <see cref="BridgeFilter"/> into an EF Core query. Pure function of
/// (queryable, filter) — unit-testable without a database; the PostGIS translation (bbox →
/// <c>ST_Intersects</c> over the GIST index) is exercised by the Testcontainers suite.
/// </summary>
public static class BridgeQueryBuilder
{
    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    public static IQueryable<Bridge> Apply(IQueryable<Bridge> query, BridgeFilter filter)
    {
        if (filter.StateFipsCode is { } state)
        {
            query = query.Where(b => b.StateCode == state);
        }

        if (filter.CountyCode is { } county)
        {
            query = query.Where(b => b.CountyCode == county);
        }

        if (filter.Conditions is { Count: > 0 } conditions)
        {
            query = query.Where(b => conditions.Contains(b.ConditionClass));
        }

        if (filter.MaterialCodes is { Count: > 0 } materials)
        {
            query = query.Where(b => b.MaterialCode != null && materials.Contains(b.MaterialCode));
        }

        if (filter.DesignCodes is { Count: > 0 } designs)
        {
            query = query.Where(b => b.DesignCode != null && designs.Contains(b.DesignCode));
        }

        if (filter.YearBuiltMin is { } yearMin)
        {
            query = query.Where(b => b.YearBuilt >= yearMin);
        }

        if (filter.YearBuiltMax is { } yearMax)
        {
            query = query.Where(b => b.YearBuilt <= yearMax);
        }

        if (filter.MinAdt is { } minAdt)
        {
            query = query.Where(b => b.Adt >= minAdt);
        }

        if (filter.Bbox is { } bbox)
        {
            var envelope = ToPolygon(bbox);
            query = query.Where(b => b.Location.Intersects(envelope));
        }

        return query;
    }

    public static Polygon ToPolygon(BoundingBox bbox) =>
        Geometry.CreatePolygon(
        [
            new Coordinate(bbox.MinLon, bbox.MinLat),
            new Coordinate(bbox.MaxLon, bbox.MinLat),
            new Coordinate(bbox.MaxLon, bbox.MaxLat),
            new Coordinate(bbox.MinLon, bbox.MaxLat),
            new Coordinate(bbox.MinLon, bbox.MinLat),
        ]);
}
