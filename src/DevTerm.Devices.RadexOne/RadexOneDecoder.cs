using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using DevTerm.Core.Presenters;

namespace DevTerm.Devices.RadexOne;

/// <summary>
/// Decodes a Radex One HID report into human-readable text: strips the assumed report-ID byte
/// (<see cref="RadexOneHidFraming"/>), parses the framer packet (<see cref="RadexOneFramer"/>), and
/// renders whichever of the four reply types it turns out to be. A checksum failure or unrecognized
/// prefix (a garbled/partial report, or the HID framing guess being wrong) surfaces as a single
/// diagnostic line rather than throwing — matching every other presenter's "never throw on bad
/// input" contract.
/// </summary>
public sealed class RadexOneDecoder : IPresenter
{
    public string Name => "radexone";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        var report = data.ToArray();
        var packet = RadexOneHidFraming.UnwrapReply(report);

        if (!RadexOneFramer.TryParseReply(packet, out var type, out _, out var extension))
        {
            return [$"RADEX-ONE: unrecognized reply ({report.Length} byte(s) — checksum mismatch or wrong prefix)"];
        }

        return
        [
            type switch
            {
                RadexOneCommand.ReadData when extension.Length >= RadexOneReadData.ExtensionLength => FormatReadData(RadexOneReadData.Parse(extension)),
                RadexOneCommand.ReadSerialVersion => $"RADEX-ONE: {DecodeAscii(extension)}",
                RadexOneCommand.ReadSettings or RadexOneCommand.WriteSettings when extension.Length >= 1 => FormatSettings(extension),
                _ => $"RADEX-ONE: reply type 0x{type:X4}, {extension.Length} byte(s)",
            },
        ];
    }

    private static string FormatReadData(RadexOneReadData reading) =>
        FormattableString.Invariant($"RADEX-ONE: CPM={reading.Cpm} Ambient={reading.Ambient} Accum={reading.Accumulated}");

    private static string FormatSettings(byte[] extension)
    {
        var alarmMode = extension[0] switch
        {
            0 => "Off",
            1 => "Vibration",
            2 => "Audio",
            3 => "Vibration+Audio",
            var mode => $"0x{mode:X2}",
        };

        var threshold = extension.Length >= 3
            ? BinaryPrimitives.ReadUInt16LittleEndian(extension.AsSpan(1, 2)).ToString(CultureInfo.InvariantCulture)
            : "?";

        return $"RADEX-ONE: alarm={alarmMode} threshold={threshold}";
    }

    private static string DecodeAscii(byte[] extension) =>
        new(Array.ConvertAll(extension, b => b is >= 0x20 and < 0x7F ? (char)b : '.'));
}
