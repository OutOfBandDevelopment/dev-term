using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;
using Moq;

namespace DevTerm.Transports.Tcp.Tests;

[TestClass]
public sealed class TcpTransportTests
{
    private static IOptions<TcpTransportOptions> Options(TcpTransportMode mode, string? host = "device.local", int port = 502) =>
        Microsoft.Extensions.Options.Options.Create(new TcpTransportOptions { Mode = mode, Host = host, Port = port });

    [TestMethod]
    public async Task OpenAsync_InClientMode_DialsOutViaConnectionSource()
    {
        var connection = new Mock<ITcpConnection>();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));

        await transport.OpenAsync();

        source.Verify(s => s.ConnectAsync(It.Is<TcpTransportOptions>(o => o.Host == "device.local"), It.IsAny<CancellationToken>()), Times.Once);
        source.Verify(s => s.AcceptAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(ConnectionState.Open, transport.State);
    }

    [TestMethod]
    public async Task OpenAsync_InListenerMode_AcceptsViaConnectionSource()
    {
        var connection = new Mock<ITcpConnection>();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.AcceptAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Listener, host: null, port: 9000));

        await transport.OpenAsync();

        source.Verify(s => s.AcceptAsync(It.Is<TcpTransportOptions>(o => o.Port == 9000), It.IsAny<CancellationToken>()), Times.Once);
        source.Verify(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(ConnectionState.Open, transport.State);
    }

    [TestMethod]
    public async Task OpenAsync_RaisesStateChangedThroughOpeningToOpen()
    {
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ITcpConnection>());

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        var states = new List<ConnectionState>();
        transport.StateChanged += (_, e) => states.Add(e.Current);

        await transport.OpenAsync();

        CollectionAssert.AreEqual(new[] { ConnectionState.Opening, ConnectionState.Open }, states);
    }

    [TestMethod]
    public async Task OpenAsync_WhenConnectionSourceThrows_TransitionsToFaultedAndRethrows()
    {
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SocketExceptionStub());

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));

        await Assert.ThrowsExactlyAsync<SocketExceptionStub>(() => transport.OpenAsync());
        Assert.AreEqual(ConnectionState.Faulted, transport.State);
    }

    [TestMethod]
    public async Task WriteAsync_WhenNotOpen_Throws()
    {
        var transport = new TcpTransport(Mock.Of<ITcpConnectionSource>(), Options(TcpTransportMode.Client));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.WriteAsync(new byte[] { 1 }));
    }

    [TestMethod]
    public async Task WriteAsync_WhenOpen_WritesBytesToConnection()
    {
        var connection = new Mock<ITcpConnection>();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        await transport.OpenAsync();

        var payload = new byte[] { 0x01, 0x02, 0x03 };
        await transport.WriteAsync(payload);

        connection.Verify(c => c.Write(payload, 0, payload.Length), Times.Once);
    }

    [TestMethod]
    public async Task ConnectionDataReceived_IsForwardedAsTransportDataReceived()
    {
        var connection = new Mock<ITcpConnection>();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        await transport.OpenAsync();

        ReadOnlyMemory<byte>? received = null;
        transport.DataReceived += (_, e) => received = e.Data;

        var payload = new byte[] { 0xDE, 0xAD };
        connection.Raise(c => c.DataReceived += null, connection.Object, new TcpDataReceivedEventArgs(payload));

        Assert.IsTrue(received.HasValue);
        CollectionAssert.AreEqual(payload, received!.Value.ToArray());
    }

    [TestMethod]
    public async Task ConnectionClosed_TransitionsTransportToClosed()
    {
        var connection = new Mock<ITcpConnection>();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        await transport.OpenAsync();

        connection.Raise(c => c.Closed += null, connection.Object, EventArgs.Empty);

        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_ClosesAndDisposesConnection()
    {
        var connection = new Mock<ITcpConnection>();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        await transport.OpenAsync();

        await transport.CloseAsync();

        connection.Verify(c => c.Dispose(), Times.Once);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_WhenNeverOpened_DoesNothing()
    {
        var source = new Mock<ITcpConnectionSource>();
        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));

        await transport.CloseAsync();

        source.Verify(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Stand-in for a real socket exception so the test doesn't depend on actual networking.</summary>
    private sealed class SocketExceptionStub : Exception;
}
