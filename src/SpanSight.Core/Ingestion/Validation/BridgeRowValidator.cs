using System.Globalization;

using NetTopologySuite.Geometries;

using SpanSight.Core.Domain;
using SpanSight.Core.Domain.Lookups;
using SpanSight.Core.Geo;

namespace SpanSight.Core.Ingestion.Validation;

/// <summary>Result of validating one parsed row: a loadable entity, or the reasons it is quarantined.</summary>
public sealed record BridgeValidationResult
{
    public Bridge? Bridge { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public bool IsValid => Bridge is not null;
}

/// <summary>
/// Semantic validation for FR-0.2: coordinate decode + plausibility rules. Strict by design —
/// any failed rule quarantines the whole row with machine-readable reasons; nothing is silently
/// defaulted into <c>core</c>. Data quality is a showcase feature, not a fix (risk R-1).
/// </summary>
public sealed class BridgeRowValidator(IDmsCoordinateConverter coordinateConverter)
{
    private const int MinPlausibleYearBuilt = 1600; // pre-dates any US structure; catches "0" and garbage
    private const int MaxStructureNumberLength = 15; // NBI item 8 field width

    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    public BridgeValidationResult Validate(NbiRawRecord record, int snapshotYear)
    {
        var reasons = new List<string>();

        var state = StateFips.ByFips.GetValueOrDefault(record.StateCode.PadLeft(2, '0'));
        if (state is null)
        {
            reasons.Add(QuarantineReasons.UnknownStateCode);
        }

        if (record.StructureNumber.Length > MaxStructureNumberLength)
        {
            reasons.Add(QuarantineReasons.StructureNumberInvalid);
        }

        double latitude = 0, longitude = 0;
        if (!coordinateConverter.TryConvert(record.RawLatitude, record.RawLongitude, out latitude, out longitude, out var coordinateFault))
        {
            reasons.Add(coordinateFault == CoordinateFault.ZeroOrMissing
                ? QuarantineReasons.CoordinateMissingOrZero
                : QuarantineReasons.CoordinateInvalid);
        }
        else if (state is { CheckBounds: true } &&
                 (latitude < state.MinLat || latitude > state.MaxLat ||
                  longitude < state.MinLon || longitude > state.MaxLon))
        {
            reasons.Add(QuarantineReasons.CoordinateOutsideState);
        }

        var yearBuilt = ParseInt(record.RawYearBuilt);
        if (record.RawYearBuilt is not null && yearBuilt is null)
        {
            reasons.Add(QuarantineReasons.YearBuiltImpossible);
        }
        else if (yearBuilt is { } y && (y < MinPlausibleYearBuilt || y > snapshotYear + 1))
        {
            reasons.Add(QuarantineReasons.YearBuiltImpossible);
        }

        var adt = ParseInt(record.RawAdt);
        if ((record.RawAdt is not null && adt is null) || adt < 0)
        {
            reasons.Add(QuarantineReasons.AdtInvalid);
            adt = null;
        }

        var length = ParseDecimal(record.RawStructureLengthMeters);
        if ((record.RawStructureLengthMeters is not null && length is null) || length < 0)
        {
            reasons.Add(QuarantineReasons.StructureLengthInvalid);
            length = null;
        }

        foreach (var condition in (ReadOnlySpan<string?>)[record.DeckCondition, record.SuperstructureCondition, record.SubstructureCondition, record.CulvertCondition])
        {
            if (condition is not null && !IsValidConditionCode(condition))
            {
                reasons.Add(QuarantineReasons.ConditionCodeInvalid);
                break;
            }
        }

        if (reasons.Count > 0)
        {
            return new BridgeValidationResult { Reasons = reasons };
        }

        var lowest = ConditionClassifier.LowestRating(
            record.DeckCondition, record.SuperstructureCondition, record.SubstructureCondition, record.CulvertCondition);

        return new BridgeValidationResult
        {
            Bridge = new Bridge
            {
                StateCode = state!.Fips,
                StructureNumber = record.StructureNumber,
                RecordType = record.RecordType,
                CountyCode = record.CountyCode?.PadLeft(3, '0'),
                FeaturesIntersected = record.FeaturesDescription,
                FacilityCarried = record.FacilityCarried,
                LocationText = record.LocationText,
                Location = Geometry.CreatePoint(new Coordinate(longitude, latitude)),
                YearBuilt = yearBuilt,
                Adt = adt,
                MaterialCode = record.MaterialCode?.Trim(),
                DesignCode = record.DesignCode?.Trim().PadLeft(2, '0'),
                StructureLengthMeters = length,
                DeckCondition = Normalize(record.DeckCondition),
                SuperstructureCondition = Normalize(record.SuperstructureCondition),
                SubstructureCondition = Normalize(record.SubstructureCondition),
                CulvertCondition = Normalize(record.CulvertCondition),
                LowestRating = lowest,
                ConditionClass = ConditionClassifier.Classify(lowest),
                SourceFormat = SourceFormat.LegacyCodingGuide,
                SnapshotYear = snapshotYear,
            },
        };
    }

    private static string? Normalize(string? condition) => condition?.Trim().ToUpperInvariant();

    private static bool IsValidConditionCode(string value)
    {
        var v = value.Trim();
        return v.Length == 1 && (v[0] is >= '0' and <= '9' || char.ToUpperInvariant(v[0]) == 'N');
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
