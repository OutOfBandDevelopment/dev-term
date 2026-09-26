namespace DevTerm.Devices.RadexOne;

/// <summary>
/// The Read Data reply's ambient, accumulated, and CPM readings, each a little-endian 16-bit value —
/// see <see cref="RadexOneExtensionCodec.TryParseReadData"/> for the real extension layout these are
/// read from (verified byte-for-byte against a source-doc trace 2026-09-25; an earlier draft of this
/// type assumed a flat 6-byte extension, which was wrong). The exact units (µR/h vs µSv/h for
/// ambient/accumulated) aren't specified in the source doc and aren't asserted here.
/// </summary>
public readonly record struct RadexOneReadData(ushort Ambient, ushort Accumulated, ushort Cpm)
{
    /// <summary>The minimum extension length <see cref="RadexOneExtensionCodec.TryParseReadData"/> requires.</summary>
    public const int ExtensionLength = 22;

    public static RadexOneReadData Parse(ReadOnlySpan<byte> extension)
    {
        if (!RadexOneExtensionCodec.TryParseReadData(extension, out var ambient, out var accumulated, out var cpm))
        {
            throw new ArgumentException($"Read Data extension is {extension.Length} byte(s), expected at least {ExtensionLength}.", nameof(extension));
        }

        return new RadexOneReadData(ambient, accumulated, cpm);
    }
}
