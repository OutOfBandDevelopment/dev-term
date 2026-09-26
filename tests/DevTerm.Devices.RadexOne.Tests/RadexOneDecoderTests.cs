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
    public void Render_WithWriteSettingsAckReply_FormatsAcknowledgement()
    {
        var decoder = new RadexOneDecoder();
        var extension = BuildWriteSettingsAckExtension();
        var report = RadexOneFramer.BuildReply(1, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.AreEqual("RADEX-ONE: write settings acknowledged", lines[0]);
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
    [TestCategory(TestCategories.BugRegression)]
    public void Render_WithReadDataReply_AndCorruptedExtensionChecksum_IsNotShownAsAValidReading()
    {
        var decoder = new RadexOneDecoder();
        var extension = BuildReadDataExtension(ambient: 10, accumulated: 20, cpm: 30);
        extension[10] ^= 0xFF; // corrupt a reserved byte (not ambient/accumulated/cpm themselves)
        var report = RadexOneFramer.BuildReply(1, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.HasCount(1, lines);
        // Without checksum verification, the corrupted reserved byte doesn't change any decoded
        // field, so the bug shows this exact string as a valid reading despite the corruption.
        Assert.AreNotEqual("RADEX-ONE: CPM=30 Ambient=10 Accum=20", lines[0]);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Render_WithSettingsReply_AndCorruptedExtensionChecksum_IsNotShownAsAValidReading()
    {
        var decoder = new RadexOneDecoder();
        var extension = BuildReadSettingsExtension(alarmMode: 2, threshold: 300);
        extension[11] ^= 0xFF; // corrupt a reserved byte (not alarmMode/threshold themselves)
        var report = RadexOneFramer.BuildReply(1, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.HasCount(1, lines);
        // Without checksum verification, the corrupted reserved byte doesn't change any decoded
        // field, so the bug shows this exact string as valid settings despite the corruption.
        Assert.AreNotEqual("RADEX-ONE: alarm=Audio threshold=300", lines[0]);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Render_WithWriteSettingsAckReply_AndCorruptedExtensionChecksum_IsNotShownAsAcknowledged()
    {
        var decoder = new RadexOneDecoder();
        var extension = BuildWriteSettingsAckExtension();
        extension[2] ^= 0xFF; // corrupt the reserved word, leaving the outer header checksum intact
        var report = RadexOneFramer.BuildReply(1, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.HasCount(1, lines);
        Assert.AreNotEqual("RADEX-ONE: write settings acknowledged", lines[0]);
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
        WriteChecksum(extension, coveredLength: 20);
        return extension;
    }

    private static byte[] BuildReadSettingsExtension(byte alarmMode, ushort threshold)
    {
        var extension = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension, RadexOneCommand.ReadSettings);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(4), 0x0005);
        extension[8] = alarmMode;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(9), threshold);
        WriteChecksum(extension, coveredLength: 14);
        return extension;
    }

    private static byte[] BuildWriteSettingsAckExtension()
    {
        var extension = new byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(extension, RadexOneCommand.WriteSettings);
        WriteChecksum(extension, coveredLength: 4);
        return extension;
    }

    private static void WriteChecksum(byte[] extension, int coveredLength) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
            extension.AsSpan(coveredLength),
            RadexOneFramer.ComputeChecksum(extension.AsSpan(0, coveredLength)));
}
