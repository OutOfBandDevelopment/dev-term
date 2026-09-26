using System.Buffers;
using DevTerm.Test.Utilities;

namespace DevTerm.Devices.RadexOne.Tests;

/// <summary>
/// Feeds <see cref="RadexOneDecoder"/> HID-wrapped sample replies (built via
/// <see cref="RadexOneFramer.BuildReply"/> + <see cref="RadexOneHidFraming.WrapRequest"/>, reused here
/// purely as a byte-layout helper since the report shape is symmetric for this test's purposes) and
/// asserts the rendered text — no real device involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Hid)]
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
        byte[] extension = [0x0A, 0x00, 0x14, 0x00, 0x1E, 0x00]; // Ambient=10, Accumulated=20, Cpm=30
        var report = WrapReply(RadexOneCommand.ReadData, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.HasCount(1, lines);
        Assert.AreEqual("RADEX-ONE: CPM=30 Ambient=10 Accum=20", lines[0]);
    }

    [TestMethod]
    public void Render_WithSerialVersionReply_RendersAsciiText()
    {
        var decoder = new RadexOneDecoder();
        var report = WrapReply(RadexOneCommand.ReadSerialVersion, "RD1706123"u8.ToArray());

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.AreEqual("RADEX-ONE: RD1706123", lines[0]);
    }

    [TestMethod]
    public void Render_WithSettingsReply_FormatsAlarmModeAndThreshold()
    {
        var decoder = new RadexOneDecoder();
        byte[] extension = [0x02, 0x2C, 0x01]; // Audio, threshold=0x012C=300
        var report = WrapReply(RadexOneCommand.ReadSettings, extension);

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.AreEqual("RADEX-ONE: alarm=Audio threshold=300", lines[0]);
    }

    [TestMethod]
    public void Render_WithBadChecksum_ProducesADiagnosticLineInsteadOfThrowing()
    {
        var decoder = new RadexOneDecoder();
        var report = WrapReply(RadexOneCommand.ReadData, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        report[11] ^= 0xFF;

        var lines = decoder.Render(new ReadOnlySequence<byte>(report));

        Assert.HasCount(1, lines);
        StringAssert.Contains(lines[0], "unrecognized reply");
    }

    private static byte[] WrapReply(ushort type, byte[] extension) =>
        RadexOneHidFraming.WrapRequest(RadexOneFramer.BuildReply(type, 1, extension));
}
