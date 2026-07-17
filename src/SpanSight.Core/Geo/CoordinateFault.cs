namespace SpanSight.Core.Geo;

/// <summary>Why a DMS-encoded NBI coordinate pair could not be converted (FR-0.2).</summary>
public enum CoordinateFault
{
    None = 0,

    /// <summary>Item 16 and/or 17 is blank, non-numeric, or all zeros (the NBI "not recorded" idiom).</summary>
    ZeroOrMissing = 1,

    /// <summary>Minutes ≥ 60, seconds ≥ 60, or degrees outside 0–90 (lat) / 0–180 (lon).</summary>
    OutOfRange = 2,

    /// <summary>Value has more digits than the item allows (8 for item 16, 9 for item 17).</summary>
    Malformed = 3,
}
