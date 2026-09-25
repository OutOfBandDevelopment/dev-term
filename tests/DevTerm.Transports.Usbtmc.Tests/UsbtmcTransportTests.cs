using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using DevTerm.Test.Utilities;
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
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Usbtmc)]
[TestClass]
public sealed class UsbtmcTransportTests
{
    public required TestContext TestContext { get; set; }

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

    private static (UsbtmcTransport Transport, FakeUsbtmcDevice Device) CreateTransport(
        int maxTransferSize = 1024,
        int maxResponseSize = 16 * 1024 * 1024,
        int vendorId = 1,
        int productId = 1,
        bool clearOnOpen = false)
    {
        var device = new FakeUsbtmcDevice { MaxTransferSize = maxTransferSize };
        var factory = new Mock<IUsbtmcDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<UsbtmcTransportOptions>())).Returns(device);

        var options = Microsoft.Extensions.Options.Options.Create(new UsbtmcTransportOptions
        {
            VendorId = vendorId,
            ProductId = productId,
            MaxTransferSize = maxTransferSize,
            MaxResponseSize = maxResponseSize,
            ClearOnOpen = clearOnOpen,
        });

        return (new UsbtmcTransport(factory.Object, options), device);
    }

    private static async Task<byte[]> ReadAvailableAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        var result = await reader.ReadAsync(cancellationToken);
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
    private const byte _firstQueryRequestTag = 2;

    [TestMethod]
    public async Task WriteAsync_ReplySpanningTwoPhysicalTransfers_ReassemblesCorrectly()
    {
        var cancellationToken = TestContext.CancellationToken;

        var (transport, device) = CreateTransport(maxTransferSize: 4);
        await transport.OpenAsync(cancellationToken);

        // One 8-byte transfer ("ABCDEFGH", EOM set - EOM belongs to the USBTMC transfer, not to a
        // physical read) that doesn't fit one 16-byte read buffer (HeaderSize + MaxTransferSize):
        // the first read fills the buffer exactly, so the transfer isn't over yet, and the second
        // read is pure continuation payload with NO header at all, ended by a short packet.
        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 8, eom: true), Encoding.ASCII.GetBytes("ABCD")));
        device.EnqueueRead(Encoding.ASCII.GetBytes("EFGH"));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"), cancellationToken);

        var reply = await ReadAvailableAsync(transport.Input, cancellationToken);
        Assert.AreEqual("ABCDEFGH", Encoding.ASCII.GetString(reply));
    }

    [TestMethod]
    public async Task WriteAsync_ContinuationBytesThatWouldDecodeAsABogusHeader_AreNeverReparsed()
    {
        var cancellationToken = TestContext.CancellationToken;
        var (transport, device) = CreateTransport(maxTransferSize: 4);
        await transport.OpenAsync(cancellationToken);

        // The continuation read below is deliberately built to look like a *valid* header if
        // (incorrectly) re-decoded: matching MsgID/bTag/~bTag, but with an absurd TransferSize and
        // EOM set. If ReadReply ever re-parses a continuation transfer as a header again, this
        // would be misread as a short, final, empty-payload reply instead of 12 bytes of real data.
        var bogusLookingContinuation = BuildReplyHeader(_firstQueryRequestTag, transferSize: int.MaxValue, eom: true);

        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 16, eom: true), Encoding.ASCII.GetBytes("AAAA")));
        device.EnqueueRead(bogusLookingContinuation);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"), cancellationToken);

        var reply = await ReadAvailableAsync(transport.Input, cancellationToken);
        var expected = Concat(Encoding.ASCII.GetBytes("AAAA"), bogusLookingContinuation);
        Assert.AreSequenceEqual(expected, reply);
    }

    [TestMethod]
    public async Task WriteAsync_SingleTransferReply_StillWorks()
    {
        var cancellationToken = TestContext.CancellationToken;
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(cancellationToken);

        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 5, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"), cancellationToken);

        var reply = await ReadAvailableAsync(transport.Input, cancellationToken);
        Assert.AreEqual("HELLO", Encoding.ASCII.GetString(reply));
    }

    [TestMethod]
    public async Task WriteAsync_DeclaredTransferSizeExceedsMaxResponseSize_Throws()
    {
        var (transport, device) = CreateTransport(maxResponseSize: 8);
        await transport.OpenAsync(TestContext.CancellationToken);

        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 1000, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await Assert.ThrowsExactlyAsync<IOException>(() => transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task WriteAsync_Query_RequestsAnEffectivelyUnlimitedTransferSize()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 5, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"), TestContext.CancellationToken);

        // WrittenFrames[0] is the DEV_DEP_MSG_OUT command; [1] is the REQUEST_DEV_DEP_MSG_IN whose
        // TransferSize field (bytes 4..8) is the requested max reply size.
        var requestFrame = device.WrittenFrames[1];
        var requestedSize = BinaryPrimitives.ReadUInt32LittleEndian(requestFrame.AsSpan(4, 4));
        Assert.AreEqual((uint)int.MaxValue, requestedSize);
    }

    [TestMethod]
    public async Task WriteAsync_FirstReplyIsAValidZeroLengthMessage_RetriesAndReturnsTheRealReply()
    {
        var cancellationToken = TestContext.CancellationToken;
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(cancellationToken);

        // Confirmed against a real Rigol DS1102E: a query sent right after OpenAsync sometimes
        // gets back a fully well-formed, EOM-terminated, zero-byte logical message before the
        // real reply - not a physical zero-byte transfer (that's the separate stalled/count<=0
        // path), but a valid header declaring TransferSize=0. A second REQUEST_DEV_DEP_MSG_IN
        // should recover the real data.
        device.EnqueueRead(BuildReplyHeader(_firstQueryRequestTag, transferSize: 0, eom: true));
        device.EnqueueRead(Concat(BuildReplyHeader((byte)(_firstQueryRequestTag + 1), transferSize: 5, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"), cancellationToken);

        var reply = await ReadAvailableAsync(transport.Input, cancellationToken);
        Assert.AreEqual("HELLO", Encoding.ASCII.GetString(reply));
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task WriteAsync_TwoConsecutiveValidZeroLengthMessages_ReturnsAnEmptyReplyWithoutThrowing()
    {
        var cancellationToken = TestContext.CancellationToken;

        var (transport, device) = CreateTransport();
        await transport.OpenAsync(cancellationToken);

        device.EnqueueRead(BuildReplyHeader(_firstQueryRequestTag, transferSize: 0, eom: true));
        device.EnqueueRead(BuildReplyHeader((byte)(_firstQueryRequestTag + 1), transferSize: 0, eom: true));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"), cancellationToken);

        // WrittenFrames[0] is the DEV_DEP_MSG_OUT command, [1]/[2] are the two
        // REQUEST_DEV_DEP_MSG_IN retries (tags 2 and 3) - asserting 3 here (rather than just "no
        // data arrived") is what actually proves the SECOND queued empty header was consumed and
        // tag-validated by the retry-once path, not just that the first one alone produced nothing.
        Assert.HasCount(3, device.WrittenFrames);

        // A genuinely empty reply never advances any bytes into the pipe and the pipe is never
        // completed, so the blocking ReadAvailableAsync (a bare PipeReader.ReadAsync) used by every
        // other test in this file would hang forever here waiting for bytes that are never coming -
        // that's what made this test hang before this fix, not a bug in WriteAsync/ReadReply itself.
        // TryRead is non-blocking: it returns false when nothing has been written yet, which is
        // exactly "an empty reply" for a pipe that's still open.
        Assert.IsFalse(transport.Input.TryRead(out _));
    }

    [TestMethod]
    public async Task WriteAsync_NonQuery_DoesNotReadAReply()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("OUTP ON"), TestContext.CancellationToken);

        // A non-query never issues REQUEST_DEV_DEP_MSG_IN - only the DEV_DEP_MSG_OUT command frame.
        Assert.HasCount(1, device.WrittenFrames);
    }

    [TestMethod]
    public async Task WriteAsync_MessageSplitAcrossTransfersWithEomClear_RequestsEachTransferAndConcatenates()
    {
        var cancellationToken = TestContext.CancellationToken;
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(cancellationToken);

        // USBTMC 1.0 section 3.3.1.1 rules 9/13: a device short on buffer space sends one message
        // as several transfers, EOM clear on all but the last, each answering its own
        // REQUEST_DEV_DEP_MSG_IN (tags 2 and 3 here).
        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 4, eom: false), Encoding.ASCII.GetBytes("ABCD")));
        device.EnqueueRead(Concat(BuildReplyHeader((byte)(_firstQueryRequestTag + 1), transferSize: 4, eom: true), Encoding.ASCII.GetBytes("EFGH")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("WAV:DATA?"), cancellationToken);

        var reply = await ReadAvailableAsync(transport.Input, cancellationToken);
        Assert.AreEqual("ABCDEFGH", Encoding.ASCII.GetString(reply));
        Assert.HasCount(3, device.WrittenFrames);
        Assert.AreEqual(UsbtmcCodec.RequestDevDepMsgIn, device.WrittenFrames[2][0]);
    }

    [TestMethod]
    public async Task WriteAsync_ShortPacketBeforeDeclaredTransferSize_KeepsTheBytesAndThrowsWithoutWaiting()
    {
        var cancellationToken = TestContext.CancellationToken;
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(cancellationToken);

        // Declares 10 bytes but the transfer ends (short packet) after 5. USBTMC 1.0 Table 11
        // index 4: process the bytes that arrived, report a protocol error - the device won't
        // send anything more for this transfer, so waiting for the rest would only time out.
        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 10, eom: true), Encoding.ASCII.GetBytes("HELLO")));

        await Assert.ThrowsExactlyAsync<IOException>(() => transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?"), cancellationToken));

        Assert.IsTrue(transport.Input.TryRead(out var result));
        Assert.AreEqual("HELLO", Encoding.ASCII.GetString(result.Buffer.ToArray()));
    }

    [TestMethod]
    public async Task WriteAsync_TransferFillingTheBufferExactly_ConsumesTheTerminatingZeroLengthPacket()
    {
        var cancellationToken = TestContext.CancellationToken;
        var (transport, device) = CreateTransport(maxTransferSize: 4);
        await transport.OpenAsync(cancellationToken);

        // A 16-byte transfer exactly fills the 16-byte buffer, so the device terminates it with a
        // zero-length packet (rule 10). Stopping once TransferSize is counted off would leave that
        // ZLP queued as the "first read" of the next query.
        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 4, eom: true), Encoding.ASCII.GetBytes("ABCD")));
        device.EnqueueRead([]);
        await transport.WriteAsync(Encoding.ASCII.GetBytes("A?"), cancellationToken);
        Assert.AreEqual("ABCD", Encoding.ASCII.GetString(await ReadAvailableAsync(transport.Input, cancellationToken)));

        // Second query: command tag 3, request tag 4.
        device.EnqueueRead(Concat(BuildReplyHeader(4, transferSize: 2, eom: true), Encoding.ASCII.GetBytes("OK")));
        await transport.WriteAsync(Encoding.ASCII.GetBytes("B?"), cancellationToken);
        Assert.AreEqual("OK", Encoding.ASCII.GetString(await ReadAvailableAsync(transport.Input, cancellationToken)));
    }

    [TestMethod]
    public async Task WriteAsync_ReplyTimesOut_AbortsTheBulkInTransferForThatRequest()
    {
        var cancellationToken = TestContext.CancellationToken;
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(cancellationToken);

        device.EnqueueTimeout();

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => transport.WriteAsync(Encoding.ASCII.GetBytes("*TST?"), cancellationToken));

        // Without the abort the pending request's late reply would answer the next query instead.
        Assert.HasCount(1, device.AbortedBulkInTags);
        Assert.AreEqual(_firstQueryRequestTag, device.AbortedBulkInTags[0]);
    }

    [TestMethod]
    public async Task WriteAsync_BulkOutTimesOut_AbortsTheBulkOutTransfer()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        device.TimeOutNextWrite = true;

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => transport.WriteAsync(Encoding.ASCII.GetBytes("OUTP ON"), TestContext.CancellationToken));
        Assert.HasCount(1, device.AbortedBulkOutTags);
        Assert.AreEqual((byte)1, device.AbortedBulkOutTags[0]);
    }

    [TestMethod]
    public async Task WriteAsync_StaleReplyFromAnEarlierRequestArrivesFirst_SkipsItAndReturnsTheRealReply()
    {
        var cancellationToken = TestContext.CancellationToken;
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(cancellationToken);

        // A reply to some long-abandoned request (bTag 99) is still queued ahead of ours.
        device.EnqueueRead(Concat(BuildReplyHeader(99, transferSize: 5, eom: true), Encoding.ASCII.GetBytes("STALE")));
        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 5, eom: true), Encoding.ASCII.GetBytes("FRESH")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("*IDN?"), cancellationToken);

        Assert.AreEqual("FRESH", Encoding.ASCII.GetString(await ReadAvailableAsync(transport.Input, cancellationToken)));
    }

    [TestMethod]
    public async Task WriteAsync_EmptyData_SendsNothing()
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        // USBTMC 1.0 Table 3: a DEV_DEP_MSG_OUT's TransferSize must be > 0.
        await transport.WriteAsync(ReadOnlyMemory<byte>.Empty, TestContext.CancellationToken);

        Assert.IsEmpty(device.WrittenFrames);
    }

    [TestMethod]
    [DataRow("*IDN?", true)]
    [DataRow("*IDN?\r\n", true)]
    [DataRow("  *IDN?", true)]
    [DataRow(":MEAS:VPP? CHAN1", true)]
    [DataRow(":MEAS:VPP?\tCHAN1", true)]
    [DataRow("*RST;*IDN?", true)]
    [DataRow("SYST:ERR?;*CLS", true)]
    [DataRow("OUTP ON", false)]
    [DataRow(":DISP:TEXT \"a;b?\"", false)]
    public async Task WriteAsync_QueryDetection_ReadsAReplyOnlyForQueries(string command, bool expectQuery)
    {
        var (transport, device) = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        device.EnqueueRead(Concat(BuildReplyHeader(_firstQueryRequestTag, transferSize: 1, eom: true), Encoding.ASCII.GetBytes("1")));

        await transport.WriteAsync(Encoding.ASCII.GetBytes(command), TestContext.CancellationToken);

        Assert.HasCount(expectQuery ? 2 : 1, device.WrittenFrames);
    }

    [TestMethod]
    public async Task OpenAndClose_ByDefault_NoClearButRemoteAndBackToLocal()
    {
        var (transport, device) = CreateTransport();

        await transport.OpenAsync(TestContext.CancellationToken);
        Assert.AreEqual(0, device.ClearCount);
        Assert.AreSequenceEqual(new[] { true }, device.RemoteCalls);

        await transport.CloseAsync(TestContext.CancellationToken);
        Assert.AreSequenceEqual(new[] { true, false }, device.RemoteCalls);
    }

    [TestMethod]
    public async Task OpenAsync_ClearOnOpen_SendsInitiateClear()
    {
        var (transport, device) = CreateTransport(clearOnOpen: true);

        await transport.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(1, device.ClearCount);
    }

    [TestMethod]
    public async Task OpenAndClose_RigolDs1000DgVidPid_NeverSendsRemoteControl()
    {
        // 0x1AB1:0x0588 (Rigol DS1102E / DG1022) - see UsbtmcDeviceQuirks.
        var (transport, device) = CreateTransport(vendorId: 0x1AB1, productId: 0x0588);

        await transport.OpenAsync(TestContext.CancellationToken);
        await transport.CloseAsync(TestContext.CancellationToken);

        Assert.IsEmpty(device.RemoteCalls);
    }

    [TestMethod]
    public async Task DisposeAsync_Twice_DoesNotThrow()
    {
        // Session.DisposeAsync disposes its transport, and an owner's `await using` then disposes
        // it again - confirmed as a real ObjectDisposedException against a real DG1022 run.
        var (transport, _) = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.DisposeAsync();
        await transport.DisposeAsync();
    }

    [TestMethod]
    public void Quirks_RigolDs1000DgVidPid_DelaysTheReadRequestAndSkipsRemoteControl()
    {
        var quirks = UsbtmcDeviceQuirks.For(0x1AB1, 0x0588);

        Assert.IsGreaterThan(0, quirks.RequestDelayMs);
        Assert.IsFalse(quirks.SupportsRemoteControl);
        Assert.AreEqual(UsbtmcDeviceQuirks.None, UsbtmcDeviceQuirks.For(0x1AB1, 0x0642));
    }
}
