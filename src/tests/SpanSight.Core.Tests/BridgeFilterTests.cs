using SpanSight.Core.Domain;
using SpanSight.Core.Filtering;

namespace SpanSight.Core.Tests;

public class BridgeFilterTests
{
    private static (bool Ok, BridgeFilter Filter, Dictionary<string, string[]> Errors) Create(
        string? state = null,
        string? county = null,
        string[]? conditions = null,
        string[]? materials = null,
        string[]? designs = null,
        int? yearMin = null,
        int? yearMax = null,
        int? minAdt = null,
        string? bbox = null)
    {
        var ok = BridgeFilter.TryCreate(state, county, conditions, materials, designs, yearMin, yearMax, minAdt, bbox,
            out var filter, out var errors);
        return (ok, filter, errors);
    }

    [Theory]
    [InlineData("FL")]
    [InlineData("fl")]
    [InlineData("12")]
    public void State_normalizes_to_fips(string input)
    {
        var (ok, filter, _) = Create(state: input);

        Assert.True(ok);
        Assert.Equal("12", filter.StateFipsCode);
    }

    [Fact]
    public void Unknown_state_is_a_field_error()
    {
        var (ok, _, errors) = Create(state: "ZZ");

        Assert.False(ok);
        Assert.Contains("state", errors.Keys);
    }

    [Fact]
    public void County_pads_to_three_digits()
    {
        var (ok, filter, _) = Create(county: "86");

        Assert.True(ok);
        Assert.Equal("086", filter.CountyCode);
    }

    [Fact]
    public void Conditions_parse_case_insensitively_and_dedupe()
    {
        var (ok, filter, _) = Create(conditions: ["poor", "Poor", "FAIR"]);

        Assert.True(ok);
        Assert.Equal([ConditionClass.Poor, ConditionClass.Fair], filter.Conditions);
    }

    [Fact]
    public void Unknown_condition_is_a_field_error()
    {
        var (ok, _, errors) = Create(conditions: ["Terrible"]);

        Assert.False(ok);
        Assert.Contains("condition", errors.Keys);
    }

    [Fact]
    public void Design_codes_pad_to_item_43b_width()
    {
        var (ok, filter, _) = Create(designs: ["2", "19"]);

        Assert.True(ok);
        Assert.Equal(["02", "19"], filter.DesignCodes);
    }

    [Fact]
    public void Inverted_year_range_is_a_field_error()
    {
        var (ok, _, errors) = Create(yearMin: 1990, yearMax: 1960);

        Assert.False(ok);
        Assert.Contains("yearBuiltMin", errors.Keys);
    }

    [Fact]
    public void Bbox_parses_minlon_minlat_maxlon_maxlat()
    {
        var (ok, filter, _) = Create(bbox: "-80.5,25.5,-80.0,26.0");

        Assert.True(ok);
        Assert.Equal(new BoundingBox(-80.5, 25.5, -80.0, 26.0), filter.Bbox);
    }

    [Theory]
    [InlineData("-80.5,25.5,-80.0")] // missing part
    [InlineData("-80.0,26.0,-80.5,25.5")] // inverted
    [InlineData("a,b,c,d")]
    public void Malformed_bbox_is_a_field_error(string bbox)
    {
        var (ok, _, errors) = Create(bbox: bbox);

        Assert.False(ok);
        Assert.Contains("bbox", errors.Keys);
    }

    [Fact]
    public void Empty_input_yields_empty_filter()
    {
        var (ok, filter, _) = Create();

        Assert.True(ok);
        Assert.Equal(BridgeFilter.Empty, filter);
    }
}
