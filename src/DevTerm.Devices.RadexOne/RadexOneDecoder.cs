using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using DevTerm.Core.Presenters;

namespace DevTerm.Devices.RadexOne;

/// <summary>
/// Decodes a Radex One serial reply into human-readable text. Buffers incoming bytes across
/// however many transport reads a reply happens to arrive in — a real serial connection can split
/// even a single short reply across more than one <c>Session.Output</c> event (see CLAUDE.md's
/// "RawPresenter is only trustworthy for a genuinely terminatorless device" note: the device is a
/// plain virtual COM port, 2400 8N1, real-hardware confirmed 2026-09-25, not the USB HID device an
/// earlier draft of the proposal wrongly assumed, so there is no fixed-size report to rely on to
/// mark a reply's boundary) — then parses each complete framer packet (see
/// <see cref="RadexOneFramer"/>) once enough bytes have arrived. A byte sequence that can't start a
/// valid reply (wrong prefix, or a checksum mismatch once a full candidate packet is buffered) is
/// dropped one byte at a time so the decoder resynchronizes on the next real reply instead of
/// getting stuck.
/// </summary>
public sealed class RadexOneDecoder : IPresenter
{
    public string Name => "radexone";

    private readonly List<byte> _buffer = [];

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        if (!data.IsEmpty)
        {
            _buffer.AddRange(data.ToArray());
        }

        List<string>? results = null;

        while (true)
        {
            while (_buffer.Count > 0 && _buffer[0] != RadexOneFramer.InboundPrefix0)
            {
                _buffer.RemoveAt(0);
            }

            if (_buffer.Count < 2)
            {
                break;
            }

            if (_buffer[1] != RadexOneFramer.InboundPrefix1)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            if (_buffer.Count < RadexOneFramer.HeaderLength)
            {
                break;
            }

            var header = CollectionsMarshal.AsSpan(_buffer)[..RadexOneFramer.HeaderLength];
            var extensionLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
            var totalLength = RadexOneFramer.HeaderLength + extensionLength;

            if (_buffer.Count < totalLength)
            {
                break;
            }

            var packet = CollectionsMarshal.AsSpan(_buffer)[..totalLength];

            if (!RadexOneFramer.TryParseReply(packet, out _, out var extension))
            {
                (results ??= []).Add($"RADEX-ONE: unrecognized reply ({totalLength} byte(s) — checksum mismatch or wrong prefix)");
                _buffer.RemoveAt(0);
                continue;
            }

            (results ??= []).Add(Format(extension));
            _buffer.RemoveRange(0, totalLength);
        }

        return (IReadOnlyList<string>?)results ?? [];
    }

    private static string Format(byte[] extension)
    {
        if (!RadexOneExtensionCodec.TryReadCommandCode(extension, out var commandCode))
        {
            return $"RADEX-ONE: reply with {extension.Length} byte(s) (too short to identify command)";
        }

        return commandCode switch
        {
            RadexOneCommand.ReadData when RadexOneExtensionCodec.TryParseReadData(extension, out var ambient, out var accumulated, out var cpm) =>
                FormattableString.Invariant($"RADEX-ONE: CPM={cpm} Ambient={ambient} Accum={accumulated}"),
            RadexOneCommand.ReadSerialVersion => $"RADEX-ONE: {DecodeAscii(RadexOneExtensionCodec.ReadSerialVersionPayload(extension))}",
            RadexOneCommand.ReadSettings when RadexOneExtensionCodec.TryParseReadSettings(extension, out var alarmMode, out var threshold) =>
                FormatSettings(alarmMode, threshold),
            RadexOneCommand.WriteSettings => "RADEX-ONE: write settings acknowledged",
            _ => $"RADEX-ONE: reply command 0x{commandCode:X4}, {extension.Length} byte(s)",
        };
    }

    private static string FormatSettings(byte alarmMode, ushort threshold)
    {
        var alarmText = alarmMode switch
        {
            0 => "Off",
            1 => "Vibration",
            2 => "Audio",
            3 => "Vibration+Audio",
            var mode => $"0x{mode:X2}",
        };

        return $"RADEX-ONE: alarm={alarmText} threshold={threshold.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string DecodeAscii(ReadOnlySpan<byte> payload)
    {
        var chars = new char[payload.Length];
        for (var i = 0; i < payload.Length; i++)
        {
            chars[i] = payload[i] is >= 0x20 and < 0x7F ? (char)payload[i] : '.';
        }

        return new string(chars);
    }
}
