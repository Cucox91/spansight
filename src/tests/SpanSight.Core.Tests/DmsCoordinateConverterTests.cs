using SpanSight.Core.Geo;

using Xunit;

namespace SpanSight.Core.Tests;

public class DmsCoordinateConverterTests
{
    private readonly NbiDmsCoordinateConverter _converter = new();

    [Fact]
    public void Converts_known_miami_pair_to_decimal_degrees()
    {
        // 25°46'26.80"N, 80°11'41.63"W — item 16 "25462680", item 17 "080114163".
        var ok = _converter.TryConvert("25462680", "080114163", out var lat, out var lon, out var fault);

        Assert.True(ok);
        Assert.Equal(CoordinateFault.None, fault);
        Assert.Equal(25.0 + 46.0 / 60 + 26.80 / 3600, lat, precision: 6);
        Assert.Equal(-(80.0 + 11.0 / 60 + 41.63 / 3600), lon, precision: 6);
    }

    [Fact]
    public void Left_pads_values_whose_leading_zeros_were_trimmed()
    {
        // "8461560" is "08461560" after a spreadsheet round-trip: 8°46'15.60".
        var ok = _converter.TryConvert("8461560", "80114163", out var lat, out var lon, out _);

        Assert.True(ok);
        Assert.Equal(8.0 + 46.0 / 60 + 15.60 / 3600, lat, precision: 6);
        Assert.Equal(-(80.0 + 11.0 / 60 + 41.63 / 3600), lon, precision: 6);
    }

    [Fact]
    public void Accepts_exports_with_explicit_decimal_point()
    {
        var ok = _converter.TryConvert("254626.80", "0801141.63", out var lat, out _, out _);

        Assert.True(ok);
        Assert.Equal(25.774111, lat, precision: 6);
    }

    [Theory]
    [InlineData(null, "080114163")]
    [InlineData("", "080114163")]
    [InlineData("0", "080114163")]
    [InlineData("00000000", "080114163")]
    [InlineData("25462680", "0")]
    [InlineData("ABC", "080114163")]
    public void Blank_zero_or_nonnumeric_is_ZeroOrMissing(string? lat, string lon)
    {
        var ok = _converter.TryConvert(lat, lon, out _, out _, out var fault);

        Assert.False(ok);
        Assert.Equal(CoordinateFault.ZeroOrMissing, fault);
    }

    [Theory]
    [InlineData("29611560", "095221920")] // minutes = 61
    [InlineData("29467560", "095221920")] // seconds = 75.60
    [InlineData("91461560", "095221920")] // degrees = 91 (lat max 90)
    public void Out_of_range_components_fault(string lat, string lon)
    {
        var ok = _converter.TryConvert(lat, lon, out _, out _, out var fault);

        Assert.False(ok);
        Assert.Equal(CoordinateFault.OutOfRange, fault);
    }

    [Fact]
    public void Too_many_digits_is_Malformed()
    {
        var ok = _converter.TryConvert("294615601", "095221920", out _, out _, out var fault);

        Assert.False(ok);
        Assert.Equal(CoordinateFault.Malformed, fault);
    }

    [Fact]
    public void Longitude_is_negated_for_western_hemisphere()
    {
        _converter.TryConvert("25462680", "080114163", out _, out var lon, out _);

        Assert.True(lon < 0);
    }
}
