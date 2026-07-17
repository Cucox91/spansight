namespace SpanSight.Core.Domain.Lookups;

/// <summary>
/// State/territory FIPS codes with USPS abbreviations and generous bounding boxes used by the
/// out-of-state coordinate check (FR-0.2). Boxes are deliberately padded (~0.5°) — the check
/// exists to catch grossly misplaced coordinates, not to litigate the border.
/// </summary>
public sealed record StateInfo(
    string Fips,
    string Abbreviation,
    string Name,
    double MinLat,
    double MaxLat,
    double MinLon,
    double MaxLon,
    bool CheckBounds = true);

public static class StateFips
{
    // Bounds derived from published state extreme points, padded. AK: the handful of Aleutian
    // structures east of the antimeridian will quarantine — documented, acceptable at this phase.
    // GU/AS/MP sit in the eastern hemisphere where the item-17 "negate longitude" rule breaks,
    // so bounds checking is disabled for them rather than silently mis-verified.
    private static readonly StateInfo[] All =
    [
        new("01", "AL", "Alabama", 30.1, 35.1, -88.6, -84.8),
        new("02", "AK", "Alaska", 51.0, 71.6, -180.0, -129.9),
        new("04", "AZ", "Arizona", 31.2, 37.2, -115.0, -108.9),
        new("05", "AR", "Arkansas", 32.9, 36.6, -94.7, -89.5),
        new("06", "CA", "California", 32.4, 42.1, -124.6, -114.0),
        new("08", "CO", "Colorado", 36.9, 41.1, -109.2, -101.9),
        new("09", "CT", "Connecticut", 40.9, 42.2, -73.9, -71.7),
        new("10", "DE", "Delaware", 38.3, 39.9, -75.9, -74.9),
        new("11", "DC", "District of Columbia", 38.7, 39.1, -77.2, -76.8),
        new("12", "FL", "Florida", 24.3, 31.1, -87.7, -79.9),
        new("13", "GA", "Georgia", 30.3, 35.1, -85.7, -80.7),
        new("15", "HI", "Hawaii", 18.8, 22.4, -160.4, -154.7),
        new("16", "ID", "Idaho", 41.9, 49.1, -117.4, -110.9),
        new("17", "IL", "Illinois", 36.9, 42.6, -91.6, -87.4),
        new("18", "IN", "Indiana", 37.7, 41.9, -88.2, -84.7),
        new("19", "IA", "Iowa", 40.3, 43.6, -96.7, -90.1),
        new("20", "KS", "Kansas", 36.9, 40.1, -102.2, -94.5),
        new("21", "KY", "Kentucky", 36.4, 39.2, -89.7, -81.9),
        new("22", "LA", "Louisiana", 28.8, 33.1, -94.2, -88.7),
        new("23", "ME", "Maine", 42.9, 47.6, -71.2, -66.8),
        new("24", "MD", "Maryland", 37.8, 39.8, -79.6, -74.9),
        new("25", "MA", "Massachusetts", 41.2, 42.9, -73.6, -69.8),
        new("26", "MI", "Michigan", 41.6, 48.4, -90.5, -82.3),
        new("27", "MN", "Minnesota", 43.4, 49.5, -97.4, -89.4),
        new("28", "MS", "Mississippi", 30.1, 35.1, -91.8, -88.0),
        new("29", "MO", "Missouri", 35.9, 40.7, -95.9, -89.0),
        new("30", "MT", "Montana", 44.3, 49.1, -116.2, -103.9),
        new("31", "NE", "Nebraska", 39.9, 43.1, -104.2, -95.2),
        new("32", "NV", "Nevada", 34.9, 42.1, -120.1, -113.9),
        new("33", "NH", "New Hampshire", 42.6, 45.4, -72.7, -70.5),
        new("34", "NJ", "New Jersey", 38.8, 41.4, -75.7, -73.8),
        new("35", "NM", "New Mexico", 31.2, 37.1, -109.2, -102.9),
        new("36", "NY", "New York", 40.4, 45.1, -79.9, -71.7),
        new("37", "NC", "North Carolina", 33.7, 36.7, -84.4, -75.3),
        new("38", "ND", "North Dakota", 45.8, 49.1, -104.2, -96.4),
        new("39", "OH", "Ohio", 38.3, 42.0, -84.9, -80.4),
        new("40", "OK", "Oklahoma", 33.5, 37.1, -103.1, -94.3),
        new("41", "OR", "Oregon", 41.9, 46.4, -124.7, -116.4),
        new("42", "PA", "Pennsylvania", 39.6, 42.4, -80.6, -74.6),
        new("44", "RI", "Rhode Island", 41.1, 42.1, -71.9, -71.0),
        new("45", "SC", "South Carolina", 32.0, 35.3, -83.4, -78.4),
        new("46", "SD", "South Dakota", 42.4, 46.0, -104.2, -96.3),
        new("47", "TN", "Tennessee", 34.9, 36.7, -90.4, -81.5),
        new("48", "TX", "Texas", 25.7, 36.6, -106.7, -93.4),
        new("49", "UT", "Utah", 36.9, 42.1, -114.2, -108.9),
        new("50", "VT", "Vermont", 42.6, 45.1, -73.5, -71.4),
        new("51", "VA", "Virginia", 36.4, 39.6, -83.8, -75.1),
        new("53", "WA", "Washington", 45.4, 49.1, -124.9, -116.8),
        new("54", "WV", "West Virginia", 37.1, 40.7, -82.7, -77.6),
        new("55", "WI", "Wisconsin", 42.4, 47.2, -93.0, -86.7),
        new("56", "WY", "Wyoming", 40.9, 45.1, -111.2, -104.0),
        new("60", "AS", "American Samoa", 0, 0, 0, 0, CheckBounds: false),
        new("66", "GU", "Guam", 0, 0, 0, 0, CheckBounds: false),
        new("69", "MP", "Northern Mariana Islands", 0, 0, 0, 0, CheckBounds: false),
        new("72", "PR", "Puerto Rico", 17.8, 18.6, -67.4, -65.1),
        new("78", "VI", "U.S. Virgin Islands", 17.6, 18.5, -65.2, -64.5),
    ];

    public static readonly IReadOnlyDictionary<string, StateInfo> ByFips =
        All.ToDictionary(s => s.Fips, StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, StateInfo> ByAbbreviation =
        All.ToDictionary(s => s.Abbreviation, StringComparer.OrdinalIgnoreCase);

    /// <summary>Accepts a FIPS code ("12", with or without zero-padding) or a USPS abbreviation ("FL").</summary>
    public static StateInfo? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.All(char.IsAsciiDigit))
        {
            return ByFips.GetValueOrDefault(trimmed.PadLeft(2, '0'));
        }

        return ByAbbreviation.GetValueOrDefault(trimmed);
    }
}
