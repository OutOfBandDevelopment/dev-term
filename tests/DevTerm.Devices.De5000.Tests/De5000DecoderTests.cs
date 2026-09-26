using System.Buffers;
using DevTerm.Test.Utilities;

namespace DevTerm.Devices.De5000.Tests;

/// <summary>
/// Feeds <see cref="De5000Decoder"/> synthetic 17-byte packets and asserts both the rendered text
/// and the structured values it publishes — no real device involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Ble)]
[TestCategory(TestCategories.DerEe_De5000)]
[TestClass]
public sealed class De5000DecoderTests
{
    // Same packet as De5000FramerTests' _validFrame: Ls=1.234mH, D=0.012, 1 kHz, no mode flags.
    private static readonly byte[] _validFrame =
    [
        0x00, 0x0D,
        0x00,
        0x40,
        0x00,
        0x01, 0x04, 0xD2, 0x33, 0x00,
        0x01, 0x00, 0x0C, 0x03, 0x00,
        0x0D, 0x0A,
    ];

    [TestMethod]
    public void Render_WithEmptySequence_ProducesNoLines()
    {
        var decoder = new De5000Decoder();

        var lines = decoder.Render(ReadOnlySequence<byte>.Empty);

        Assert.IsEmpty(lines);
    }

    [TestMethod]
    public void Render_OneWellFormedFrame_ProducesOneLine()
    {
        var decoder = new De5000Decoder();

        var lines = decoder.Render(new ReadOnlySequence<byte>(_validFrame));

        Assert.HasCount(1, lines);
        Assert.AreEqual("Ls=1.234mH D=0.012 @1 kHz", lines[0]);
    }

    [TestMethod]
    public void Render_FrameSplitAcrossTwoCalls_OnlyProducesALineOnceComplete()
    {
        var decoder = new De5000Decoder();

        var first = decoder.Render(new ReadOnlySequence<byte>(_validFrame.AsMemory(0, 10)));
        Assert.IsEmpty(first);

        var second = decoder.Render(new ReadOnlySequence<byte>(_validFrame.AsMemory(10)));
        Assert.HasCount(1, second);
        Assert.AreEqual("Ls=1.234mH D=0.012 @1 kHz", second[0]);
    }

    [TestMethod]
    public void Render_GarbageBytesBeforeAFrame_ResyncsAndStillDecodesIt()
    {
        var decoder = new De5000Decoder();
        var withGarbage = new byte[] { 0xAA, 0xBB, 0xCC }.Concat(_validFrame).ToArray();

        var lines = decoder.Render(new ReadOnlySequence<byte>(withGarbage));

        Assert.HasCount(1, lines);
        Assert.AreEqual("Ls=1.234mH D=0.012 @1 kHz", lines[0]);
    }

    [TestMethod]
    public void Render_TwoFramesInOneSequence_ProducesTwoLines()
    {
        var decoder = new De5000Decoder();
        var twoFrames = _validFrame.Concat(_validFrame).ToArray();

        var lines = decoder.Render(new ReadOnlySequence<byte>(twoFrames));

        Assert.HasCount(2, lines);
    }

    [TestMethod]
    public void Render_HoldFlagSet_IncludesHoldInTheFormattedLine()
    {
        var decoder = new De5000Decoder();
        var withHold = (byte[])_validFrame.Clone();
        withHold[2] = 0b0000_0001;

        var lines = decoder.Render(new ReadOnlySequence<byte>(withHold));

        Assert.AreEqual("Ls=1.234mH D=0.012 @1 kHz [HOLD]", lines[0]);
    }

    [TestMethod]
    public void Render_FirstFrame_PublishesEveryStructuredValue()
    {
        var decoder = new De5000Decoder();

        IReadOnlyDictionary<string, string>? published = null;
        decoder.ValuesChanged += (_, values) => published = values;
        decoder.Render(new ReadOnlySequence<byte>(_validFrame));

        Assert.IsNotNull(published);
        Assert.AreEqual("Ls=1.234mH", published!["primary"]);
        Assert.AreEqual("D=0.012", published["secondary"]);
        Assert.AreEqual("1 kHz", published["frequency"]);
        Assert.AreEqual("0", published["hold"]);
    }

    [TestMethod]
    public void Render_SameFrameTwice_OnlyPublishesOnFirstFrame()
    {
        var decoder = new De5000Decoder();
        var publishCount = 0;
        decoder.ValuesChanged += (_, _) => publishCount++;

        decoder.Render(new ReadOnlySequence<byte>(_validFrame));
        decoder.Render(new ReadOnlySequence<byte>(_validFrame));

        Assert.AreEqual(1, publishCount);
    }

    [TestMethod]
    public void Render_OnlyHoldFlagChanges_PublishesJustHold()
    {
        var decoder = new De5000Decoder();
        decoder.Render(new ReadOnlySequence<byte>(_validFrame));

        IReadOnlyDictionary<string, string>? published = null;
        decoder.ValuesChanged += (_, values) => published = values;
        var withHold = (byte[])_validFrame.Clone();
        withHold[2] = 0b0000_0001;
        decoder.Render(new ReadOnlySequence<byte>(withHold));

        Assert.IsNotNull(published);
        Assert.AreEqual("1", published!["hold"]);
        Assert.IsFalse(published.ContainsKey("primary"));
    }
}
