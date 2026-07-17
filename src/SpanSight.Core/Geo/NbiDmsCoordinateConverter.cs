using System.Globalization;

namespace SpanSight.Core.Geo;

/// <inheritdoc cref="IDmsCoordinateConverter"/>
public sealed class NbiDmsCoordinateConverter : IDmsCoordinateConverter
{
    private const int LatitudeDigits = 8; // DDMMSSss
    private const int LongitudeDigits = 9; // DDDMMSSss

    public bool TryConvert(string? rawLatitude, string? rawLongitude, out double latitude, out double longitude, out CoordinateFault fault)
    {
        latitude = 0;
        longitude = 0;

        if (!TryParseComponent(rawLatitude, LatitudeDigits, degreeDigits: 2, maxDegrees: 90, out latitude, out fault))
        {
            return false;
        }

        if (!TryParseComponent(rawLongitude, LongitudeDigits, degreeDigits: 3, maxDegrees: 180, out longitude, out fault))
        {
            return false;
        }

        // Items 16/17 are recorded as positive values; NBI structures sit in the western
        // hemisphere, so decimal longitude is negated. (Territories east of the antimeridian,
        // e.g. Guam, are quarantined by the state-bounds check rather than special-cased here.)
        longitude = -longitude;
        fault = CoordinateFault.None;
        return true;
    }

    private static bool TryParseComponent(string? raw, int width, int degreeDigits, int maxDegrees, out double result, out CoordinateFault fault)
    {
        result = 0;

        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            fault = CoordinateFault.ZeroOrMissing;
            return false;
        }

        // Some vintages export the implied decimal explicitly ("254626.80"); normalize it away.
        text = text.Replace(".", "", StringComparison.Ordinal);

        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric))
        {
            fault = CoordinateFault.ZeroOrMissing;
            return false;
        }

        if (text.Length > width)
        {
            fault = CoordinateFault.Malformed;
            return false;
        }

        if (numeric == 0)
        {
            fault = CoordinateFault.ZeroOrMissing;
            return false;
        }

        // Leading zeros are routinely trimmed by spreadsheet round-trips; restore the fixed width.
        text = text.PadLeft(width, '0');

        var degrees = int.Parse(text[..degreeDigits], CultureInfo.InvariantCulture);
        var minutes = int.Parse(text.Substring(degreeDigits, 2), CultureInfo.InvariantCulture);
        var hundredthSeconds = int.Parse(text.Substring(degreeDigits + 2, 4), CultureInfo.InvariantCulture);
        var seconds = hundredthSeconds / 100d;

        if (minutes >= 60 || seconds >= 60 || degrees > maxDegrees)
        {
            fault = CoordinateFault.OutOfRange;
            return false;
        }

        result = degrees + minutes / 60d + seconds / 3600d;
        fault = CoordinateFault.None;
        return true;
    }
}
