using SpanSight.Core.Domain;
using SpanSight.Core.Geo;
using SpanSight.Core.Ingestion;
using SpanSight.Core.Ingestion.Validation;

namespace SpanSight.Core.Tests;

public class BridgeRowValidatorTests
{
    private readonly BridgeRowValidator _validator = new(new NbiDmsCoordinateConverter());

    private static NbiRawRecord MiamiRecord(Action<Dictionary<string, string?>>? mutate = null)
    {
        var fields = new Dictionary<string, string?>
        {
            ["lat"] = "25462680",
            ["lon"] = "080114163",
            ["year"] = "1972",
            ["adt"] = "34000",
            ["deck"] = "6",
            ["length"] = "142.3",
        };
        mutate?.Invoke(fields);

        return new NbiRawRecord
        {
            StateCode = "12",
            StructureNumber = "124077C",
            CountyCode = "86",
            FacilityCarried = "NW 12 AVE",
            FeaturesDescription = "MIAMI RIVER",
            RawLatitude = fields["lat"],
            RawLongitude = fields["lon"],
            RawYearBuilt = fields["year"],
            RawAdt = fields["adt"],
            MaterialCode = "5",
            DesignCode = "2",
            RawStructureLengthMeters = fields["length"],
            DeckCondition = fields["deck"],
            SuperstructureCondition = "6",
            SubstructureCondition = "5",
            CulvertCondition = "N",
        };
    }

    [Fact]
    public void Valid_row_builds_a_core_entity_with_decoded_coordinates()
    {
        var result = _validator.Validate(MiamiRecord(), snapshotYear: 2025);

        Assert.True(result.IsValid);
        var bridge = result.Bridge!;
        Assert.Equal("12", bridge.StateCode);
        Assert.Equal("086", bridge.CountyCode); // padded to 3-digit FIPS
        Assert.Equal("02", bridge.DesignCode); // padded to item-43B width
        Assert.Equal(25.774111, bridge.Location.Y, precision: 5);
        Assert.Equal(-80.194897, bridge.Location.X, precision: 5);
        Assert.Equal(4326, bridge.Location.SRID);
        Assert.Equal(5, bridge.LowestRating);
        Assert.Equal(ConditionClass.Fair, bridge.ConditionClass);
        Assert.Equal(2025, bridge.SnapshotYear);
        Assert.Equal(SourceFormat.LegacyCodingGuide, bridge.SourceFormat);
    }

    [Fact]
    public void Zero_coordinates_quarantine_as_missing()
    {
        var result = _validator.Validate(MiamiRecord(f => { f["lat"] = "0"; f["lon"] = "0"; }), 2025);

        Assert.False(result.IsValid);
        Assert.Contains(QuarantineReasons.CoordinateMissingOrZero, result.Reasons);
    }

    [Fact]
    public void Coordinates_outside_the_coded_state_quarantine()
    {
        // Los Angeles coordinates on a Florida record.
        var result = _validator.Validate(MiamiRecord(f => { f["lat"] = "34022760"; f["lon"] = "118134440"; }), 2025);

        Assert.False(result.IsValid);
        Assert.Contains(QuarantineReasons.CoordinateOutsideState, result.Reasons);
    }

    [Fact]
    public void Unknown_state_code_quarantines()
    {
        var record = MiamiRecord() with { StateCode = "83" };

        var result = _validator.Validate(record, 2025);

        Assert.False(result.IsValid);
        Assert.Contains(QuarantineReasons.UnknownStateCode, result.Reasons);
    }

    [Theory]
    [InlineData("2199")] // future
    [InlineData("1500")] // pre-plausible
    [InlineData("18XX")] // non-numeric
    public void Impossible_year_built_quarantines(string year)
    {
        var result = _validator.Validate(MiamiRecord(f => f["year"] = year), 2025);

        Assert.False(result.IsValid);
        Assert.Contains(QuarantineReasons.YearBuiltImpossible, result.Reasons);
    }

    [Fact]
    public void Snapshot_year_plus_one_is_allowed()
    {
        var result = _validator.Validate(MiamiRecord(f => f["year"] = "2026"), 2025);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("abc")]
    public void Invalid_adt_quarantines(string adt)
    {
        var result = _validator.Validate(MiamiRecord(f => f["adt"] = adt), 2025);

        Assert.False(result.IsValid);
        Assert.Contains(QuarantineReasons.AdtInvalid, result.Reasons);
    }

    [Fact]
    public void Invalid_condition_code_quarantines()
    {
        var result = _validator.Validate(MiamiRecord(f => f["deck"] = "X"), 2025);

        Assert.False(result.IsValid);
        Assert.Contains(QuarantineReasons.ConditionCodeInvalid, result.Reasons);
    }

    [Fact]
    public void Negative_length_quarantines()
    {
        var result = _validator.Validate(MiamiRecord(f => f["length"] = "-3.0"), 2025);

        Assert.False(result.IsValid);
        Assert.Contains(QuarantineReasons.StructureLengthInvalid, result.Reasons);
    }

    [Fact]
    public void Multiple_faults_report_every_reason()
    {
        var result = _validator.Validate(
            MiamiRecord(f => { f["year"] = "2199"; f["adt"] = "-1"; f["deck"] = "Z"; }), 2025);

        Assert.False(result.IsValid);
        Assert.Contains(QuarantineReasons.YearBuiltImpossible, result.Reasons);
        Assert.Contains(QuarantineReasons.AdtInvalid, result.Reasons);
        Assert.Contains(QuarantineReasons.ConditionCodeInvalid, result.Reasons);
    }

    [Fact]
    public void Missing_optional_fields_do_not_quarantine()
    {
        var record = new NbiRawRecord
        {
            StateCode = "12",
            StructureNumber = "MINIMAL01",
            RawLatitude = "25462680",
            RawLongitude = "080114163",
        };

        var result = _validator.Validate(record, 2025);

        Assert.True(result.IsValid);
        Assert.Null(result.Bridge!.YearBuilt);
        Assert.Equal(ConditionClass.Unknown, result.Bridge.ConditionClass);
    }
}
