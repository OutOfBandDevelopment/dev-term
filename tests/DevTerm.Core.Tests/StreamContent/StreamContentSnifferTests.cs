using System.Text;
using DevTerm.Core.StreamContent;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.StreamContent;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class StreamContentSnifferTests
{
    public static IEnumerable<object[]> RecognizedSamples =>
    [
        [StreamContentSamples.Png(), StreamContentKind.Png],
        [StreamContentSamples.Jpeg(), StreamContentKind.Jpeg],
        [StreamContentSamples.Gif(), StreamContentKind.Gif],
        [Encoding.ASCII.GetBytes("GIF87a\u0001\u0000"), StreamContentKind.Gif],
        [StreamContentSamples.Bmp(), StreamContentKind.Bmp],
        [StreamContentSamples.Tiff(), StreamContentKind.Tiff],
        [new byte[] { (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8 }, StreamContentKind.Tiff],
        [StreamContentSamples.PostScript(), StreamContentKind.PostScript],
        [new byte[] { 0xC5, 0xD0, 0xD3, 0xC6, 30, 0, 0, 0 }, StreamContentKind.PostScript],
        [StreamContentSamples.PjlPcl(), StreamContentKind.Pcl],
        [Encoding.ASCII.GetBytes("\u001bE\u001b&l0O"), StreamContentKind.Pcl],
        [Encoding.ASCII.GetBytes("\u001b%1BIN;PD;"), StreamContentKind.Pcl],
        [StreamContentSamples.Hpgl(), StreamContentKind.Hpgl],
        [Encoding.ASCII.GetBytes("SP1;PU100,100;PD200,200;"), StreamContentKind.Hpgl],
        [Encoding.ASCII.GetBytes("DF;"), StreamContentKind.Hpgl],
    ];

    [TestMethod]
    [DynamicData(nameof(RecognizedSamples))]
    public void Identify_RecognizesEachSignature(byte[] sample, StreamContentKind expected) =>
        Assert.AreEqual(expected, StreamContentSniffer.Identify(sample));

    [TestMethod]
    [DataRow("Hello, world\r\n")]
    [DataRow("BMW 320i")]
    [DataRow("OK;ERR;DONE;")]
    [DataRow("SP1 PU")]
    [DataRow("IN")]
    [DataRow("+1.23456E+00\n")]
    [DataRow("\u001bEplain VT100 next-line")]
    [DataRow("\u001b[2J\u001b[H")]
    [DataRow("%PDF-1.4")]
    [DataRow("#1")]
    public void Identify_OrdinaryReplies_AreNotContent(string reply) =>
        Assert.IsNull(StreamContentSniffer.Identify(Encoding.ASCII.GetBytes(reply)));

    [TestMethod]
    public void Identify_BmpLettersWithoutAPlausibleHeader_AreNotContent()
    {
        var bmp = StreamContentSamples.Bmp();
        bmp[6] = 1; // non-zero reserved field

        Assert.IsNull(StreamContentSniffer.Identify(bmp));
    }

    [TestMethod]
    public void Identify_TooShortToTell_IsNull() =>
        Assert.IsNull(StreamContentSniffer.Identify(StreamContentSamples.Png().AsSpan(0, 5)));

    [TestMethod]
    public void Find_BinarySignatureAfterText_ReportsItsOffset()
    {
        byte[] data = [.. "garbage before "u8, .. StreamContentSamples.Png()];

        var match = StreamContentSniffer.Find(data, startIsBoundary: false);

        Assert.IsNotNull(match);
        Assert.AreEqual(StreamContentKind.Png, match.Value.Kind);
        Assert.AreEqual(15, match.Value.Offset);
        Assert.AreEqual(0, match.Value.HeaderLength);
        Assert.IsNull(match.Value.PayloadLength);
    }

    [TestMethod]
    public void Find_Hpgl_OnlyMatchesAtAReplyBoundary()
    {
        Assert.IsNull(StreamContentSniffer.Find("reply IN;SP1;"u8, startIsBoundary: true));
        Assert.IsNull(StreamContentSniffer.Find("IN;SP1;"u8, startIsBoundary: false));

        Assert.AreEqual(StreamContentKind.Hpgl, StreamContentSniffer.Find("IN;SP1;"u8, startIsBoundary: true)?.Kind);

        var afterNewline = StreamContentSniffer.Find("ready\r\nIN;SP1;"u8, startIsBoundary: false);
        Assert.AreEqual(StreamContentKind.Hpgl, afterNewline?.Kind);
        Assert.AreEqual(7, afterNewline?.Offset);
    }

    [TestMethod]
    public void Find_BlockWrappedImage_ReportsTheHeaderAndPayloadLength()
    {
        var bmp = StreamContentSamples.Bmp();
        byte[] data = [.. "x"u8, .. StreamContentSamples.ScpiBlock(bmp), (byte)'\n'];

        var match = StreamContentSniffer.Find(data, startIsBoundary: true);

        Assert.IsNotNull(match);
        Assert.AreEqual(StreamContentKind.Bmp, match.Value.Kind);
        Assert.AreEqual(1, match.Value.Offset);
        Assert.AreEqual(4, match.Value.HeaderLength); // "#270"
        Assert.AreEqual(70L, match.Value.PayloadLength);
    }

    [TestMethod]
    public void Find_BlockWrappingUnrecognizedData_IsNotAMatchWithoutAHint() =>
        Assert.IsNull(StreamContentSniffer.Find(StreamContentSamples.ScpiBlock([1, 2, 3, 4, 5]), startIsBoundary: true));

    [TestMethod]
    public void Find_PlainText_IsNull() =>
        Assert.IsNull(StreamContentSniffer.Find("MEAS:VOLT:DC? +1.2345E+00\r\n#Channel 1 OK\n"u8, startIsBoundary: true));

    [TestMethod]
    [DataRow("#15abcde", BlockHeaderStatus.Complete, 3, 5L)]
    [DataRow("#9000000012", BlockHeaderStatus.Complete, 11, 12L)]
    [DataRow("#10", BlockHeaderStatus.Complete, 3, 0L)]
    [DataRow("#", BlockHeaderStatus.Incomplete, 0, 0L)]
    [DataRow("#4001", BlockHeaderStatus.Incomplete, 0, 0L)]
    [DataRow("#0", BlockHeaderStatus.NotABlock, 0, 0L)]
    [DataRow("#A12", BlockHeaderStatus.NotABlock, 0, 0L)]
    [DataRow("#3x12", BlockHeaderStatus.NotABlock, 0, 0L)]
    [DataRow("12345", BlockHeaderStatus.NotABlock, 0, 0L)]
    public void ParseBlockHeader_ReportsEachShape(string text, BlockHeaderStatus expected, int expectedHeaderLength, long expectedPayloadLength)
    {
        var status = StreamContentSniffer.ParseBlockHeader(Encoding.ASCII.GetBytes(text), out var headerLength, out var payloadLength);

        Assert.AreEqual(expected, status);
        Assert.AreEqual(expectedHeaderLength, headerLength);
        Assert.AreEqual(expectedPayloadLength, payloadLength);
    }
}
