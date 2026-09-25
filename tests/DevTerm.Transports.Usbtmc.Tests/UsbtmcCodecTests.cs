using System.Buffers.Binary;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Usbtmc;

namespace DevTerm.Transports.Usbtmc.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class UsbtmcCodecTests
{
    private static byte[] BuildHeader(byte msgId, byte bTag, byte bTagInverse, int transferSize, bool eom)
    {
        var header = new byte[UsbtmcCodec.HeaderSize];
        header[0] = msgId;
        header[1] = bTag;
        header[2] = bTagInverse;
        header[3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), (uint)transferSize);
        header[8] = eom ? (byte)0x01 : (byte)0x00;
        header[9] = 0;
        header[10] = 0;
        header[11] = 0;
        return header;
    }

    [TestMethod]
    public void DecodeHeader_ValidMatchingHeader_Accepts()
    {
        var header = BuildHeader(UsbtmcCodec.DevDepMsgIn, bTag: 5, bTagInverse: unchecked((byte)~5), transferSize: 42, eom: true);

        var decoded = UsbtmcCodec.DecodeHeader(header, expectedTag: 5);

        Assert.AreEqual(UsbtmcCodec.DevDepMsgIn, decoded.MsgId);
        Assert.AreEqual((byte)5, decoded.BTag);
        Assert.AreEqual(42, decoded.TransferSize);
        Assert.IsTrue(decoded.Eom);
    }

    [TestMethod]
    public void DecodeHeader_UnexpectedTag_Throws()
    {
        var header = BuildHeader(UsbtmcCodec.DevDepMsgIn, bTag: 5, bTagInverse: unchecked((byte)~5), transferSize: 42, eom: true);

        Assert.ThrowsExactly<InvalidOperationException>(() => UsbtmcCodec.DecodeHeader(header, expectedTag: 6));
    }

    [TestMethod]
    public void DecodeHeader_BTagInverseMismatch_Throws()
    {
        // transfer[2] should be ~transfer[1]; corrupt it so the consistency check fails.
        var header = BuildHeader(UsbtmcCodec.DevDepMsgIn, bTag: 5, bTagInverse: 0x00, transferSize: 42, eom: true);

        Assert.ThrowsExactly<InvalidOperationException>(() => UsbtmcCodec.DecodeHeader(header, expectedTag: 5));
    }

    [TestMethod]
    public void DecodeHeader_WrongMsgId_Throws()
    {
        var header = BuildHeader(msgId: UsbtmcCodec.DevDepMsgOut, bTag: 5, bTagInverse: unchecked((byte)~5), transferSize: 42, eom: true);

        Assert.ThrowsExactly<InvalidOperationException>(() => UsbtmcCodec.DecodeHeader(header, expectedTag: 5));
    }

    [TestMethod]
    public void DecodeHeader_ShorterThanHeaderSize_Throws()
    {
        var tooShort = new byte[UsbtmcCodec.HeaderSize - 1];

        Assert.ThrowsExactly<InvalidOperationException>(() => UsbtmcCodec.DecodeHeader(tooShort, expectedTag: 1));
    }
}
