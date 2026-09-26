using System.Globalization;

namespace DevTerm.Devices.Nmea;

/// <summary>
/// Generic NMEA 0183 sentence helpers - checksum validation and the field conversions (lat/lon
/// degrees-minutes, UTC time/date) shared by every sentence type <see cref="NmeaGpsDecoder"/>
/// recognizes. NMEA 0183 is a public, vendor-independent standard (see docs/design/presenters.md's
/// "protocol decoders" category, which names it as a canonical example) - nothing here is specific
/// to any one receiver.
/// </summary>
internal static class NmeaSentence
{
    /// <summary>
    /// Splits a raw <c>$&lt;talker&gt;&lt;type&gt;,field,field,...*hh</c> line into its sentence
    /// type (talker id stripped) and fields, and reports whether an appended checksum, if any,
    /// fails to match. Returns <see langword="false"/> for anything that isn't shaped like an NMEA
    /// sentence at all (no leading <c>$</c>, or no talker+type prefix).
    /// </summary>
    public static bool TryParse(string rawLine, out string sentenceType, out IReadOnlyList<string> fields, out bool checksumMismatch)
    {
        sentenceType = string.Empty;
        fields = [];
        checksumMismatch = false;

        if (rawLine.Length < 4 || rawLine[0] != '$')
        {
            return false;
        }

        var starIndex = rawLine.IndexOf('*');
        var body = starIndex >= 0 ? rawLine[1..starIndex] : rawLine[1..];

        if (starIndex >= 0 && starIndex + 2 < rawLine.Length)
        {
            var expectedHex = rawLine.Substring(starIndex + 1, 2);
            checksumMismatch = !string.Equals(Checksum(body), expectedHex, StringComparison.OrdinalIgnoreCase);
        }

        var allFields = body.Split(',');
        if (allFields.Length == 0 || allFields[0].Length < 3)
        {
            return false;
        }

        // First two characters are the talker id (GP/GN/GL/GA/BD/...); the rest is the sentence
        // type (GGA, RMC, ...). A 12-channel single-constellation receiver is expected to always
        // say "GP", but the type is extracted this way regardless of talker id.
        sentenceType = allFields[0][2..];
        fields = allFields[1..];
        return true;
    }

    private static string Checksum(string body)
    {
        var checksum = 0;
        foreach (var c in body)
        {
            checksum ^= c;
        }

        return checksum.ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>Converts NMEA's <c>ddmm.mmmm</c>/<c>dddmm.mmmm</c> degrees-minutes format plus a hemisphere letter ("N"/"S"/"E"/"W") to signed decimal degrees.</summary>
    public static double? ToDecimalDegrees(string value, string hemisphere)
    {
        if (string.IsNullOrEmpty(value) || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
        {
            return null;
        }

        var degrees = Math.Truncate(raw / 100.0);
        var minutes = raw - (degrees * 100.0);
        var decimalDegrees = degrees + (minutes / 60.0);
        return hemisphere is "S" or "W" ? -decimalDegrees : decimalDegrees;
    }

    /// <summary>Formats an NMEA <c>hhmmss[.ss]</c> UTC time field as <c>HH:MM:SS UTC</c>, or the empty string if blank/malformed.</summary>
    public static string FormatUtcTime(string value) =>
        value.Length < 6 ? string.Empty : $"{value[..2]}:{value[2..4]}:{value[4..6]} UTC";

    /// <summary>Formats an NMEA <c>ddmmyy</c> date field (RMC) as <c>YYYY-MM-DD</c>, assuming the 21st century.</summary>
    public static string FormatUtcDate(string value) =>
        value.Length != 6 ? string.Empty : $"20{value[4..6]}-{value[2..4]}-{value[..2]}";
}
