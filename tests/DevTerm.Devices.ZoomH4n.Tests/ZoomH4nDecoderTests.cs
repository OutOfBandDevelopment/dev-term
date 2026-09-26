using System.Buffers;
using DevTerm.Test.Utilities;

namespace DevTerm.Devices.ZoomH4n.Tests;

/// <summary>
/// Feeds <see cref="ZoomH4nDecoder"/> synthetic status bytes and asserts both the rendered text and
/// the structured values it publishes — no real device involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Serial)]
[TestCategory(TestCategories.Zoom_H4n)]
[TestClass]
public sealed class ZoomH4nDecoderTests
{
    [TestMethod]
    public void Render_WithEmptySequence_ProducesNoLines()
    {
        var decoder = new ZoomH4nDecoder();

        var lines = decoder.Render(ReadOnlySequence<byte>.Empty);

        Assert.IsEmpty(lines);
    }

    [TestMethod]
    public void Render_WithNoBitsSet_ReportsNone()
    {
        var decoder = new ZoomH4nDecoder();

        var lines = decoder.Render(new ReadOnlySequence<byte>([0x00]));

        Assert.HasCount(1, lines);
        Assert.AreEqual("Status: (none)", lines[0]);
    }

    [TestMethod]
    public void Render_WithRecordAndPeakBits_ReportsBoth()
    {
        var decoder = new ZoomH4nDecoder();

        var lines = decoder.Render(new ReadOnlySequence<byte>([0x03]));

        Assert.AreEqual("Status: Record | Peak", lines[0]);
    }

    [TestMethod]
    public void Render_WithAllDocumentedBits_ReportsAllFive()
    {
        var decoder = new ZoomH4nDecoder();

        var lines = decoder.Render(new ReadOnlySequence<byte>([0x73]));

        Assert.AreEqual("Status: Record | Peak | Mic | Led1 | Led2", lines[0]);
    }

    [TestMethod]
    public void Render_TwoBytesInOneSequence_ProducesTwoIndependentLines()
    {
        var decoder = new ZoomH4nDecoder();

        var lines = decoder.Render(new ReadOnlySequence<byte>([0x01, 0x02]));

        Assert.HasCount(2, lines);
        Assert.AreEqual("Status: Record", lines[0]);
        Assert.AreEqual("Status: Peak", lines[1]);
    }

    [TestMethod]
    public void Render_RecordBitSet_PublishesChangedStructuredValues()
    {
        var decoder = new ZoomH4nDecoder();
        // Establish a baseline first: on the very first Render call every key is new to
        // _lastValues, so all 5 would publish regardless of value - only a second, differing
        // frame isolates just the bit that actually changed.
        decoder.Render(new ReadOnlySequence<byte>([0x00]));

        IReadOnlyDictionary<string, string>? published = null;
        decoder.ValuesChanged += (_, values) => published = values;
        decoder.Render(new ReadOnlySequence<byte>([0x01]));

        Assert.IsNotNull(published);
        Assert.AreEqual("1", published!["statusRecord"]);
        Assert.IsFalse(published.ContainsKey("statusPeak"));
    }

    [TestMethod]
    public void Render_SameByteTwice_OnlyPublishesOnFirstChange()
    {
        var decoder = new ZoomH4nDecoder();
        var publishCount = 0;
        decoder.ValuesChanged += (_, _) => publishCount++;

        decoder.Render(new ReadOnlySequence<byte>([0x01]));
        decoder.Render(new ReadOnlySequence<byte>([0x01]));

        Assert.AreEqual(1, publishCount);
    }

    [TestMethod]
    public void Render_UndefinedHighBit_IsIgnoredByStatusDecoding()
    {
        var decoder = new ZoomH4nDecoder();

        var lines = decoder.Render(new ReadOnlySequence<byte>([0x80]));

        Assert.AreEqual("Status: (none)", lines[0]);
    }
}
