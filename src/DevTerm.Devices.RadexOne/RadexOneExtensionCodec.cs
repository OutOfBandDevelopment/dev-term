using System.Buffers.Binary;

namespace DevTerm.Devices.RadexOne;

/// <summary>
/// Builds/parses each command's own nested sub-packet — the framer's Extension field (see
/// <see cref="RadexOneFramer"/>'s remarks for why this, not the outer header, carries the real
/// command code). Every shape and checksum below was re-derived byte-for-byte against the source
/// reverse-engineering doc's raw example traces (docs/design/proposals/radex-one-protocol.md), not
/// just its prose field list, since the prose and the traces disagree on some reserved-byte
/// placement (most visibly for Read Serial/Version, see <see cref="ReadSerialVersionPayload"/>).
/// </summary>
internal static class RadexOneExtensionCodec
{
    /// <summary>
    /// Read Data / Read Serial+Version / Read Settings requests share this 6-byte shape:
    /// CommandCode(2) + Reserved(2, always 0x000C) + Checksum(2, covering the first 4 bytes).
    /// </summary>
    public static byte[] BuildQuery(ushort commandCode)
    {
        var extension = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(extension, commandCode);
        BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(2), 0x000C);
        BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(4), RadexOneFramer.ComputeChecksum(extension.AsSpan(0, 4)));
        return extension;
    }

    /// <summary>
    /// Write Settings request: CommandCode(2) + Reserved(2, 0x000E) + TargetValue(2, 0x0005) +
    /// ZeroReserved(2) + AlarmSetting(1) + Threshold(2, LE) + ZeroReserved(3) + Checksum(2, covering
    /// the first 14 bytes) = 16 bytes total. The alarm/threshold fields aren't 16-bit-aligned within
    /// the extension — the checksum still just walks the covered bytes two at a time from offset 0,
    /// crossing that field boundary, per the trace this was verified against.
    /// </summary>
    public static byte[] BuildWriteSettings(byte alarmMode, ushort threshold)
    {
        var extension = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(extension, RadexOneCommand.WriteSettings);
        BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(2), 0x000E);
        BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(4), 0x0005);
        extension[8] = alarmMode;
        BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(9), threshold);
        BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(14), RadexOneFramer.ComputeChecksum(extension.AsSpan(0, 14)));
        return extension;
    }

    /// <summary>
    /// Reads the command code every reply extension leads with, regardless of which of the four
    /// commands it is — the decoder dispatches on this rather than the framer's outer (constant)
    /// Type field.
    /// </summary>
    public static bool TryReadCommandCode(ReadOnlySpan<byte> extension, out ushort commandCode)
    {
        if (extension.Length < 2)
        {
            commandCode = 0;
            return false;
        }

        commandCode = BinaryPrimitives.ReadUInt16LittleEndian(extension);
        return true;
    }

    /// <summary>
    /// Read Data reply: CommandCode(2) + Reserved(2) + Reserved(2, 0x000C) + Reserved(2) +
    /// Ambient(2) + Reserved(2) + Accumulated(2) + Reserved(2) + CPM(2) + Reserved(2) + Checksum(2)
    /// = 22 bytes. Each real value is followed by a reserved zero word — verified against the source
    /// trace's own worked example (Ambient=Accumulated=0x12, CPM=0x15). The trailing checksum covers
    /// the first 20 bytes and is verified here — the outer framer's checksum only guarantees the
    /// outer header arrived intact, not this extension's own payload (see
    /// docs/bugs/fixed/021-radexone-extension-checksum-unverified.md).
    /// </summary>
    public static bool TryParseReadData(ReadOnlySpan<byte> extension, out ushort ambient, out ushort accumulated, out ushort cpm)
    {
        ambient = 0;
        accumulated = 0;
        cpm = 0;

        if (extension.Length < 22 || !HasValidChecksum(extension, coveredLength: 20))
        {
            return false;
        }

        ambient = BinaryPrimitives.ReadUInt16LittleEndian(extension[8..]);
        accumulated = BinaryPrimitives.ReadUInt16LittleEndian(extension[12..]);
        cpm = BinaryPrimitives.ReadUInt16LittleEndian(extension[16..]);
        return true;
    }

    /// <summary>
    /// Read Settings reply: CommandCode(2) + ZeroReserved(2) + TargetValue(2, 0x0005) +
    /// ZeroReserved(2) + AlarmSetting(1) + Threshold(2, LE) + ZeroReserved(3) + Checksum(2) = 16
    /// bytes — the same shape as <see cref="BuildWriteSettings"/>'s request, minus the leading
    /// Reserved(0x000E) field (replaced here by a zero word). The trailing checksum covers the first
    /// 14 bytes and is verified here (see
    /// docs/bugs/fixed/021-radexone-extension-checksum-unverified.md).
    /// </summary>
    public static bool TryParseReadSettings(ReadOnlySpan<byte> extension, out byte alarmMode, out ushort threshold)
    {
        alarmMode = 0;
        threshold = 0;

        if (extension.Length < 16 || !HasValidChecksum(extension, coveredLength: 14))
        {
            return false;
        }

        alarmMode = extension[8];
        threshold = BinaryPrimitives.ReadUInt16LittleEndian(extension[9..]);
        return true;
    }

    /// <summary>
    /// Write Settings ack: CommandCode(2) echo + ZeroReserved(2) + Checksum(2) = 6 bytes. The
    /// trailing checksum covers the first 4 bytes and is verified here (see
    /// docs/bugs/fixed/021-radexone-extension-checksum-unverified.md).
    /// </summary>
    public static bool TryVerifyWriteSettingsAck(ReadOnlySpan<byte> extension) =>
        extension.Length >= 6 && HasValidChecksum(extension, coveredLength: 4);

    /// <summary>
    /// Read Serial/Version reply's variable-length payload, per the doc's own field list:
    /// CommandCode(2) + Reserved(2, 0x000C) + payload(variable) + Checksum(2). The source trace's own
    /// exact reserved-byte content past the first 4 bytes doesn't fully reconcile against the doc's
    /// prose field list (one reserved word differs from a plain zero in a way the doc doesn't
    /// document), so this deliberately doesn't re-validate this extension's own inner checksum —
    /// unlike <see cref="TryParseReadData"/>/<see cref="TryParseReadSettings"/>/
    /// <see cref="TryVerifyWriteSettingsAck"/>, which do (see
    /// docs/bugs/fixed/021-radexone-extension-checksum-unverified.md); this just slices out the
    /// middle for display.
    /// </summary>
    public static ReadOnlySpan<byte> ReadSerialVersionPayload(ReadOnlySpan<byte> extension) =>
        extension.Length < 6 ? [] : extension[4..^2];

    private static bool HasValidChecksum(ReadOnlySpan<byte> extension, int coveredLength) =>
        BinaryPrimitives.ReadUInt16LittleEndian(extension.Slice(coveredLength, 2)) ==
            RadexOneFramer.ComputeChecksum(extension[..coveredLength]);
}
