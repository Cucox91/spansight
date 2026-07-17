namespace SpanSight.Core.Geo;

/// <summary>
/// Converts NBI DMS-encoded coordinates (items 16/17) to WGS84 decimal degrees.
/// </summary>
/// <remarks>
/// Coding Guide encoding: item 16 (latitude) is 8 digits <c>DDMMSSss</c>, item 17 (longitude)
/// is 9 digits <c>DDDMMSSss</c>, both with two implied decimal places on the seconds and both
/// recorded as positive values in the western hemisphere — the converter emits negative
/// longitudes for US locations. Shorter numeric strings are left-padded with zeros (leading
/// zeros are frequently trimmed in delimited exports).
/// </remarks>
public interface IDmsCoordinateConverter
{
    /// <summary>
    /// Attempts to convert a raw item-16/17 pair. Returns <see langword="true"/> with WGS84
    /// coordinates, or <see langword="false"/> with the <paramref name="fault"/> that makes the
    /// pair unusable (the row is then quarantined per FR-0.2 — never silently defaulted).
    /// </summary>
    bool TryConvert(string? rawLatitude, string? rawLongitude, out double latitude, out double longitude, out CoordinateFault fault);
}
