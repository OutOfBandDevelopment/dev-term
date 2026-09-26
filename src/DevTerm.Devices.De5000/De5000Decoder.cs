using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using DevTerm.Core.Presenters;

namespace DevTerm.Devices.De5000;

/// <summary>
/// Decodes the DE-5000's continuous stream of fixed 17-byte measurement packets
/// (<see cref="De5000Framer"/>), per docs/design/proposals/de5000-lcr-meter-protocol.md. Unlike
/// <c>DevTerm.Devices.RadexOne</c>'s request/response HID reports, the meter transmits unprompted and
/// a packet can arrive split across more than one transport read (the BLE optical-to-BLE adapter has
/// no framing of its own past the meter's own header/footer bytes), so bytes are buffered across
/// <see cref="Render"/> calls the same way <c>AsciiPresenter</c> buffers a partial line. Framing
/// resyncs one byte at a time on a header/footer mismatch rather than discarding the whole buffer -
/// cheap here since a real mismatch should be rare (fixed header/footer bytes, no length field to
/// misinterpret).
///
/// Implements <see cref="IStructuredPresenter"/> so a generic control-panel renderer can drive
/// <see cref="De5000UiDefinition"/>'s live indicators.
/// </summary>
public sealed class De5000Decoder : IPresenter, IStructuredPresenter
{
    private readonly List<byte> _buffer = [];
    private readonly Dictionary<string, string> _lastValues = [];

    public string Name => "de5000";

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        foreach (var segment in data)
        {
            foreach (var b in segment.Span)
            {
                _buffer.Add(b);
            }
        }

        List<string>? lines = null;
        while (_buffer.Count >= De5000Framer.FrameLength)
        {
            var candidate = CollectionsMarshal.AsSpan(_buffer)[..De5000Framer.FrameLength];
            if (De5000Framer.TryParse(candidate, out var frame))
            {
                _buffer.RemoveRange(0, De5000Framer.FrameLength);
                (lines ??= []).Add(FormatLine(frame));
                PublishValues(frame);
            }
            else
            {
                _buffer.RemoveAt(0);
            }
        }

        return lines ?? [];
    }

    private void PublishValues(De5000Frame frame)
    {
        var values = new Dictionary<string, string>
        {
            ["primary"] = FormatMeasurement(frame.PrimaryQuantity, frame.PrimaryValue, frame.PrimaryUnit, frame.PrimaryStatus),
            ["secondary"] = FormatMeasurement(frame.SecondaryQuantity, frame.SecondaryValue, frame.SecondaryUnit, frame.SecondaryStatus),
            ["frequency"] = frame.Frequency ?? string.Empty,
            ["hold"] = frame.Hold ? "1" : "0",
            ["delta"] = frame.Delta ? "1" : "0",
            ["referenceShown"] = frame.ReferenceShown ? "1" : "0",
            ["calibration"] = frame.Calibration ? "1" : "0",
            ["sorting"] = frame.Sorting ? "1" : "0",
            ["lcrAuto"] = frame.LcrAuto ? "1" : "0",
            ["autoRange"] = frame.AutoRange ? "1" : "0",
            ["parallel"] = frame.Parallel ? "1" : "0",
        };

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

    private static string FormatLine(De5000Frame frame)
    {
        var parts = new List<string>
        {
            FormatMeasurement(frame.PrimaryQuantity, frame.PrimaryValue, frame.PrimaryUnit, frame.PrimaryStatus),
            FormatMeasurement(frame.SecondaryQuantity, frame.SecondaryValue, frame.SecondaryUnit, frame.SecondaryStatus),
        };

        if (frame.Frequency is not null)
        {
            parts.Add($"@{frame.Frequency}");
        }

        var flags = new List<string>();
        if (frame.Hold)
        {
            flags.Add("HOLD");
        }

        if (frame.Delta)
        {
            flags.Add("REL");
        }

        if (frame.Calibration)
        {
            flags.Add("CAL");
        }

        if (frame.Sorting)
        {
            flags.Add(frame.Tolerance is null ? "SORT" : $"SORT {frame.Tolerance}");
        }

        if (frame.LcrAuto)
        {
            flags.Add("AUTO-LCR");
        }

        if (frame.AutoRange)
        {
            flags.Add("AUTO-RNG");
        }

        if (flags.Count > 0)
        {
            parts.Add($"[{string.Join(" ", flags)}]");
        }

        return string.Join(" ", parts);
    }

    private static string FormatMeasurement(string? quantity, double value, string? unit, string? status)
    {
        var label = quantity ?? "?";

        if (status is "blank" or "----" or "OL")
        {
            return $"{label}={status}";
        }

        var text = $"{label}={value.ToString("G6", CultureInfo.InvariantCulture)}{unit ?? string.Empty}";
        return status is "normal" or null ? text : $"{text} ({status})";
    }
}
