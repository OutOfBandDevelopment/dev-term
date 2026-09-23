using System.Buffers;
using System.Globalization;
using DevTerm.Core.Presenters;

namespace DevTerm.Devices.K8055;

/// <summary>
/// Decodes the Velleman K8055's unprompted 9-byte input report (confirmed against real hardware —
/// see docs/design/proposals/velleman-k8055-protocol.md): [00, 00, 03, AnalogIn1, AnalogIn2,
/// CounterLo1, CounterHi1, CounterLo2, CounterHi2]. Implements <see cref="IStructuredPresenter"/> so
/// a generic control-panel renderer can drive live indicators (keyed by the same ids
/// <see cref="K8055UiDefinition"/> declares) without parsing this presenter's own rendered text.
/// </summary>
/// <remarks>
/// Digital-input bit-to-channel mapping is unconfirmed — nothing was wired to the 5 digital-input
/// pins during the original real-hardware check. Rather than guess 5 individual channel keys, byte 0
/// is published raw as "digitalInRaw" (hex); confirm the real per-bit mapping by wiring one input at
/// a time and observing which bit flips, then add named channel keys/indicators once confirmed.
/// </remarks>
public sealed class K8055Decoder : IPresenter, IStructuredPresenter
{
    private const int FrameLength = 9;

    private readonly List<byte> _buffer = [];

    public string Name => "k8055";

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        foreach (var segment in data)
        {
            _buffer.AddRange(segment.Span);
        }

        var lines = new List<string>();
        while (_buffer.Count >= FrameLength)
        {
            var frame = _buffer.GetRange(0, FrameLength);
            _buffer.RemoveRange(0, FrameLength);
            lines.Add(DecodeFrame(frame, out var values));
            ValuesChanged?.Invoke(this, values);
        }

        return lines;
    }

    private static string DecodeFrame(IReadOnlyList<byte> frame, out IReadOnlyDictionary<string, string> values)
    {
        var digitalInRaw = frame[0];
        var analogIn1 = frame[3];
        var analogIn2 = frame[4];
        var counter1 = (ushort)(frame[5] | (frame[6] << 8));
        var counter2 = (ushort)(frame[7] | (frame[8] << 8));

        var digitalInRawText = $"0x{digitalInRaw:X2}";
        var analogIn1Text = analogIn1.ToString(CultureInfo.InvariantCulture);
        var analogIn2Text = analogIn2.ToString(CultureInfo.InvariantCulture);
        var counter1Text = counter1.ToString(CultureInfo.InvariantCulture);
        var counter2Text = counter2.ToString(CultureInfo.InvariantCulture);

        values = new Dictionary<string, string>
        {
            ["digitalInRaw"] = digitalInRawText,
            ["analogIn1"] = analogIn1Text,
            ["analogIn2"] = analogIn2Text,
            ["counter1"] = counter1Text,
            ["counter2"] = counter2Text,
        };

        return $"IN: D={digitalInRawText} A1={analogIn1Text} A2={analogIn2Text} C1={counter1Text} C2={counter2Text}";
    }
}
