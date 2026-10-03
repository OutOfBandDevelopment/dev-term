using System.Buffers.Binary;
using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Tcp.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Tcp)]
[TestClass]
public sealed class LxiDiscoveryTests
{
    private static byte[] Reply(uint xid, uint port, uint acceptStat = 0, uint messageType = 1)
    {
        // xid, REPLY, MSG_ACCEPTED, verifier flavor, verifier length, accept_stat, port.
        var bytes = new byte[28];
        var words = new uint[] { xid, messageType, 0, 0, 0, acceptStat, port };
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), words[i]);
        }

        return bytes;
    }

    [TestMethod]
    public void BuildGetPortRequest_IsAPortmapperGetPortForTheVxi11CoreProgramOverTcp()
    {
        var request = LxiDiscovery.BuildGetPortRequest(0x1234);

        Assert.AreEqual(56, request.Length);
        Assert.AreEqual(0x1234u, BinaryPrimitives.ReadUInt32BigEndian(request));
        Assert.AreEqual(0u, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(4)));   // CALL
        Assert.AreEqual(2u, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(8)));   // RPC version 2
        Assert.AreEqual(100000u, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(12))); // portmapper
        Assert.AreEqual(3u, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(20)));  // GETPORT
        Assert.AreEqual(395183u, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(40))); // VXI-11 core
        Assert.AreEqual(6u, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(48)));  // TCP
    }

    [TestMethod]
    public void ParseGetPortReply_ReturnsThePort_FromTheRealDg1062zReply()
    {
        // Captured from the bench Rigol DG1062Z at 192.168.0.87.
        var reply = Convert.FromHexString("0000123400000001000000000000000000000000000000000000026a");

        Assert.AreEqual(618, LxiDiscovery.ParseGetPortReply(reply, 0x1234));
    }

    [TestMethod]
    public void ParseGetPortReply_IgnoresPortZero_ForeignXidsAndGarbage()
    {
        Assert.IsNull(LxiDiscovery.ParseGetPortReply(Reply(7, 0), 7));          // portmapper up, VXI-11 not registered (a second bench host did this)
        Assert.IsNull(LxiDiscovery.ParseGetPortReply(Reply(7, 618), 8));        // someone else's reply
        Assert.IsNull(LxiDiscovery.ParseGetPortReply(Reply(7, 618, acceptStat: 1), 7));
        Assert.IsNull(LxiDiscovery.ParseGetPortReply(Reply(7, 618, messageType: 0), 7));
        Assert.IsNull(LxiDiscovery.ParseGetPortReply([1, 2, 3], 7));
    }

    [TestMethod]
    public void Display_ShowsTheIdentityAndRawPort_OrSaysThereIsNoRawPort()
    {
        Assert.AreEqual("Rigol,DG1062Z  (192.168.0.87:5555)", new LxiDevice("192.168.0.87", 618, 5555, "Rigol,DG1062Z").Display);
        StringAssert.Contains(new LxiDevice("10.0.0.2", 111, 0, string.Empty).Display, "VXI-11 only");
    }
}
