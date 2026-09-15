using System.Buffers;
using System.IO.Pipelines;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;
using Moq;

namespace DevTerm.Transports.Tcp.Tests;

[TestCategory("UNIT")]
[TestClass]
public sealed class TcpTransportTests
{
    private static IOptions<TcpTransportOptions> Options(TcpTransportMode mode, string? host = "device.local", int port = 502) =>
        Microsoft.Extensions.Options.Options.Create(new TcpTransportOptions { Mode = mode, Host = host, Port = port });

    /// <summary>
    /// A connection double whose <see cref="ITcpConnection.Stream"/> is backed by a real
    /// <see cref="Pipe"/>: the transport's background pump reads from it for real, and the test
    /// plays "bytes arrived on the wire" by writing to <c>WirePipe.Writer</c>.
    /// </summary>
    private static (Mock<ITcpConnection> Connection, Pipe WirePipe) CreateConnection()
    {
        var wirePipe = new Pipe();
        var connection = new Mock<ITcpConnection>();
        connection.SetupGet(c => c.Stream).Returns(wirePipe.Reader.AsStream());
        return (connection, wirePipe);
    }

    [TestMethod]
    public async Task OpenAsync_InClientMode_DialsOutViaConnectionSource()
    {
        var (connection, _) = CreateConnection();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));

        await transport.OpenAsync();

        source.Verify(s => s.ConnectAsync(It.Is<TcpTransportOptions>(o => o.Host == "device.local"), It.IsAny<CancellationToken>()), Times.Once);
        source.Verify(s => s.AcceptAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(ConnectionState.Open, transport.State);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task OpenAsync_InListenerMode_AcceptsViaConnectionSource()
    {
        var (connection, _) = CreateConnection();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.AcceptAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Listener, host: null, port: 9000));

        await transport.OpenAsync();

        source.Verify(s => s.AcceptAsync(It.Is<TcpTransportOptions>(o => o.Port == 9000), It.IsAny<CancellationToken>()), Times.Once);
        source.Verify(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(ConnectionState.Open, transport.State);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task OpenAsync_RaisesStateChangedThroughOpeningToOpen()
    {
        var (connection, _) = CreateConnection();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        var states = new List<ConnectionState>();
        transport.StateChanged += (_, e) => states.Add(e.Current);

        await transport.OpenAsync();

        CollectionAssert.AreEqual(new[] { ConnectionState.Opening, ConnectionState.Open }, states);

        await transport.CloseAsync();
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
        var (connection, _) = CreateConnection();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        await transport.OpenAsync();

        var payload = new byte[] { 0x01, 0x02, 0x03 };
        await transport.WriteAsync(payload);

        connection.Verify(c => c.Write(payload, 0, payload.Length), Times.Once);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task IncomingBytes_AreAvailableThroughInput()
    {
        var (connection, wirePipe) = CreateConnection();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        await transport.OpenAsync();

        var payload = new byte[] { 0xDE, 0xAD };
        await wirePipe.Writer.WriteAsync(payload);

        var result = await transport.Input.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(payload, result.Buffer.ToArray());
        transport.Input.AdvanceTo(result.Buffer.End);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task RemoteClosesTheConnection_TransportTransitionsToClosed()
    {
        var (connection, wirePipe) = CreateConnection();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        var closedTcs = new TaskCompletionSource();
        transport.StateChanged += (_, e) =>
        {
            if (e.Current == ConnectionState.Closed)
            {
                closedTcs.TrySetResult();
            }
        };

        await transport.OpenAsync();
        await wirePipe.Writer.CompleteAsync();

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_ClosesAndDisposesConnection()
    {
        var (connection, _) = CreateConnection();
        var source = new Mock<ITcpConnectionSource>();
        source.Setup(s => s.ConnectAsync(It.IsAny<TcpTransportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var transport = new TcpTransport(source.Object, Options(TcpTransportMode.Client));
        await transport.OpenAsync();

        await transport.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));

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
