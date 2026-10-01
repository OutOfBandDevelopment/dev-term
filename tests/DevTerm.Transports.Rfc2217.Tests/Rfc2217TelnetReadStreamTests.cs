using System.Buffers;
using System.IO.Pipelines;
using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Rfc2217.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Rfc2217)]
[TestClass]
public sealed class Rfc2217TelnetReadStreamTests
{
    private static (Rfc2217TelnetReadStream Stream, Pipe Incoming, Pipe Outgoing) CreateStream()
    {
        var incoming = new Pipe();
        var outgoing = new Pipe();
        var stream = new Rfc2217TelnetReadStream(new DuplexPipeStream(incoming, outgoing));
        return (stream, incoming, outgoing);
    }

    private static async Task WriteIncomingAsync(Pipe incoming, byte[] bytes, CancellationToken cancellationToken) =>
        await incoming.Writer.WriteAsync(bytes, cancellationToken);

    [TestMethod]
    public async Task ReadAsync_PlainData_PassesThroughUnchanged()
    {
        var (stream, incoming, _) = CreateStream();
        await WriteIncomingAsync(incoming, "hello"u8.ToArray(), TestContext.CancellationToken);

        var buffer = new byte[16];
        var n = await stream.ReadAsync(buffer, TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.AreSequenceEqual("hello"u8.ToArray(), buffer.AsSpan(0, n).ToArray());
    }

    [TestMethod]
    public async Task ReadAsync_EscapedIacInDataChannel_Unescapes()
    {
        var (stream, incoming, _) = CreateStream();
        await WriteIncomingAsync(incoming, [0x01, Telnet.Iac, Telnet.Iac, 0x02], TestContext.CancellationToken);

        var buffer = new byte[16];
        var n = await stream.ReadAsync(buffer, TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.AreSequenceEqual(new byte[] { 0x01, Telnet.Iac, 0x02 }, buffer.AsSpan(0, n).ToArray());
    }

    [TestMethod]
    public async Task ReadAsync_ComPortOptionSubnegotiation_IsStrippedFromDataAndRaisesComPortMessageReceived()
    {
        var (stream, incoming, _) = CreateStream();
        IRfc2217Message? received = null;
        stream.ComPortMessageReceived += m => received = m;

        var subnegotiation = Rfc2217Codec.EncodeSetBaudRate(9600);
        await WriteIncomingAsync(incoming, [.. subnegotiation, 0x41], TestContext.CancellationToken);

        var buffer = new byte[16];
        var n = await stream.ReadAsync(buffer, TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.AreSequenceEqual(new byte[] { 0x41 }, buffer.AsSpan(0, n).ToArray());
        var message = Assert.IsInstanceOfType<Rfc2217BaudRateMessage>(received);
        Assert.AreEqual(9600, message.BaudRate);
    }

    [TestMethod]
    public async Task ReadAsync_ChunkIsEntirelyControlBytes_DoesNotReturnZero_KeepsWaitingForRealData()
    {
        var (stream, incoming, _) = CreateStream();
        var buffer = new byte[16];
        var readTask = stream.ReadAsync(buffer, TestContext.CancellationToken).AsTask();

        await WriteIncomingAsync(incoming, Telnet.BuildWillDo(Telnet.ComPortOption), TestContext.CancellationToken);
        await Task.Delay(200, TestContext.CancellationToken);
        Assert.IsFalse(readTask.IsCompleted, "A read that only consumed Telnet negotiation bytes must not complete with 0 - it must keep waiting for real data.");

        await WriteIncomingAsync(incoming, "ok"u8.ToArray(), TestContext.CancellationToken);
        var n = await readTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.AreSequenceEqual("ok"u8.ToArray(), buffer.AsSpan(0, n).ToArray());
    }

    [TestMethod]
    public async Task ReadAsync_NegotiationCommandSplitAcrossTwoUnderlyingReads_StillResolvesCorrectly()
    {
        var (stream, incoming, _) = CreateStream();
        var negotiationReceived = new TaskCompletionSource<bool>();
        stream.ComPortOptionNegotiationReceived += accepted => negotiationReceived.TrySetResult(accepted);

        var buffer = new byte[16];
        var readTask = stream.ReadAsync(buffer, TestContext.CancellationToken).AsTask();

        // IAC WILL COM-PORT-OPTION, split mid-command: [IAC, WILL] then [COM-PORT-OPTION] as two
        // separate underlying stream reads - exercises the state machine resuming across reads.
        await WriteIncomingAsync(incoming, [Telnet.Iac, Telnet.Will], TestContext.CancellationToken);
        await Task.Delay(100, TestContext.CancellationToken);
        Assert.IsFalse(readTask.IsCompleted);
        Assert.IsFalse(negotiationReceived.Task.IsCompleted);

        await WriteIncomingAsync(incoming, [Telnet.ComPortOption], TestContext.CancellationToken);
        var accepted = await negotiationReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsTrue(accepted);

        await WriteIncomingAsync(incoming, "ok"u8.ToArray(), TestContext.CancellationToken);
        var n = await readTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual("ok"u8.ToArray(), buffer.AsSpan(0, n).ToArray());
    }

    [TestMethod]
    public async Task ReadAsync_UnrelatedOptionOffer_SendsAutoRefusalAndNeverLeaksIntoData()
    {
        var (stream, incoming, outgoing) = CreateStream();
        var buffer = new byte[16];
        var readTask = stream.ReadAsync(buffer, TestContext.CancellationToken).AsTask();

        await WriteIncomingAsync(incoming, [Telnet.Iac, Telnet.Will, 1], TestContext.CancellationToken);

        var refusal = await outgoing.Reader.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(new byte[] { Telnet.Iac, Telnet.Dont, 1 }, refusal.Buffer.ToArray());
        outgoing.Reader.AdvanceTo(refusal.Buffer.End);

        Assert.IsFalse(readTask.IsCompleted);

        await WriteIncomingAsync(incoming, "ok"u8.ToArray(), TestContext.CancellationToken);
        var n = await readTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual("ok"u8.ToArray(), buffer.AsSpan(0, n).ToArray());
    }

    [TestMethod]
    public async Task ReadAsync_GenuineInnerStreamEof_ReturnsZero()
    {
        var (stream, incoming, _) = CreateStream();
        await incoming.Writer.CompleteAsync();

        var buffer = new byte[16];
        var n = await stream.ReadAsync(buffer, TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.AreEqual(0, n);
    }

    [TestMethod]
    public async Task ComPortOptionNegotiationReceived_FiresFalseForWontDont()
    {
        var (stream, incoming, _) = CreateStream();
        var negotiationReceived = new TaskCompletionSource<bool>();
        stream.ComPortOptionNegotiationReceived += accepted => negotiationReceived.TrySetResult(accepted);
        _ = stream.ReadAsync(new byte[16], TestContext.CancellationToken).AsTask();

        await WriteIncomingAsync(incoming, [Telnet.Iac, Telnet.Wont, Telnet.ComPortOption, Telnet.Iac, Telnet.Dont, Telnet.ComPortOption], TestContext.CancellationToken);

        var accepted = await negotiationReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public async Task WriteAsync_EscapesIacBytesBeforeSendingToInnerStream()
    {
        var (stream, _, outgoing) = CreateStream();

        await stream.WriteAsync(new byte[] { 0x01, Telnet.Iac, 0x02 }, TestContext.CancellationToken);

        var result = await outgoing.Reader.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(new byte[] { 0x01, Telnet.Iac, Telnet.Iac, 0x02 }, result.Buffer.ToArray());
    }

    [TestMethod]
    public async Task SendRawFramedAsync_DoesNotEscapeAlreadyFramedBytes()
    {
        var (stream, _, outgoing) = CreateStream();
        var offer = Telnet.BuildWillDo(Telnet.ComPortOption);

        await stream.SendRawFramedAsync(offer, TestContext.CancellationToken);

        var result = await outgoing.Reader.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(offer, result.Buffer.ToArray());
    }

    public required TestContext TestContext { get; set; }
}
