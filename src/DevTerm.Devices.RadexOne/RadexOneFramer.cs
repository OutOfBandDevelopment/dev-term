using System.Buffers.Binary;

namespace DevTerm.Devices.RadexOne;

/// <summary>
/// The four Radex One command/reply type codes, per
/// docs/design/proposals/radex-one-protocol.md. Reused as-is for both directions — the reply to a
/// query is assumed to echo the same type code as the request that produced it (only the packet's
/// leading prefix byte pair differs, per <see cref="RadexOneFramer"/>); the source reverse-engineering
/// writeup this proposal is drawn from doesn't spell out a separate reply-side code, so this is an
/// assumption pending real-hardware verification, same as the HID report framing below.
/// </summary>
public static class RadexOneCommand
{
    public const ushort ReadData = 0x0800;
    public const ushort ReadSerialVersion = 0x0001;
    public const ushort WriteSettings = 0x0802;
    public const ushort ReadSettings = 0x0801;
}

/// <summary>
/// Builds and parses the Radex One's packet framing (shared by request and reply), per
/// docs/design/proposals/radex-one-protocol.md:
/// <c>Prefix(2) + Type(2, LE) + ExtensionLength(2, LE) + PacketNumber(2, LE) + Reserved(2, 0x00 0x00)
/// + Checksum(2, LE) + Extension(variable)</c>. The checksum covers the 10 bytes from Prefix through
/// Reserved: <c>0xFFFF - sum(those bytes)</c> — the max possible sum of 10 bytes (2550) never
/// approaches 0xFFFF, so the "% FFFF" the source doc mentions never actually triggers a wraparound in
/// practice; this still matches the doc's literal wording.
///
/// <para>This is the packet payload only — how it's wrapped inside a USB HID report (report ID,
/// fixed report length) is a separate, unconfirmed assumption; see
/// <see cref="RadexOneHidFraming"/>.</para>
/// </summary>
public static class RadexOneFramer
{
    private const byte _outboundPrefix0 = 0x7B;
    private const byte _outboundPrefix1 = 0xFF;
    private const byte _inboundPrefix0 = 0x7A;
    private const byte _inboundPrefix1 = 0xFF;

    /// <summary>Prefix(2) + Type(2) + ExtensionLength(2) + PacketNumber(2) + Reserved(2) — the span the checksum covers.</summary>
    private const int _checksumCoveredLength = 10;

    private const int _checksumOffset = _checksumCoveredLength;
    private const int _extensionOffset = _checksumOffset + 2;

    /// <summary>Builds an outbound (0x7B 0xFF-prefixed) request packet — the framer-level payload, not yet HID-wrapped.</summary>
    public static byte[] BuildRequest(ushort type, ushort packetNumber, ReadOnlySpan<byte> extension) =>
        Build(_outboundPrefix0, _outboundPrefix1, type, packetNumber, extension);

    /// <summary>Builds an inbound (0x7A 0xFF-prefixed) reply packet — exposed for tests that need a well-formed reply to feed the decoder.</summary>
    public static byte[] BuildReply(ushort type, ushort packetNumber, ReadOnlySpan<byte> extension) =>
        Build(_inboundPrefix0, _inboundPrefix1, type, packetNumber, extension);

    private static byte[] Build(byte prefix0, byte prefix1, ushort type, ushort packetNumber, ReadOnlySpan<byte> extension)
    {
        var packet = new byte[_extensionOffset + extension.Length];
        packet[0] = prefix0;
        packet[1] = prefix1;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), type);
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
    /// trailing zero-padding past the packet's own declared <c>ExtensionLength</c> (e.g. from a fixed-size
    /// HID report) — anything beyond the parsed extension is ignored rather than treated as an error.
    /// Returns false for a short buffer, a wrong prefix, or a checksum mismatch (a real transport-level
    /// anomaly, not malformed decoder input — see the proposal's own "checksum validation belongs in the
    /// framer" note).
    /// </summary>
    public static bool TryParseReply(ReadOnlySpan<byte> buffer, out ushort type, out ushort packetNumber, out byte[] extension)
    {
        type = 0;
        packetNumber = 0;
        extension = [];

        if (buffer.Length < _extensionOffset || buffer[0] != _inboundPrefix0 || buffer[1] != _inboundPrefix1)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(_checksumOffset, 2)) != ComputeChecksum(buffer[.._checksumCoveredLength]))
        {
            return false;
        }

        type = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2));
        var extensionLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2));
        packetNumber = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));

        if (buffer.Length < _extensionOffset + extensionLength)
        {
            return false;
        }

        extension = buffer.Slice(_extensionOffset, extensionLength).ToArray();
        return true;
    }

    private static ushort ComputeChecksum(ReadOnlySpan<byte> coveredBytes)
    {
        var sum = 0;
        foreach (var b in coveredBytes)
        {
            sum += b;
        }

        return (ushort)(0xFFFF - (sum % 0xFFFF));
    }
}
