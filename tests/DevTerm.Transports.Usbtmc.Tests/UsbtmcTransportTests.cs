using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using DevTerm.Transports.Usbtmc;
using Microsoft.Extensions.Options;
using Moq;

namespace DevTerm.Transports.Usbtmc.Tests;

/// <summary>
/// Covers <see cref="UsbtmcTransport.WriteAsync"/>'s reply-reassembly path via <c>ReadReply</c>
/// (private, exercised indirectly through the public API) - see
/// docs/design/proposals/usbtmc-lockup-fix-prompt.md's "Testing requirement" section, which calls
/// out that a single-physical-transfer test alone would not catch the bulk-IN reassembly bug this
/// class guards against.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class UsbtmcTransportTests
{
    private static byte[] BuildReplyHeader(byte bTag, int transferSize, bool eom)
    {
        var header = new byte[UsbtmcCodec.HeaderSize];
        header[0] = UsbtmcCodec.DevDepMsgIn;
        header[1] = bTag;
        header[2] = unchecked((byte)~bTag);
        header[3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), (uint)transferSize);
        header[8] = eom ? (byte)0x01 : (byte)0x00;
        header[9] = 0;
        header[10] = 0;
        header[11] = 0;
        return header;
    }

    private static (UsbtmcTransport Transport, FakeUsbtmcDevice Device) CreateTransport(int maxTransferSize = 1024, int maxResponseSize = 16 * 1024 * 1024)
    {
        var device = new FakeUsbtmcDevice { MaxTransferSize = maxTransferSize };
        var factory = new Mock<IUsbtmcDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<UsbtmcTransportOptions>())).Returns(device);

        var options = Microsoft.Extensions.Options.Options.Create(new UsbtmcTransportOptions
        {
            VendorId = 1,
            ProductId = 1,
            MaxTransferSize = maxTransferSize,
            MaxResponseSize = maxResponseSize,
        });

        return (new UsbtmcTransport(factory.Object, options), device);
    }

    private static async Task<byte[]> ReadAvailableAsync(PipeReader reader)
    {
        var result = await reader.ReadAsync();
        var data = result.Buffer.ToArray();
        reader.AdvanceTo(result.Buffer.End);
        return data;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    // The request frame this transport sends is always the SECOND bulk-OUT write for a fresh
    // transport's first query (first is the DEV_DEP_MSG_OUT command frame, tag 1; second is the
    // REQUEST_DEV_DEP_MSG_IN, tag 2) - so a reply header must claim bTag 2 to pass the new
    // expected-tag validation.
    private const byte FirstQueryRequestTag = 2;

    [TestMethod]
    public async Task WriteAsync_ReplySpanningTwoPhysicalTransfers_ReassemblesCorrectly()
    {
        var (transport, device) = CreateTransport(maxTransferSize: 4);
        await transport.OpenAsync();

        // Logical reply is 8 bytes ("ABCDEFGH"); the first physical transfer only has room for 4
        // (the read buffer is HeaderSize + MaxTransferSize), so EOM is not set there. The second
        // transfer is pure continuation payload with NO header at all.
        device.EnqueueRead(Concat(BuildReplyHeader(FirstQueryRequestTag, transferSize: 8, eom: false), Encoding.ASCII.GetBytes("ABCD")));
        device.EnqueueRead(Encoding.ASCII.GetBytes("EFGH"));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"));

        var reply = await ReadAvailableAsync(transport.Input);
        Assert.AreEqual("ABCDEFGH", Encoding.ASCII.GetString(reply));
    }

    [TestMethod]
    public async Task WriteAsync_ContinuationBytesThatWouldDecodeAsABogusHeader_AreNeverReparsed()
    {
        var (transport, device) = CreateTransport(maxTransferSize: 16);
        await transport.OpenAsync();

        // The continuation transfer below is deliberately built to look like a *valid* header if
        // (incorrectly) re-decoded: matching MsgID/bTag/~bTag, but with an absurd TransferSize and
        // EOM set. If ReadReply ever re-parses a continuation transfer as a header again, this
        // would be misread as a short, final, empty-payload reply instead of 12 bytes of real data.
        var bogusLookingContinuation = BuildReplyHeader(FirstQueryRequestTag, transferSize: int.MaxValue, eom: true);

        device.EnqueueRead(Concat(BuildReplyHeader(FirstQueryRequestTag, transferSize: 16, eom: false), Encoding.ASCII.GetBytes("AAAA")));
        device.EnqueueRead(bogusLookingContinuation);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"));

        var reply = await ReadAvailableAsync(transport.Input);
        var expected = Concat(Encoding.ASCII.GetBytes("AAAA"), bogusLookingContinuation);
        CollectionAssert.AreEqual(expected, reply);
    }

    [TestMethod]
    public async Task WriteAsync_SingleTransferReply_StillWorks()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync();

        device.EnqueueRead(Concat(BuildReplyHeader(FirstQueryRequestTag, transferSize: 5, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"));

        var reply = await ReadAvailableAsync(transport.Input);
        Assert.AreEqual("HELLO", Encoding.ASCII.GetString(reply));
    }

    [TestMethod]
    public async Task WriteAsync_DeclaredTransferSizeExceedsMaxResponseSize_Throws()
    {
        var (transport, device) = CreateTransport(maxResponseSize: 8);
        await transport.OpenAsync();

        device.EnqueueRead(Concat(BuildReplyHeader(FirstQueryRequestTag, transferSize: 1000, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await Assert.ThrowsExactlyAsync<IOException>(() => transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?")));
    }

    [TestMethod]
    public async Task WriteAsync_Query_RequestsAnEffectivelyUnlimitedTransferSize()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync();

        device.EnqueueRead(Concat(BuildReplyHeader(FirstQueryRequestTag, transferSize: 5, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"));

        // WrittenFrames[0] is the DEV_DEP_MSG_OUT command; [1] is the REQUEST_DEV_DEP_MSG_IN whose
        // TransferSize field (bytes 4..8) is the requested max reply size.
        var requestFrame = device.WrittenFrames[1];
        var requestedSize = BinaryPrimitives.ReadUInt32LittleEndian(requestFrame.AsSpan(4, 4));
        Assert.AreEqual((uint)int.MaxValue, requestedSize);
    }

    [TestMethod]
    public async Task WriteAsync_FirstReplyIsAValidZeroLengthMessage_RetriesAndReturnsTheRealReply()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync();

        // Confirmed against a real Rigol DS1102E: a query sent right after OpenAsync sometimes
        // gets back a fully well-formed, EOM-terminated, zero-byte logical message before the
        // real reply - not a physical zero-byte transfer (that's the separate stalled/count<=0
        // path), but a valid header declaring TransferSize=0. A second REQUEST_DEV_DEP_MSG_IN
        // should recover the real data.
        device.EnqueueRead(BuildReplyHeader(FirstQueryRequestTag, transferSize: 0, eom: true));
        device.EnqueueRead(Concat(BuildReplyHeader((byte)(FirstQueryRequestTag + 1), transferSize: 5, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"));

        var reply = await ReadAvailableAsync(transport.Input);
        Assert.AreEqual("HELLO", Encoding.ASCII.GetString(reply));
    }

    [TestMethod]
    public async Task WriteAsync_TwoConsecutiveValidZeroLengthMessages_ReturnsAnEmptyReplyWithoutThrowing()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync();

        device.EnqueueRead(BuildReplyHeader(FirstQueryRequestTag, transferSize: 0, eom: true));
        device.EnqueueRead(BuildReplyHeader((byte)(FirstQueryRequestTag + 1), transferSize: 0, eom: true));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"));

        var reply = await ReadAvailableAsync(transport.Input);
        Assert.HasCount(0, reply);
    }

    [TestMethod]
    public async Task WriteAsync_NonQuery_DoesNotReadAReply()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync();

        await transport.WriteAsync(Encoding.ASCII.GetBytes("OUTP ON"));

        // A non-query never issues REQUEST_DEV_DEP_MSG_IN - only the DEV_DEP_MSG_OUT command frame.
        Assert.HasCount(1, device.WrittenFrames);
    }
}
