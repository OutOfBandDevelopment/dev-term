using System.Buffers;

namespace DevTerm.Devices.K8055.Tests;

/// <summary>
/// Feeds <see cref="K8055Decoder"/> the confirmed real 9-byte input shape (see
/// docs/design/proposals/velleman-k8055-protocol.md's real-hardware finding) and asserts both its
/// human-readable <c>Render</c> text and its structured <see cref="K8055Decoder.ValuesChanged"/>
/// output. No real device involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class K8055DecoderTests
{
    // [00, 00, 03, AnalogIn1=0x4C(76), AnalogIn2=0x4C(76), CounterLo1=0x2A, CounterHi1=0x01, CounterLo2=0x00, CounterHi2=0x00]
    // -> counter1 = 0x012A = 298, counter2 = 0.
    private static readonly byte[] SampleFrame = [0x00, 0x00, 0x03, 0x4C, 0x4C, 0x2A, 0x01, 0x00, 0x00];

    [TestMethod]
    public void Render_WithConfirmedFrameShape_ProducesOneReadableLine()
    {
        var decoder = new K8055Decoder();

        var lines = decoder.Render(new ReadOnlySequence<byte>(SampleFrame));

        Assert.AreEqual(1, lines.Count);
        StringAssert.Contains(lines[0], "A1=76");
        StringAssert.Contains(lines[0], "A2=76");
        StringAssert.Contains(lines[0], "C1=298");
        StringAssert.Contains(lines[0], "C2=0");
        StringAssert.Contains(lines[0], "D=0x00");
    }

    [TestMethod]
    public void Render_RaisesValuesChangedWithExpectedDictionary()
    {
        var decoder = new K8055Decoder();
        IReadOnlyDictionary<string, string>? received = null;
        decoder.ValuesChanged += (_, values) => received = values;

        decoder.Render(new ReadOnlySequence<byte>(SampleFrame));

        Assert.IsNotNull(received);
        Assert.AreEqual("0x00", received["digitalInRaw"]);
        Assert.AreEqual("76", received["analogIn1"]);
        Assert.AreEqual("76", received["analogIn2"]);
        Assert.AreEqual("298", received["counter1"]);
        Assert.AreEqual("0", received["counter2"]);
    }

    [TestMethod]
    public void Render_WithNonZeroDigitalInByte_ReportsItRawInHex()
    {
        var decoder = new K8055Decoder();
        var frame = (byte[])SampleFrame.Clone();
        frame[0] = 0x05;

        var lines = decoder.Render(new ReadOnlySequence<byte>(frame));

        StringAssert.Contains(lines[0], "D=0x05");
    }

    [TestMethod]
    public void Render_WithAPartialFrameAcrossTwoCalls_EmitsNothingUntilComplete()
    {
        var decoder = new K8055Decoder();

        var firstCallLines = decoder.Render(new ReadOnlySequence<byte>(SampleFrame.AsMemory(0, 5)));
        Assert.AreEqual(0, firstCallLines.Count);

        var secondCallLines = decoder.Render(new ReadOnlySequence<byte>(SampleFrame.AsMemory(5)));
        Assert.AreEqual(1, secondCallLines.Count);
    }

    [TestMethod]
    public void Render_WithTwoFramesInOneCall_EmitsTwoLines()
    {
        var decoder = new K8055Decoder();
        var twoFrames = new byte[SampleFrame.Length * 2];
        SampleFrame.CopyTo(twoFrames, 0);
        SampleFrame.CopyTo(twoFrames, SampleFrame.Length);

        var lines = decoder.Render(new ReadOnlySequence<byte>(twoFrames));

        Assert.AreEqual(2, lines.Count);
    }
}
