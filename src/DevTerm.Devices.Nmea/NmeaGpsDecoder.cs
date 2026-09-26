using System.Buffers;
using System.Globalization;
using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Devices.Nmea;

/// <summary>
/// Decodes a standard NMEA 0183 GPS sentence stream (GGA/RMC/GSA/GSV/VTG) - nothing here is specific
/// to any one receiver. Confirmed compatible with the DeLorme Earthmate GPS BT-20 (USB HID,
/// VID 0x1163/PID 0x0200, a 12-channel NMEA 2.0-compliant unit), whose unprompted, continuous output
/// is the same shape as <c>K8055Decoder</c>'s unprompted HID input reports; nothing stops this same
/// decoder from being reused for a future serial or TCP-attached NMEA receiver. Implements
/// <see cref="IStructuredPresenter"/> so a control panel's live indicators (see
/// <see cref="NmeaGpsUiDefinition"/>) can be driven the same way as <c>De5000Decoder</c>'s.
/// </summary>
/// <remarks>
/// The exact HID report framing for the Earthmate BT-20 specifically (report length, whether byte 0
/// is a constant report-ID byte the way it is for <c>K8055Decoder</c>/<c>BusylightDecoder</c>) is not
/// confirmed against real hardware - no unit was available to capture a trace this session. NMEA
/// sentences are pure printable ASCII terminated by CR/LF, so any embedded <c>0x00</c> byte can only
/// be HID padding/report-ID filler, never real sentence content; this decoder strips every
/// <c>0x00</c> byte before buffering rather than assuming a specific report length or a fixed
/// report-ID position, so it self-corrects regardless of how the transport's pipe happens to chunk/
/// coalesce individual reports (and is a harmless no-op for a transport that never emits stray NUL
/// bytes, such as serial or TCP). Confirm this assumption against a real device before trusting it
/// fully - see docs/design/proposals/nmea-gps-protocol.md's Status section.
/// </remarks>
public sealed class NmeaGpsDecoder : IPresenter, IStructuredPresenter
{
    private readonly List<byte> _buffer = [];
    private readonly Dictionary<string, string> _lastValues = [];

    public string Name => "nmea";

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        foreach (var segment in data)
        {
            foreach (var b in segment.Span)
            {
                if (b != 0x00)
                {
                    _buffer.Add(b);
                }
            }
        }

        List<string>? lines = null;
        int newlineIndex;
        while ((newlineIndex = _buffer.IndexOf((byte)'\n')) >= 0)
        {
            var lineBytes = _buffer.GetRange(0, newlineIndex);
            _buffer.RemoveRange(0, newlineIndex + 1);
            if (lineBytes.Count > 0 && lineBytes[^1] == (byte)'\r')
            {
                lineBytes.RemoveAt(lineBytes.Count - 1);
            }

            if (lineBytes.Count == 0)
            {
                continue;
            }

            (lines ??= []).Add(DecodeLine(Encoding.ASCII.GetString(lineBytes.ToArray())));
        }

        return lines ?? [];
    }

    private string DecodeLine(string rawLine)
    {
        if (!NmeaSentence.TryParse(rawLine, out var sentenceType, out var fields, out var checksumMismatch))
        {
            return rawLine;
        }

        var (summary, values) = sentenceType switch
        {
            "GGA" => DecodeGga(fields),
            "RMC" => DecodeRmc(fields),
            "GSA" => DecodeGsa(fields),
            "GSV" => DecodeGsv(fields),
            "VTG" => DecodeVtg(fields),
            _ => ($"{sentenceType}: {string.Join(',', fields)}", null),
        };

        if (values is not null)
        {
            PublishValues(values);
        }

        return checksumMismatch ? $"{summary} [checksum mismatch]" : summary;
    }

    private static (string Summary, Dictionary<string, string>? Values) DecodeGga(IReadOnlyList<string> f)
    {
        if (f.Count < 9)
        {
            return ($"GGA: {string.Join(',', f)}", null);
        }

        var time = NmeaSentence.FormatUtcTime(f[0]);
        var latText = FormatDegrees(NmeaSentence.ToDecimalDegrees(f[1], f[2]));
        var lonText = FormatDegrees(NmeaSentence.ToDecimalDegrees(f[3], f[4]));
        var fixText = FixQualityText(f[5]);
        var satellites = f[6];
        var hdop = f[7];
        var altitude = f[8];

        var values = new Dictionary<string, string>
        {
            ["fixQuality"] = fixText,
            ["satellitesUsed"] = satellites,
            ["hdop"] = hdop,
            ["latitude"] = latText,
            ["longitude"] = lonText,
            ["altitude"] = altitude,
            ["utcTime"] = time,
        };

        return ($"GGA: fix={fixText} sats={satellites} lat={latText} lon={lonText} alt={altitude}m hdop={hdop} time={time}", values);
    }

    private static (string Summary, Dictionary<string, string>? Values) DecodeRmc(IReadOnlyList<string> f)
    {
        if (f.Count < 9)
        {
            return ($"RMC: {string.Join(',', f)}", null);
        }

        var time = NmeaSentence.FormatUtcTime(f[0]);
        var status = f[1] == "A" ? "Active" : "Void";
        var latText = FormatDegrees(NmeaSentence.ToDecimalDegrees(f[2], f[3]));
        var lonText = FormatDegrees(NmeaSentence.ToDecimalDegrees(f[4], f[5]));
        var speedKnots = f[6];
        var courseTrue = f[7];
        var date = NmeaSentence.FormatUtcDate(f[8]);

        var values = new Dictionary<string, string>
        {
            ["navStatus"] = status,
            ["latitude"] = latText,
            ["longitude"] = lonText,
            ["speedKnots"] = speedKnots,
            ["courseTrue"] = courseTrue,
            ["utcTime"] = time,
            ["utcDate"] = date,
        };

        return ($"RMC: status={status} lat={latText} lon={lonText} speed={speedKnots}kt course={courseTrue} date={date} time={time}", values);
    }

    private static (string Summary, Dictionary<string, string>? Values) DecodeGsa(IReadOnlyList<string> f)
    {
        if (f.Count < 17)
        {
            return ($"GSA: {string.Join(',', f)}", null);
        }

        var fixType = f[1] switch
        {
            "1" => "No fix",
            "2" => "2D",
            "3" => "3D",
            _ => "Unknown",
        };
        var satelliteCount = f.Skip(2).Take(12).Count(prn => prn.Length > 0).ToString(CultureInfo.InvariantCulture);
        var pdop = f[14];
        var hdop = f[15];
        var vdop = f[16];

        var values = new Dictionary<string, string>
        {
            ["fixType"] = fixType,
            ["satellitesUsed"] = satelliteCount,
            ["pdop"] = pdop,
            ["hdop"] = hdop,
            ["vdop"] = vdop,
        };

        return ($"GSA: fixType={fixType} satsUsed={satelliteCount} pdop={pdop} hdop={hdop} vdop={vdop}", values);
    }

    private static (string Summary, Dictionary<string, string>? Values) DecodeGsv(IReadOnlyList<string> f)
    {
        if (f.Count < 3)
        {
            return ($"GSV: {string.Join(',', f)}", null);
        }

        var messageNumber = f[1];
        var totalMessages = f[0];
        var inView = f[2];

        var values = new Dictionary<string, string> { ["satellitesInView"] = inView };
        return ($"GSV: msg {messageNumber}/{totalMessages} satellitesInView={inView}", values);
    }

    private static (string Summary, Dictionary<string, string>? Values) DecodeVtg(IReadOnlyList<string> f)
    {
        if (f.Count < 8)
        {
            return ($"VTG: {string.Join(',', f)}", null);
        }

        var courseTrue = f[0];
        var speedKnots = f[4];
        var speedKmh = f[6];

        var values = new Dictionary<string, string>
        {
            ["courseTrue"] = courseTrue,
            ["speedKnots"] = speedKnots,
            ["speedKmh"] = speedKmh,
        };

        return ($"VTG: course={courseTrue}T speed={speedKnots}kt/{speedKmh}km/h", values);
    }

    private static string FixQualityText(string code) => code switch
    {
        "0" => "No fix",
        "1" => "GPS fix",
        "2" => "DGPS fix",
        "3" => "PPS fix",
        "4" => "RTK fix",
        "5" => "Float RTK",
        "6" => "Estimated",
        _ => code.Length == 0 ? "No fix" : $"Unknown ({code})",
    };

    private static string FormatDegrees(double? degrees) =>
        degrees is { } value ? value.ToString("F6", CultureInfo.InvariantCulture) : string.Empty;

    private void PublishValues(IReadOnlyDictionary<string, string> values)
    {
        Dictionary<string, string>? changed = null;
        foreach (var (id, value) in values)
        {
            if (!_lastValues.TryGetValue(id, out var previous) || previous != value)
            {
                (changed ??= [])[id] = value;
                _lastValues[id] = value;
            }
        }

        if (changed is not null)
        {
            ValuesChanged?.Invoke(this, changed);
        }
    }
}
