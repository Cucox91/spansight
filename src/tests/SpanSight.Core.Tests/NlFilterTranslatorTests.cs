using SpanSight.Core.Ai;

using Xunit;

namespace SpanSight.Core.Tests;

public class NlFilterTranslatorTests
{
    [Fact]
    public void Translates_full_spec_through_the_validated_filter()
    {
        var spec = new NlFilterSpec(
            State: "FL",
            Conditions: ["Poor"],
            TypeGroups: ["Truss / Arch"],
            YearBuiltMax: 1969,
            MinAdt: 10_000,
            Unsupported: null);

        var result = NlFilterTranslator.Translate(spec);

        Assert.Equal("12", result.Filter.StateFipsCode);
        Assert.Equal(["Poor"], result.Applied.Conditions);
        Assert.Equal(["Truss / Arch"], result.Applied.TypeGroups);
        Assert.Equal(["09", "10", "11", "12", "13", "14"], result.Filter.DesignCodes);
        Assert.Equal(1969, result.Filter.YearBuiltMax);
        Assert.Equal(10_000, result.Filter.MinAdt);
        Assert.Contains("Poor condition", result.Interpretation);
        Assert.Contains("Florida", result.Interpretation);
        Assert.Contains("built in or before 1969", result.Interpretation);
    }

    [Fact]
    public void Fails_closed_on_invalid_values_and_reports_them()
    {
        var spec = new NlFilterSpec(
            State: "ZZ",
            Conditions: ["Poor", "Terrible"],
            TypeGroups: ["Suspension?"],
            YearBuiltMax: 999,
            MinAdt: -5,
            Unsupported: ["in Miami-Dade county"]);

        var result = NlFilterTranslator.Translate(spec);

        Assert.Null(result.Filter.StateFipsCode);
        Assert.Equal(["Poor"], result.Applied.Conditions);
        Assert.Empty(result.Applied.TypeGroups!);
        Assert.Null(result.Filter.YearBuiltMax);
        Assert.Null(result.Filter.MinAdt);
        Assert.Contains("in Miami-Dade county", result.Applied.Unsupported!);
        Assert.Contains("unknown state 'ZZ'", result.Applied.Unsupported!);
        Assert.Contains("unknown condition 'Terrible'", result.Applied.Unsupported!);
        Assert.Contains("unknown structure type 'Suspension?'", result.Applied.Unsupported!);
        Assert.Contains("couldn't translate", result.Interpretation);
    }

    [Fact]
    public void Empty_spec_renders_show_everything_interpretation()
    {
        var result = NlFilterTranslator.Translate(new NlFilterSpec(null, null, null, null, null, null));

        Assert.Equal(Core.Filtering.BridgeFilter.Empty, result.Filter);
        Assert.Equal("No filters recognized — showing all bridges", result.Interpretation);
    }

    [Fact]
    public void Type_group_matching_is_case_insensitive_and_deduplicated()
    {
        var spec = new NlFilterSpec(null, null, ["culvert", "CULVERT"], null, null, null);

        var result = NlFilterTranslator.Translate(spec);

        Assert.Equal(["Culvert"], result.Applied.TypeGroups);
        Assert.Equal(["19"], result.Filter.DesignCodes);
    }
}
