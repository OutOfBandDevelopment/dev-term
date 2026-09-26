using System.Buffers;
using DevTerm.Core.Presenters;

namespace DevTerm.Devices.ZoomH4n;

/// <summary>
/// Decodes the Zoom H4n RC04/RC2 remote protocol's inbound status byte, per
/// docs/design/proposals/zoom-h4n-remote-protocol.md: a single byte, sent unprompted, is a bitmask
/// (0x01 Record LED, 0x02 Peak, 0x10 Mic LED, 0x20 Led1, 0x40 Led2) — unlike
/// <c>DevTerm.Devices.K8055</c>'s multi-byte frame, each byte is a complete, independent status
/// snapshot, so nothing is buffered across reads. Bits 0x04/0x08/0x80 have no documented meaning
/// (0x80 is the handshake's own high-bit wake signal, consumed separately by
/// <see cref="ZoomH4nControlSurface"/>'s init handshake, not a status bit) and are ignored here.
/// Implements <see cref="DevTerm.Core.Presenters.IStructuredPresenter"/> so a generic control-panel
/// renderer can drive the 5 status indicators <see cref="ZoomH4nUiDefinition"/> declares (ids
/// prefixed "status" to avoid colliding with the button command ids of the same underlying words,
/// e.g. "record" the button vs. "statusRecord" the indicator).
/// </summary>
public sealed class ZoomH4nDecoder : IPresenter, IStructuredPresenter
{
    private readonly Dictionary<string, string> _lastValues = [];

    public string Name => "zoomh4n";

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        var lines = new List<string>();
        foreach (var segment in data)
        {
            foreach (var status in segment.Span)
            {
                lines.Add(DecodeByte(status, out var values));

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

        return lines;
    }

    private static string DecodeByte(byte status, out IReadOnlyDictionary<string, string> values)
    {
        var record = (status & 0x01) != 0;
        var peak = (status & 0x02) != 0;
        var mic = (status & 0x10) != 0;
        var led1 = (status & 0x20) != 0;
        var led2 = (status & 0x40) != 0;

        values = new Dictionary<string, string>
        {
            ["statusRecord"] = record ? "1" : "0",
            ["statusPeak"] = peak ? "1" : "0",
            ["statusMic"] = mic ? "1" : "0",
            ["statusLed1"] = led1 ? "1" : "0",
            ["statusLed2"] = led2 ? "1" : "0",
        };

        var flags = new List<string>();
        if (record)
        {
            flags.Add("Record");
        }

        if (peak)
        {
            flags.Add("Peak");
        }

        if (mic)
        {
            flags.Add("Mic");
        }

        if (led1)
        {
            flags.Add("Led1");
        }

        if (led2)
        {
            flags.Add("Led2");
        }

        return flags.Count == 0 ? "Status: (none)" : $"Status: {string.Join(" | ", flags)}";
    }
}
