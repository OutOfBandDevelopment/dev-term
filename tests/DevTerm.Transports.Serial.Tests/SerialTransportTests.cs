using System.Buffers;
using System.IO.Pipelines;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;
using Moq;

namespace DevTerm.Transports.Serial.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class SerialTransportTests
{
    private static IOptions<SerialTransportOptions> Options(string portName = "COM1") =>
        Microsoft.Extensions.Options.Options.Create(new SerialTransportOptions { PortName = portName });

    /// <summary>
    /// A port double whose <see cref="ISerialPort.BaseStream"/> is backed by a real
    /// <see cref="Pipe"/>: the transport's background pump reads from it for real, and the test
    /// plays "bytes arrived on the wire" by writing to <c>DevicePipe.Writer</c> — no need to
    /// simulate the read side by mocking a <see cref="Stream"/> or raising events.
    /// </summary>
    private static (Mock<ISerialPort> Port, Pipe DevicePipe) CreatePort()
    {
        var devicePipe = new Pipe();
        var port = new Mock<ISerialPort>();
        port.SetupGet(p => p.BaseStream).Returns(devicePipe.Reader.AsStream());
        return (port, devicePipe);
    }

    [TestMethod]
    public async Task OpenAsync_CreatesAndOpensPortFromFactory()
    {
        var (port, _) = CreatePort();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options("COM3"));

        await transport.OpenAsync(TestContext.CancellationToken);

        factory.Verify(f => f.Create(It.Is<SerialTransportOptions>(o => o.PortName == "COM3")), Times.Once);
        port.Verify(p => p.Open(), Times.Once);
        Assert.AreEqual(ConnectionState.Open, transport.State);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task OpenAsync_RaisesStateChangedThroughOpeningToOpen()
    {
        var (port, _) = CreatePort();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        var states = new List<ConnectionState>();
        transport.StateChanged += (_, e) => states.Add(e.Current);

        await transport.OpenAsync(TestContext.CancellationToken);

        Assert.AreSequenceEqual([ConnectionState.Opening, ConnectionState.Open], states);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task OpenAsync_WhenPortThrows_TransitionsToFaultedAndRethrows()
    {
        var port = new Mock<ISerialPort>();
        port.Setup(p => p.Open()).Throws(new UnauthorizedAccessException("port in use"));
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => transport.OpenAsync(TestContext.CancellationToken));
        Assert.AreEqual(ConnectionState.Faulted, transport.State);
        port.Verify(p => p.Dispose(), Times.Once);
    }

    [TestMethod]
    public async Task WriteAsync_WhenNotOpen_Throws()
    {
        var transport = new SerialTransport(Mock.Of<ISerialPortFactory>(), Options());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.WriteAsync(new byte[] { 1 }, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task WriteAsync_WhenOpen_WritesBytesToPort()
    {
        var (port, _) = CreatePort();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        await transport.OpenAsync(TestContext.CancellationToken);

        var payload = new byte[] { 0x01, 0x02, 0x03 };
        await transport.WriteAsync(payload, TestContext.CancellationToken);

        port.Verify(p => p.Write(payload, 0, payload.Length), Times.Once);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task IncomingBytes_AreAvailableThroughInput()
    {
        var (port, devicePipe) = CreatePort();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        await transport.OpenAsync(TestContext.CancellationToken);

        var payload = "ޭ"u8.ToArray();
        await devicePipe.Writer.WriteAsync(payload, TestContext.CancellationToken);

        var result = await transport.Input.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(payload, result.Buffer.ToArray());
        transport.Input.AdvanceTo(result.Buffer.End);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task DeviceClosesTheConnection_TransportTransitionsToClosed()
    {
        var (port, devicePipe) = CreatePort();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        var closedTcs = new TaskCompletionSource();
        transport.StateChanged += (_, e) =>
        {
            if (e.Current == ConnectionState.Closed)
            {
                closedTcs.TrySetResult();
            }
        };

        await transport.OpenAsync(TestContext.CancellationToken);
        await devicePipe.Writer.CompleteAsync();

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_ClosesAndDisposesPort()
    {
        var (port, _) = CreatePort();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.CloseAsync(TestContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        port.Verify(p => p.Close(), Times.Once);
        port.Verify(p => p.Dispose(), Times.Once);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_WhenNeverOpened_DoesNothing()
    {
        var factory = new Mock<ISerialPortFactory>();
        var transport = new SerialTransport(factory.Object, Options());

        await transport.CloseAsync(TestContext.CancellationToken);

        factory.Verify(f => f.Create(It.IsAny<SerialTransportOptions>()), Times.Never);
    }

    public TestContext TestContext { get; set; }
}
