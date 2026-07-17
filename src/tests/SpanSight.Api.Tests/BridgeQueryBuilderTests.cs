using NetTopologySuite.Geometries;

using SpanSight.Api.Querying;
using SpanSight.Core.Domain;
using SpanSight.Core.Filtering;

namespace SpanSight.Api.Tests;

/// <summary>
/// Filter-correctness assertions against LINQ-to-objects (the same expression tree EF translates;
/// the PostGIS translation itself is exercised by the Testcontainers suite).
/// </summary>
public class BridgeQueryBuilderTests
{
    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    private static Bridge Make(
        string state,
        string number,
        double lat,
        double lon,
        ConditionClass condition = ConditionClass.Fair,
        string? county = null,
        string? material = null,
        string? design = null,
        int? year = null,
        int? adt = null) => new()
        {
            StateCode = state,
            StructureNumber = number,
            RecordType = "1",
            CountyCode = county,
            Location = Geometry.CreatePoint(new Coordinate(lon, lat)),
            ConditionClass = condition,
            MaterialCode = material,
            DesignCode = design,
            YearBuilt = year,
            Adt = adt,
        };

    private static readonly List<Bridge> Bridges =
    [
        Make("12", "MIA1", 25.78, -80.21, ConditionClass.Poor, county: "086", material: "3", design: "02", year: 1955, adt: 40000),
        Make("12", "TPA1", 27.96, -82.44, ConditionClass.Good, county: "057", material: "5", design: "02", year: 1985, adt: 41000),
        Make("48", "HOU1", 29.77, -95.37, ConditionClass.Fair, county: "201", material: "3", design: "10", year: 1968, adt: 142000),
        Make("06", "LAX1", 34.04, -118.23, ConditionClass.Poor, county: "037", material: "4", design: "02", year: 1962, adt: 188000),
    ];

    private static List<Bridge> Apply(BridgeFilter filter) =>
        [.. BridgeQueryBuilder.Apply(Bridges.AsQueryable(), filter)];

    [Fact]
    public void Empty_filter_returns_everything()
    {
        Assert.Equal(4, Apply(BridgeFilter.Empty).Count);
    }

    [Fact]
    public void State_filter_matches_fips()
    {
        var result = Apply(new BridgeFilter { StateFipsCode = "12" });

        Assert.Equal(2, result.Count);
        Assert.All(result, b => Assert.Equal("12", b.StateCode));
    }

    [Fact]
    public void Filters_combine_conjunctively()
    {
        var result = Apply(new BridgeFilter
        {
            StateFipsCode = "12",
            Conditions = [ConditionClass.Poor],
            MaterialCodes = ["3"],
            YearBuiltMax = 1960,
        });

        Assert.Equal("MIA1", Assert.Single(result).StructureNumber);
    }

    [Fact]
    public void County_filter_applies()
    {
        var result = Apply(new BridgeFilter { CountyCode = "201" });

        Assert.Equal("HOU1", Assert.Single(result).StructureNumber);
    }

    [Fact]
    public void Condition_filter_accepts_multiple_classes()
    {
        var result = Apply(new BridgeFilter { Conditions = [ConditionClass.Poor, ConditionClass.Fair] });

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Year_range_is_inclusive()
    {
        var result = Apply(new BridgeFilter { YearBuiltMin = 1962, YearBuiltMax = 1968 });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Min_adt_excludes_lower_and_null()
    {
        var result = Apply(new BridgeFilter { MinAdt = 100000 });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Bbox_keeps_only_contained_points()
    {
        // A box around South Florida.
        var result = Apply(new BridgeFilter { Bbox = new BoundingBox(-81.0, 25.0, -80.0, 26.5) });

        Assert.Equal("MIA1", Assert.Single(result).StructureNumber);
    }

    [Fact]
    public void Design_filter_applies()
    {
        var result = Apply(new BridgeFilter { DesignCodes = ["10"] });

        Assert.Equal("HOU1", Assert.Single(result).StructureNumber);
    }
}
