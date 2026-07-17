namespace SpanSight.Core.Domain.Lookups;

/// <summary>
/// Human-readable decodings of published NBI code values (Coding Guide items 43A/43B/58–62).
/// Display-only decoding of published data — no judgment computed (GR-6).
/// </summary>
public static class NbiCodes
{
    /// <summary>Item 43A — kind of material/design.</summary>
    public static readonly IReadOnlyDictionary<string, string> Materials = new Dictionary<string, string>
    {
        ["1"] = "Concrete",
        ["2"] = "Concrete continuous",
        ["3"] = "Steel",
        ["4"] = "Steel continuous",
        ["5"] = "Prestressed concrete",
        ["6"] = "Prestressed concrete continuous",
        ["7"] = "Wood or timber",
        ["8"] = "Masonry",
        ["9"] = "Aluminum, wrought iron, or cast iron",
        ["0"] = "Other",
    };

    /// <summary>Item 43B — type of design and/or construction.</summary>
    public static readonly IReadOnlyDictionary<string, string> Designs = new Dictionary<string, string>
    {
        ["01"] = "Slab",
        ["02"] = "Stringer/multi-beam or girder",
        ["03"] = "Girder and floorbeam system",
        ["04"] = "Tee beam",
        ["05"] = "Box beam or girders — multiple",
        ["06"] = "Box beam or girders — single or spread",
        ["07"] = "Frame",
        ["08"] = "Orthotropic",
        ["09"] = "Truss — deck",
        ["10"] = "Truss — thru",
        ["11"] = "Arch — deck",
        ["12"] = "Arch — thru",
        ["13"] = "Suspension",
        ["14"] = "Stayed girder",
        ["15"] = "Movable — lift",
        ["16"] = "Movable — bascule",
        ["17"] = "Movable — swing",
        ["18"] = "Tunnel",
        ["19"] = "Culvert",
        ["20"] = "Mixed types",
        ["21"] = "Segmental box girder",
        ["22"] = "Channel beam",
        ["00"] = "Other",
    };

    /// <summary>Items 58/59/60/62 — condition rating scale.</summary>
    public static readonly IReadOnlyDictionary<string, string> ConditionRatings = new Dictionary<string, string>
    {
        ["9"] = "Excellent condition",
        ["8"] = "Very good condition",
        ["7"] = "Good condition",
        ["6"] = "Satisfactory condition",
        ["5"] = "Fair condition",
        ["4"] = "Poor condition",
        ["3"] = "Serious condition",
        ["2"] = "Critical condition",
        ["1"] = "Imminent failure condition",
        ["0"] = "Failed condition — out of service",
        ["N"] = "Not applicable",
    };

    public static string DecodeMaterial(string? code) =>
        code is not null && Materials.TryGetValue(code.TrimStart('0') is { Length: > 0 } t ? t : "0", out var text)
            ? text
            : "Unknown";

    public static string DecodeDesign(string? code) =>
        code is not null && Designs.TryGetValue(code.Trim().PadLeft(2, '0'), out var text)
            ? text
            : "Unknown";

    public static string DecodeConditionRating(string? code) =>
        code is not null && ConditionRatings.TryGetValue(code.Trim().ToUpperInvariant(), out var text)
            ? text
            : "Not recorded";
}
