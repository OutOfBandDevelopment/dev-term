using System.Buffers.Binary;

namespace DevTerm.Devices.RadexOne;

/// <summary>
/// The five Radex One command codes, per docs/design/proposals/radex-one-protocol.md. These live
/// inside the framer's Extension field (see <see cref="RadexOneFramer"/>'s remarks) — not, as an
/// earlier draft of this module assumed, in the outer header's Type field, which is actually a
/// constant marker shared by every command. A reply echoes the same code as the request that
/// produced it (real-hardware confirmed 2026-09-25 against a Radex One on COM8).
/// </summary>
public static class RadexOneCommand
{
    public const ushort ReadData = 0x0800;
    public const ushort ReadSerialVersion = 0x0001;
    public const ushort WriteSettings = 0x0802;
    public const ushort ReadSettings = 0x0801;

    /// <summary>
    /// Clears the device's accumulated-dose counter. Discovered from a user-captured trace (see
    /// docs/design/proposals/radex-one-protocol.md's "Trace Examples"), not the original
    /// reverse-engineering doc — request extension shares <see cref="RadexOneExtensionCodec.BuildQuery"/>'s
    /// 6-byte query shape but with its second word 0x0001, not the other three queries' 0x000C.
    /// </summary>
    public const ushort ResetAccumulated = 0x0803;
}

/// <summary>
/// Builds and parses the Radex One's outer packet envelope (shared by request and reply), per
/// docs/design/proposals/radex-one-protocol.md:
/// <c>Prefix(2) + Type(2, LE) + ExtensionLength(2, LE) + PacketNumber(2, LE) + Reserved(2, 0x00 0x00)
/// + Checksum(2, LE) + Extension(variable)</c>.
///
/// <para><b>Corrected 2026-09-25 against the source reverse-engineering doc's own raw traces</b> (an
/// earlier draft of this framer got two things wrong, discovered only once byte-exact examples were
/// checked by hand rather than trusting the doc's prose field list):</para>
/// <list type="bullet">
/// <item>The outer header's Type field is a <b>constant marker</b> — <c>0x0020</c> outbound,
/// <c>0x8020</c> inbound — not a per-command code. The real command code is the first word of the
/// Extension itself; see <see cref="RadexOneExtensionCodec"/>.</item>
/// <item>The checksum is a <b>word-sum</b>, not a byte-sum: read the covered range as consecutive
/// little-endian 16-bit words and sum those, then <c>0xFFFF - (sum % 0xFFFF)</c>. The doc's own
/// "% FFFF" is not a no-op the way a byte-sum's tiny max total made it look — verified against a real
/// reply trace whose outer-header word-sum exceeds 0xFFFF and only matches the trace's actual
/// checksum bytes once the modulo is applied.</item>
/// </list>
/// <para>Both corrections were confirmed by manually re-deriving several of the source doc's example
/// packets (Read Data, Read Settings, Write Settings, both directions) byte-for-byte against this
/// exact formula — every one matched, including the one that requires the modulo to wrap.</para>
///
/// <para>The device is a plain virtual COM port (2400 8N1, real-hardware confirmed 2026-09-25), not a
/// USB HID device as an earlier draft of the proposal wrongly claimed, so there is no report wrapping
/// to account for here.</para>
/// </summary>
public static class RadexOneFramer
{
    /// <summary>The reply prefix pair a stream-buffering reader (see <see cref="RadexOneDecoder"/>) scans for to resynchronize.</summary>
    public const byte InboundPrefix0 = 0x7A;
    public const byte InboundPrefix1 = 0xFF;

    private const byte _outboundPrefix0 = 0x7B;
    private const byte _outboundPrefix1 = 0xFF;
    private const byte _inboundPrefix0 = InboundPrefix0;
    private const byte _inboundPrefix1 = InboundPrefix1;

    /// <summary>The outer header's Type field is this constant on every outbound request, regardless of command.</summary>
    private const ushort _outboundTypeMarker = 0x0020;

    /// <summary>The outer header's Type field is this constant on every inbound reply, regardless of command.</summary>
    private const ushort _inboundTypeMarker = 0x8020;

    /// <summary>Prefix(2) + Type(2) + ExtensionLength(2) + PacketNumber(2) + Reserved(2) — the span the checksum covers.</summary>
    private const int _checksumCoveredLength = 10;

    private const int _checksumOffset = _checksumCoveredLength;
    private const int _extensionOffset = _checksumOffset + 2;

    /// <summary>Prefix through Checksum — the fixed-size header a stream-buffering reader needs before it can learn the total packet length from ExtensionLength.</summary>
    public const int HeaderLength = _extensionOffset;

    /// <summary>Builds an outbound (0x7B 0xFF-prefixed) request packet around an already-built command extension (see <see cref="RadexOneExtensionCodec"/>).</summary>
    public static byte[] BuildRequest(ushort packetNumber, ReadOnlySpan<byte> extension) =>
        Build(_outboundPrefix0, _outboundPrefix1, _outboundTypeMarker, packetNumber, extension);

    /// <summary>Builds an inbound (0x7A 0xFF-prefixed) reply packet — exposed for tests that need a well-formed reply to feed the decoder.</summary>
    public static byte[] BuildReply(ushort packetNumber, ReadOnlySpan<byte> extension) =>
        Build(_inboundPrefix0, _inboundPrefix1, _inboundTypeMarker, packetNumber, extension);

    private static byte[] Build(byte prefix0, byte prefix1, ushort typeMarker, ushort packetNumber, ReadOnlySpan<byte> extension)
    {
        var packet = new byte[_extensionOffset + extension.Length];
        packet[0] = prefix0;
        packet[1] = prefix1;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), typeMarker);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), (ushort)extension.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), packetNumber);
        packet[8] = 0x00;
        packet[9] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(_checksumOffset), ComputeChecksum(packet.AsSpan(0, _checksumCoveredLength)));
        extension.CopyTo(packet.AsSpan(_extensionOffset));
        return packet;
    }

    /// <summary>
    /// Parses an inbound (0x7A 0xFF-prefixed) reply out of <paramref name="buffer"/>, which may carry
    /// trailing bytes past the packet's own declared <c>ExtensionLength</c> — anything beyond the
    /// parsed extension is ignored rather than treated as an error. Returns false for a short buffer,
    /// a wrong prefix or type marker, or a checksum mismatch (a real transport-level anomaly, not
    /// malformed decoder input — see the proposal's own "checksum validation belongs in the framer"
    /// note). The returned <paramref name="extension"/> starts with its own command code word — see
    /// <see cref="RadexOneExtensionCodec"/> to decode the rest.
    /// </summary>
    public static bool TryParseReply(ReadOnlySpan<byte> buffer, out ushort packetNumber, out byte[] extension)
    {
        packetNumber = 0;
        extension = [];

        if (buffer.Length < _extensionOffset || buffer[0] != _inboundPrefix0 || buffer[1] != _inboundPrefix1)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2)) != _inboundTypeMarker)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(_checksumOffset, 2)) != ComputeChecksum(buffer[.._checksumCoveredLength]))
        {
            return false;
        }

        var extensionLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2));
        packetNumber = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));

        if (buffer.Length < _extensionOffset + extensionLength)
        {
            return false;
        }

        extension = buffer.Slice(_extensionOffset, extensionLength).ToArray();
        return true;
    }

    /// <summary>
    /// <c>0xFFFF - (sum(words) % 0xFFFF)</c>, where the covered bytes are read as consecutive
    /// little-endian 16-bit words (so <paramref name="coveredBytes"/>'s length must be even — every
    /// real covered range in this protocol is). Shared with <see cref="RadexOneExtensionCodec"/>,
    /// whose per-command extensions carry their own trailing checksum computed the same way over
    /// their own preceding bytes.
    /// </summary>
    internal static ushort ComputeChecksum(ReadOnlySpan<byte> coveredBytes)
    {
        var sum = 0;
        for (var i = 0; i + 1 < coveredBytes.Length; i += 2)
        {
            sum += BinaryPrimitives.ReadUInt16LittleEndian(coveredBytes.Slice(i, 2));
        }

        return (ushort)(0xFFFF - (sum % 0xFFFF));
    }
}
