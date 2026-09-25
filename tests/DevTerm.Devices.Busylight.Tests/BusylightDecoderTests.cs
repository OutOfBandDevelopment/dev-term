using System.Buffers;
using System.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Devices.Busylight.Tests;

/// <summary>
/// Feeds <see cref="BusylightDecoder"/> a sample poll reply and asserts the rendered text — no real
/// device involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class BusylightDecoderTests
{
    [TestMethod]
    public void Render_WithEmptySequence_ProducesNoLines()
    {
        var decoder = new BusylightDecoder();

        var lines = decoder.Render(ReadOnlySequence<byte>.Empty);

        Assert.IsEmpty(lines);
    }

    [TestMethod]
    public void Render_WithAsciiIdentificationReply_ProducesOnePrefixedLine()
    {
        var decoder = new BusylightDecoder();
        var bytes = Encoding.ASCII.GetBytes("0001PLENOM0000010000000");

        var lines = decoder.Render(new ReadOnlySequence<byte>(bytes));

        Assert.HasCount(1, lines);
        Assert.AreEqual("BUSYLIGHT: 0001PLENOM0000010000000", lines[0]);
    }

    [TestMethod]
    public void Render_WithNonPrintableBytes_ReplacesThemWithADot()
    {
        var decoder = new BusylightDecoder();
        byte[] bytes = [(byte)'O', (byte)'K', 0x00, 0x01, 0x7F];

        var lines = decoder.Render(new ReadOnlySequence<byte>(bytes));

        Assert.AreEqual("BUSYLIGHT: OK...", lines[0]);
    }
}
