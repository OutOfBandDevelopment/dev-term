using System.Buffers;
using DevTerm.Test.Utilities;

namespace DevTerm.Devices.RadexOne.Tests;

/// <summary>
/// Feeds <see cref="RadexOneDecoder"/> sample replies (built via <see cref="RadexOneFramer.BuildReply"/>
/// and <see cref="RadexOneExtensionCodec"/>) and asserts the rendered text — no real device involved,
/// so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Serial)]
[TestCategory(TestCategories.Radex_One)]
[TestClass]
public sealed class RadexOneDecoderTests
{
    [TestMethod]
    public void Render_WithEmptySequence_ProducesNoLines()
    {
        var decoder = new RadexOneDecoder();

        var lines = decoder.Render(ReadOnlySequence<byte>.Empty);

        Assert.IsEmpty(lines);
    }

    [TestMethod]
    public void Render_WithReadDataReply_FormatsAmbientAccumAndCpm()
    {
        var decoder = new RadexOneDecoder();
        var extension = BuildReadDataExtension(ambient: 10, accumulated: 20, cpm: 30);
        var report = RadexOneFramer.BuildReply(1, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.HasCount(1, lines);
        Assert.AreEqual("RADEX-ONE: CPM=30 Ambient=10 Accum=20", lines[0]);
    }

    [TestMethod]
    public void Render_WithSerialVersionReply_RendersAsciiText()
    {
        var decoder = new RadexOneDecoder();
        var payload = "RD1706123"u8.ToArray();
        var extension = new byte[4 + payload.Length + 2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension, RadexOneCommand.ReadSerialVersion);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(2), 0x000C);
        payload.CopyTo(extension.AsSpan(4));
        var report = RadexOneFramer.BuildReply(1, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.AreEqual("RADEX-ONE: RD1706123", lines[0]);
    }

    [TestMethod]
    public void Render_WithSettingsReply_FormatsAlarmModeAndThreshold()
    {
        var decoder = new RadexOneDecoder();
        var extension = BuildReadSettingsExtension(alarmMode: 2, threshold: 300); // Audio
        var report = RadexOneFramer.BuildReply(1, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.AreEqual("RADEX-ONE: alarm=Audio threshold=300", lines[0]);
    }

    [TestMethod]
    public void Render_WithBadChecksum_ProducesADiagnosticLineInsteadOfThrowing()
    {
        var decoder = new RadexOneDecoder();
        var extension = BuildReadDataExtension(ambient: 10, accumulated: 20, cpm: 30);
        var report = RadexOneFramer.BuildReply(1, extension);
        report[10] ^= 0xFF; // corrupt the outer header checksum

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.HasCount(1, lines);
        StringAssert.Contains(lines[0], "unrecognized reply");
    }

    [TestMethod]
    public void Render_WhenReplySplitsAcrossTwoReads_StillDecodes()
    {
        var decoder = new RadexOneDecoder();
        var extension = BuildReadDataExtension(ambient: 10, accumulated: 20, cpm: 30);
        var report = RadexOneFramer.BuildReply(1, extension);
        var splitAt = report.Length / 2;

        var first = decoder.Render(new ReadOnlySequence<byte>(report[..splitAt]));
        Assert.IsEmpty(first);

        var second = decoder.Render(new ReadOnlySequence<byte>(report[splitAt..]));

        Assert.HasCount(1, second);
        Assert.AreEqual("RADEX-ONE: CPM=30 Ambient=10 Accum=20", second[0]);
    }

    private static byte[] BuildReadDataExtension(ushort ambient, ushort accumulated, ushort cpm)
    {
        var extension = new byte[22];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension, RadexOneCommand.ReadData);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(4), 0x000C);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(8), ambient);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(12), accumulated);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(16), cpm);
        return extension;
    }

    private static byte[] BuildReadSettingsExtension(byte alarmMode, ushort threshold)
    {
        var extension = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension, RadexOneCommand.ReadSettings);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(4), 0x0005);
        extension[8] = alarmMode;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(9), threshold);
        return extension;
    }
}
