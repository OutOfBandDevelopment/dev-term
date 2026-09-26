using System.Buffers.Binary;

namespace DevTerm.Devices.RadexOne;

/// <summary>
/// The Read Data reply's extension: ambient, accumulated, and CPM readings, each a little-endian
/// 16-bit value, per docs/design/proposals/radex-one-protocol.md. Field order (ambient, then
/// accumulated, then CPM) matches that proposal's own summary of the reply; the exact units
/// (µR/h vs µSv/h for ambient/accumulated) aren't specified there and aren't asserted here.
/// </summary>
public readonly record struct RadexOneReadData(ushort Ambient, ushort Accumulated, ushort Cpm)
{
    public const int ExtensionLength = 6;

    public static RadexOneReadData Parse(ReadOnlySpan<byte> extension)
    {
        if (extension.Length < ExtensionLength)
        {
            throw new ArgumentException($"Read Data extension is {extension.Length} byte(s), expected at least {ExtensionLength}.", nameof(extension));
        }

        return new RadexOneReadData(
            BinaryPrimitives.ReadUInt16LittleEndian(extension),
            BinaryPrimitives.ReadUInt16LittleEndian(extension[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(extension[4..]));
    }
}
