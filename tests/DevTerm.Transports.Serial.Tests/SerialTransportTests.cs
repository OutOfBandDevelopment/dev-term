using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;
using Moq;

namespace DevTerm.Transports.Serial.Tests;

[TestClass]
public sealed class SerialTransportTests
{
    private static IOptions<SerialTransportOptions> Options(string portName = "COM1") =>
        Microsoft.Extensions.Options.Options.Create(new SerialTransportOptions { PortName = portName });

    [TestMethod]
    public async Task OpenAsync_CreatesAndOpensPortFromFactory()
    {
        var port = new Mock<ISerialPort>();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options("COM3"));

        await transport.OpenAsync();

        factory.Verify(f => f.Create(It.Is<SerialTransportOptions>(o => o.PortName == "COM3")), Times.Once);
        port.Verify(p => p.Open(), Times.Once);
        Assert.AreEqual(ConnectionState.Open, transport.State);
    }

    [TestMethod]
    public async Task OpenAsync_RaisesStateChangedThroughOpeningToOpen()
    {
        var port = new Mock<ISerialPort>();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        var states = new List<ConnectionState>();
        transport.StateChanged += (_, e) => states.Add(e.Current);

        await transport.OpenAsync();

        CollectionAssert.AreEqual(new[] { ConnectionState.Opening, ConnectionState.Open }, states);
    }

    [TestMethod]
    public async Task OpenAsync_WhenPortThrows_TransitionsToFaultedAndRethrows()
    {
        var port = new Mock<ISerialPort>();
        port.Setup(p => p.Open()).Throws(new UnauthorizedAccessException("port in use"));
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => transport.OpenAsync());
        Assert.AreEqual(ConnectionState.Faulted, transport.State);
        port.Verify(p => p.Dispose(), Times.Once);
    }

    [TestMethod]
    public async Task WriteAsync_WhenNotOpen_Throws()
    {
        var transport = new SerialTransport(Mock.Of<ISerialPortFactory>(), Options());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.WriteAsync(new byte[] { 1 }));
    }

    [TestMethod]
    public async Task WriteAsync_WhenOpen_WritesBytesToPort()
    {
        var port = new Mock<ISerialPort>();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        await transport.OpenAsync();

        var payload = new byte[] { 0x01, 0x02, 0x03 };
        await transport.WriteAsync(payload);

        port.Verify(p => p.Write(payload, 0, payload.Length), Times.Once);
    }

    [TestMethod]
    public async Task PortDataReceived_IsForwardedAsTransportDataReceived()
    {
        var port = new Mock<ISerialPort>();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        await transport.OpenAsync();

        ReadOnlyMemory<byte>? received = null;
        transport.DataReceived += (_, e) => received = e.Data;

        var payload = new byte[] { 0xDE, 0xAD };
        port.Raise(p => p.DataReceived += null, port.Object, new SerialPortDataReceivedEventArgs(payload));

        Assert.IsTrue(received.HasValue);
        CollectionAssert.AreEqual(payload, received!.Value.ToArray());
    }

    [TestMethod]
    public async Task CloseAsync_ClosesAndDisposesPort()
    {
        var port = new Mock<ISerialPort>();
        var factory = new Mock<ISerialPortFactory>();
        factory.Setup(f => f.Create(It.IsAny<SerialTransportOptions>())).Returns(port.Object);

        var transport = new SerialTransport(factory.Object, Options());
        await transport.OpenAsync();

        await transport.CloseAsync();

        port.Verify(p => p.Close(), Times.Once);
        port.Verify(p => p.Dispose(), Times.Once);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_WhenNeverOpened_DoesNothing()
    {
        var factory = new Mock<ISerialPortFactory>();
        var transport = new SerialTransport(factory.Object, Options());

        await transport.CloseAsync();

        factory.Verify(f => f.Create(It.IsAny<SerialTransportOptions>()), Times.Never);
    }
}
