using System.Buffers.Binary;

namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// Pure encode/decode for USBTMC bulk-transfer headers (USBTMC 1.0 section 3.2) - no device I/O,
/// so it's unit-testable without real hardware per docs/design/usbtmc-transport.md's testing
/// strategy. Each header is 12 bytes: MsgID, bTag, ~bTag, a reserved byte, a 4-byte little-endian
/// TransferSize, an attributes byte (EOM for OUT/IN, TermCharEnabled for a read request), a
/// TermChar byte, and 2 reserved bytes.
/// </summary>
public static class UsbtmcCodec
{
    public const byte DevDepMsgOut = 1;
    public const byte RequestDevDepMsgIn = 2;
    public const byte DevDepMsgIn = 2;

    public const int HeaderSize = 12;

    private const byte _eomBit = 0x01;
    private const byte _termCharEnabledBit = 0x02;

    /// <summary>Encodes a host-to-device DEV_DEP_MSG_OUT transfer: header + payload padded to a 4-byte boundary.</summary>
    public static byte[] EncodeDevDepMsgOut(byte bTag, ReadOnlySpan<byte> payload, bool eom)
    {
        var padded = PadTo4(payload.Length);
        var buffer = new byte[HeaderSize + padded];
        WriteHeader(buffer, DevDepMsgOut, bTag, payload.Length, eom ? _eomBit : (byte)0, termChar: 0);
        payload.CopyTo(buffer.AsSpan(HeaderSize));
        return buffer;
    }

    /// <summary>Encodes a host-to-device REQUEST_DEV_DEP_MSG_IN transfer requesting up to <paramref name="maxTransferSize"/> bytes back.</summary>
    public static byte[] EncodeRequestDevDepMsgIn(byte bTag, int maxTransferSize, byte termChar, bool termCharEnabled)
    {
        var buffer = new byte[HeaderSize];
        WriteHeader(buffer, RequestDevDepMsgIn, bTag, maxTransferSize, termCharEnabled ? _termCharEnabledBit : (byte)0, termChar);
        return buffer;
    }

    /// <summary>The decoded fields of one bulk-IN DEV_DEP_MSG_IN transfer's 12-byte header.</summary>
    public readonly record struct DecodedHeader(byte MsgId, byte BTag, int TransferSize, bool Eom);

    /// <summary>
    /// Decodes the header of one bulk-IN transfer. Throws if <paramref name="transfer"/> is
    /// shorter than <see cref="HeaderSize"/>, if MsgID isn't <see cref="DevDepMsgIn"/>, if the
    /// bTag/~bTag consistency check fails, or if bTag doesn't match <paramref name="expectedTag"/>
    /// (a desync - e.g. after a prior timeout/abort left a stale transfer in the pipe). Only ever
    /// call this on the *first* physical transfer of a logical response - a continuation transfer
    /// has no header at all, and re-decoding its raw payload bytes as a header is exactly the bug
    /// this validation exists to catch (see docs/design/proposals/usbtmc-lockup-fix-prompt.md).
    /// </summary>
    public static DecodedHeader DecodeHeader(ReadOnlySpan<byte> transfer, byte expectedTag)
    {
        var header = DecodeHeader(transfer);
        if (header.BTag != expectedTag)
        {
            throw new InvalidOperationException($"USBTMC bulk-IN header bTag {header.BTag} does not match expected {expectedTag} (desynced?).");
        }

        return header;
    }

    /// <summary>
    /// Decodes and structurally validates the header of one bulk-IN transfer without checking its
    /// bTag against a specific request - lets <see cref="UsbtmcTransport"/> recognize a
    /// well-formed but stale reply (one left over from an earlier, abandoned request) and skip
    /// it, rather than treating it the same as a corrupt header.
    /// </summary>
    public static DecodedHeader DecodeHeader(ReadOnlySpan<byte> transfer)
    {
        if (transfer.Length < HeaderSize)
        {
            throw new InvalidOperationException(
                $"USBTMC bulk-IN transfer of {transfer.Length} byte(s) is shorter than the {HeaderSize}-byte header.");
        }

        var msgId = transfer[0];
        var bTag = transfer[1];
        var bTagInverse = transfer[2];

        if (msgId != DevDepMsgIn)
        {
            throw new InvalidOperationException($"USBTMC bulk-IN header has unexpected MsgID {msgId} (expected {DevDepMsgIn}).");
        }

        if (bTagInverse != unchecked((byte)~bTag))
        {
            throw new InvalidOperationException($"USBTMC bulk-IN header failed bTag/~bTag consistency check (bTag={bTag}, ~bTag byte={bTagInverse}).");
        }

        var rawTransferSize = BinaryPrimitives.ReadUInt32LittleEndian(transfer[4..8]);
        if (rawTransferSize > int.MaxValue)
        {
            // Cast straight to int this wraps negative, sails past any MaxResponseSize check, and
            // silently turns into an empty reply.
            throw new InvalidOperationException($"USBTMC bulk-IN header declares an implausible TransferSize of {rawTransferSize} byte(s).");
        }

        var transferSize = (int)rawTransferSize;
        var eom = (transfer[8] & _eomBit) != 0;
        return new DecodedHeader(msgId, bTag, transferSize, eom);
    }

    /// <summary>
    /// Slices the payload out of one decoded bulk-IN transfer, clamped to however many bytes
    /// actually arrived (a short final transfer's declared TransferSize can exceed what's present).
    /// </summary>
    public static ReadOnlySpan<byte> ExtractPayload(ReadOnlySpan<byte> transfer, DecodedHeader header)
    {
        var available = transfer.Length - HeaderSize;
        var length = Math.Clamp(header.TransferSize, 0, Math.Max(0, available));
        return transfer.Slice(HeaderSize, length);
    }

    /// <summary>
    /// Sizes a bulk-IN read buffer: room for the header plus <paramref name="maxTransferSize"/>
    /// payload bytes, rounded up to a whole number of <paramref name="maxPacketSize"/> packets.
    /// A buffer whose tail can't hold a full packet makes libusb fail the read with an overflow
    /// as soon as a reply is long enough to reach it, and a read that fills the buffer exactly is
    /// the only way to tell "more of this transfer is coming" from "the device sent a short packet
    /// and the transfer is over".
    /// </summary>
    public static int BulkInBufferSize(int maxTransferSize, int maxPacketSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTransferSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPacketSize, 1);

        var wanted = (long)HeaderSize + maxTransferSize;
        var packets = (wanted + maxPacketSize - 1) / maxPacketSize;
        return checked((int)(packets * maxPacketSize));
    }

    /// <summary>Advances a bTag counter through the valid 1-255 range (0 is reserved by the USBTMC spec).</summary>
    public static byte NextTag(byte current) => current >= 0xFF ? (byte)1 : (byte)(current + 1);

    private static void WriteHeader(Span<byte> buffer, byte msgId, byte bTag, int transferSize, byte attributes, byte termChar)
    {
        buffer[0] = msgId;
        buffer[1] = bTag;
        buffer[2] = unchecked((byte)~bTag);
        buffer[3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], (uint)transferSize);
        buffer[8] = attributes;
        buffer[9] = termChar;
        buffer[10] = 0;
        buffer[11] = 0;
    }

    private static int PadTo4(int length) => (length + 3) & ~3;
}
