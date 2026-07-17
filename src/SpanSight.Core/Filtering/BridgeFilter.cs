using SpanSight.Core.Domain;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Core.Filtering;

/// <summary>WGS84 bounding box (minLon,minLat,maxLon,maxLat — the MapLibre convention).</summary>
public sealed record BoundingBox(double MinLon, double MinLat, double MaxLon, double MaxLat);

/// <summary>
/// The one validated filter predicate (DESIGN.md: "one predicate, one source of truth").
/// Every consumer — query string, KPI strip, GeoJSON layer, and the Phase 0.5 NL translator
/// (FR-AI.1) — funnels through this type, so nothing AI-shaped can express more than the
/// filter form can (ADR-008 §2).
/// </summary>
public sealed record BridgeFilter
{
    /// <summary>State FIPS code, normalized ("12").</summary>
    public string? StateFipsCode { get; init; }

    /// <summary>County FIPS code within the state, normalized to 3 digits.</summary>
    public string? CountyCode { get; init; }

    public IReadOnlyList<ConditionClass>? Conditions { get; init; }

    /// <summary>Item 43A codes ("3" = steel …).</summary>
    public IReadOnlyList<string>? MaterialCodes { get; init; }

    /// <summary>Item 43B codes ("10" = thru truss …).</summary>
    public IReadOnlyList<string>? DesignCodes { get; init; }

    public int? YearBuiltMin { get; init; }

    public int? YearBuiltMax { get; init; }

    public int? MinAdt { get; init; }

    public BoundingBox? Bbox { get; init; }

    public static readonly BridgeFilter Empty = new();

    /// <summary>
    /// Builds a normalized filter from raw (query-string or LLM-emitted) values, collecting
    /// field-level errors for a ProblemDetails response (FR-0.3 AC-4).
    /// </summary>
    public static bool TryCreate(
        string? state,
        string? county,
        IReadOnlyList<string>? conditions,
        IReadOnlyList<string>? materials,
        IReadOnlyList<string>? designs,
        int? yearBuiltMin,
        int? yearBuiltMax,
        int? minAdt,
        string? bbox,
        out BridgeFilter filter,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        string? stateFips = null;

        if (!string.IsNullOrWhiteSpace(state))
        {
            var info = StateFips.Resolve(state);
            if (info is null)
            {
                errors["state"] = [$"Unknown state '{state}'. Use a USPS abbreviation (FL) or FIPS code (12)."];
            }
            else
            {
                stateFips = info.Fips;
            }
        }

        string? countyNormalized = null;
        if (!string.IsNullOrWhiteSpace(county))
        {
            var trimmed = county.Trim();
            if (trimmed.Length is < 1 or > 3 || !trimmed.All(char.IsAsciiDigit))
            {
                errors["county"] = [$"County must be a 1–3 digit FIPS code, got '{county}'."];
            }
            else
            {
                countyNormalized = trimmed.PadLeft(3, '0');
            }
        }

        List<ConditionClass>? conditionList = null;
        if (conditions is { Count: > 0 })
        {
            conditionList = [];
            var bad = new List<string>();
            foreach (var raw in conditions)
            {
                if (Enum.TryParse<ConditionClass>(raw, ignoreCase: true, out var parsed) && parsed != ConditionClass.Unknown)
                {
                    if (!conditionList.Contains(parsed))
                    {
                        conditionList.Add(parsed);
                    }
                }
                else
                {
                    bad.Add(raw);
                }
            }

            if (bad.Count > 0)
            {
                errors["condition"] = [$"Unknown condition value(s): {string.Join(", ", bad)}. Use Good, Fair, or Poor."];
            }
        }

        var materialList = ValidateCodes(materials, NbiCodes.Materials.Keys, pad: 1, "material", errors);
        var designList = ValidateCodes(designs, NbiCodes.Designs.Keys, pad: 2, "design", errors);

        if (yearBuiltMin is { } min && yearBuiltMax is { } max && min > max)
        {
            errors["yearBuiltMin"] = [$"yearBuiltMin ({min}) is greater than yearBuiltMax ({max})."];
        }

        if (minAdt is < 0)
        {
            errors["minAdt"] = ["minAdt must be ≥ 0."];
        }

        BoundingBox? box = null;
        if (!string.IsNullOrWhiteSpace(bbox))
        {
            var parts = bbox.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 4 ||
                !double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var minLon) ||
                !double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var minLat) ||
                !double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var maxLon) ||
                !double.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out var maxLat))
            {
                errors["bbox"] = ["bbox must be 'minLon,minLat,maxLon,maxLat' in WGS84 decimal degrees."];
            }
            else if (minLon >= maxLon || minLat >= maxLat ||
                     minLat < -90 || maxLat > 90 || minLon < -180 || maxLon > 180)
            {
                errors["bbox"] = ["bbox is out of range or inverted (expected minLon,minLat,maxLon,maxLat)."];
            }
            else
            {
                box = new BoundingBox(minLon, minLat, maxLon, maxLat);
            }
        }

        filter = new BridgeFilter
        {
            StateFipsCode = stateFips,
            CountyCode = countyNormalized,
            Conditions = conditionList is { Count: > 0 } ? conditionList : null,
            MaterialCodes = materialList,
            DesignCodes = designList,
            YearBuiltMin = yearBuiltMin,
            YearBuiltMax = yearBuiltMax,
            MinAdt = minAdt,
            Bbox = box,
        };
        return errors.Count == 0;
    }

    private static List<string>? ValidateCodes(
        IReadOnlyList<string>? values,
        IEnumerable<string> known,
        int pad,
        string field,
        Dictionary<string, string[]> errors)
    {
        if (values is not { Count: > 0 })
        {
            return null;
        }

        var knownSet = known as IReadOnlySet<string> ?? known.ToHashSet(StringComparer.Ordinal);
        var normalized = new List<string>();
        var bad = new List<string>();
        foreach (var raw in values)
        {
            var code = raw.Trim().PadLeft(pad, '0');
            if (knownSet.Contains(code))
            {
                if (!normalized.Contains(code))
                {
                    normalized.Add(code);
                }
            }
            else
            {
                bad.Add(raw);
            }
        }

        if (bad.Count > 0)
        {
            errors[field] = [$"Unknown {field} code(s): {string.Join(", ", bad)}."];
        }

        return normalized.Count > 0 ? normalized : null;
    }
}
