using System.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Brokers.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Brokers)]
[TestClass]
public sealed class StompFrameTests
{
    [TestMethod]
    public void Encode_WritesCommandHeadersBlankLineBodyAndNul()
    {
        var frame = new StompFrame("SEND", [new("destination", "/topic/a"), new("content-length", "2")], "hi"u8.ToArray());

        Assert.AreEqual("SEND\ndestination:/topic/a\ncontent-length:2\n\nhi\0", Encoding.UTF8.GetString(frame.Encode()));
    }

    [TestMethod]
    public void Encode_ConnectIsNotEscaped_ButOtherFramesAre()
    {
        Assert.AreEqual("CONNECT\npasscode:a:b\n\n\0", Encoding.UTF8.GetString(new StompFrame("CONNECT", [new("passcode", "a:b")], []).Encode()));
        Assert.AreEqual("SEND\nx:a\\cb\\n\n\n\0", Encoding.UTF8.GetString(new StompFrame("SEND", [new("x", "a:b\n")], []).Encode()));
    }

    [TestMethod]
    public void TryParse_ReadsAMessageAndReportsHowManyBytesItUsed()
    {
        var bytes = Encoding.UTF8.GetBytes("MESSAGE\ndestination:/topic/t\nmessage-id:1\n\n21.5\0NEXT");

        var frame = StompFrame.TryParse(bytes, out var consumed);

        Assert.IsNotNull(frame);
        Assert.AreEqual("MESSAGE", frame.Command);
        Assert.AreEqual("/topic/t", frame.Header("destination"));
        Assert.AreEqual("21.5", Encoding.UTF8.GetString(frame.Body));
        Assert.AreEqual(bytes.Length - "NEXT".Length, consumed);
    }

    [TestMethod]
    public void TryParse_AnIncompleteFrame_ReturnsNull()
    {
        Assert.IsNull(StompFrame.TryParse("MESSAGE\ndestination:/a\n\nhal"u8, out _));
        Assert.IsNull(StompFrame.TryParse("MESSAGE\ndestination"u8, out _));
    }

    [TestMethod]
    public void TryParse_ContentLength_LetsABodyContainNul()
    {
        var bytes = new List<byte>(Encoding.UTF8.GetBytes("MESSAGE\ncontent-length:3\n\n"));
        bytes.AddRange(new byte[] { 1, 0, 2, 0 });

        var frame = StompFrame.TryParse(bytes.ToArray(), out var consumed);

        CollectionAssert.AreEqual(new byte[] { 1, 0, 2 }, frame!.Body);
        Assert.AreEqual(bytes.Count, consumed);
    }

    [TestMethod]
    public void TryParse_SkipsHeartBeatNewlines_UnescapesHeaders_AndFirstRepeatedHeaderWins()
    {
        var frame = StompFrame.TryParse(Encoding.UTF8.GetBytes("\n\nERROR\nmessage:a\\cb\nmessage:later\n\n\0"), out _);

        Assert.AreEqual("ERROR", frame!.Command);
        Assert.AreEqual("a:b", frame.Header("message"));
    }
}
