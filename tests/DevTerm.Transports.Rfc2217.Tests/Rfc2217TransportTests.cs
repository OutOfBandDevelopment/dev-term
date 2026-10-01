using System.Buffers;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Tcp;
using Moq;

namespace DevTerm.Transports.Rfc2217.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Rfc2217)]
[TestClass]
public sealed class Rfc2217TransportTests
{
    private static Rfc2217Transport CreateTransport(FakeRfc2217Server server, int negotiationTimeoutMs = 3000, int writeTimeoutMs = 5000)
    {
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(server.Connection.Object);

        var options = Microsoft.Extensions.Options.Options.Create(new Rfc2217TransportOptions
        {
            Host = "device.local",
            Port = 2217,
            BaudRate = 9600,
            DataBits = 8,
            NegotiationTimeoutMs = negotiationTimeoutMs,
            WriteTimeoutMs = writeTimeoutMs,
        });

        return new Rfc2217Transport(source.Object, options);
    }

    [TestMethod]
    public async Task OpenAsync_NegotiationAccepted_SendsOfferThenInitialComPortConfiguration()
    {
        var server = new FakeRfc2217Server();
        await server.SendWillDoComPortOptionAsync(TestContext.CancellationToken);
        var transport = CreateTransport(server);

        await transport.OpenAsync(TestContext.CancellationToken);

        Assert.IsTrue(transport.ComPortControlNegotiated);
        Assert.AreEqual(ConnectionState.Open, transport.State);

        var written = await server.WaitForNextWriteAsync(TestContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        byte[] expected =
        [
            .. Telnet.BuildWillDo(Telnet.ComPortOption),
            .. Rfc2217Codec.EncodeSetBaudRate(9600),
            .. Rfc2217Codec.EncodeSetDataSize(ComPortDataSize.Eight),
            .. Rfc2217Codec.EncodeSetParity(ComPortParity.None),
            .. Rfc2217Codec.EncodeSetStopSize(ComPortStopSize.One),
            .. Rfc2217Codec.EncodeSetControl(Rfc2217ControlValue.SetDtrStateOn),
            .. Rfc2217Codec.EncodeSetControl(Rfc2217ControlValue.SetRtsStateOn),
        ];
        Assert.AreSequenceEqual(expected, written);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task OpenAsync_NegotiationRefused_ThrowsAndFaultsAndDisposesConnection()
    {
        var server = new FakeRfc2217Server();
        await server.SendWontDontComPortOptionAsync(TestContext.CancellationToken);
        var transport = CreateTransport(server);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.OpenAsync(TestContext.CancellationToken));

        Assert.AreEqual(ConnectionState.Faulted, transport.State);
        server.Connection.Verify(c => c.Dispose(), Times.Once);
    }

    [TestMethod]
    public async Task OpenAsync_NoNegotiationReply_FallsBackToTolerantPassthrough()
    {
        var server = new FakeRfc2217Server();
        var transport = CreateTransport(server, negotiationTimeoutMs: 100);

        await transport.OpenAsync(TestContext.CancellationToken);

        Assert.IsFalse(transport.ComPortControlNegotiated);
        Assert.AreEqual(ConnectionState.Open, transport.State);

        var written = await server.WaitForNextWriteAsync(TestContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(Telnet.BuildWillDo(Telnet.ComPortOption), written);

        await server.SendDataAsync("hi"u8.ToArray(), TestContext.CancellationToken);
        var result = await transport.Input.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual("hi"u8.ToArray(), result.Buffer.ToArray());
        transport.Input.AdvanceTo(result.Buffer.End);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task Input_DecodesEscapedDataFromTheWire()
    {
        var server = new FakeRfc2217Server();
        await server.SendWillDoComPortOptionAsync(TestContext.CancellationToken);
        var transport = CreateTransport(server);
        await transport.OpenAsync(TestContext.CancellationToken);

        await server.SendDataAsync([0x01, 0xFF, 0x02], TestContext.CancellationToken);

        var result = await transport.Input.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(new byte[] { 0x01, 0xFF, 0x02 }, result.Buffer.ToArray());
        transport.Input.AdvanceTo(result.Buffer.End);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task Input_NeverSurfacesNotifyLineStateSubnegotiations()
    {
        var server = new FakeRfc2217Server();
        await server.SendWillDoComPortOptionAsync(TestContext.CancellationToken);
        var transport = CreateTransport(server);
        await transport.OpenAsync(TestContext.CancellationToken);

        await server.SendNotifyLineStateAsync(Rfc2217LineState.BreakDetect, TestContext.CancellationToken);
        await server.SendDataAsync("ok"u8.ToArray(), TestContext.CancellationToken);

        var result = await transport.Input.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual("ok"u8.ToArray(), result.Buffer.ToArray());
        transport.Input.AdvanceTo(result.Buffer.End);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task WriteAsync_EscapesIacBytesBeforeSendingToTheWire()
    {
        var server = new FakeRfc2217Server();
        await server.SendWillDoComPortOptionAsync(TestContext.CancellationToken);
        var transport = CreateTransport(server);
        await transport.OpenAsync(TestContext.CancellationToken);
        server.DrainPendingOutgoing();

        await transport.WriteAsync(new byte[] { 0x01, 0xFF, 0x02 }, TestContext.CancellationToken);

        var written = await server.WaitForNextWriteAsync(TestContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(new byte[] { 0x01, 0xFF, 0xFF, 0x02 }, written);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task WriteAsync_WhenNotOpen_Throws()
    {
        var server = new FakeRfc2217Server();
        var transport = CreateTransport(server);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.WriteAsync(new byte[] { 1 }, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task WriteAsync_ConnectionNeverDrains_ThrowsTimeoutExceptionAfterWriteTimeoutMs()
    {
        var server = new FakeRfc2217Server(outgoingPauseWriterThreshold: 1);
        await server.SendWillDoComPortOptionAsync(TestContext.CancellationToken);

        using var drainCts = new CancellationTokenSource();
        var drainTask = server.DrainOutgoingContinuouslyAsync(drainCts.Token);

        var transport = CreateTransport(server, writeTimeoutMs: 50);
        await transport.OpenAsync(TestContext.CancellationToken);

        await drainCts.CancelAsync();
        await drainTask;

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => transport.WriteAsync(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, TestContext.CancellationToken));

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task RemoteClosesTheConnection_TransportTransitionsToClosed()
    {
        var server = new FakeRfc2217Server();
        await server.SendWillDoComPortOptionAsync(TestContext.CancellationToken);
        var transport = CreateTransport(server);
        var closedTcs = new TaskCompletionSource();
        transport.StateChanged += (_, e) =>
        {
            if (e.Current == ConnectionState.Closed)
            {
                closedTcs.TrySetResult();
            }
        };

        await transport.OpenAsync(TestContext.CancellationToken);
        await server.CompleteIncomingAsync();

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_ClosesAndDisposesConnection()
    {
        var server = new FakeRfc2217Server();
        await server.SendWillDoComPortOptionAsync(TestContext.CancellationToken);
        var transport = CreateTransport(server);
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.CloseAsync(TestContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        server.Connection.Verify(c => c.Dispose(), Times.Once);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_WhenNeverOpened_DoesNothing()
    {
        var server = new FakeRfc2217Server();
        var transport = CreateTransport(server);

        await transport.CloseAsync(TestContext.CancellationToken);

        server.Connection.Verify(c => c.Dispose(), Times.Never);
    }

    public required TestContext TestContext { get; set; }
}
