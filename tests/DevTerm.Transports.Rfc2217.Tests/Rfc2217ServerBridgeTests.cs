using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Transports.Rfc2217.Tests;

/// <summary>The RFC 2217 server mode: a raw Telnet client negotiates, sets the line, and exchanges escaped data.</summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Rfc2217)]
[TestClass]
public sealed class Rfc2217ServerBridgeTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Client_NegotiatesSetsTheLineAndExchangesEscapedData()
    {
        var pipe = new Pipe();
        var written = new List<byte>();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        transport.Setup(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((data, _) => { lock (written) { written.AddRange(data.ToArray()); } })
            .Returns(Task.CompletedTask);
        await using var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        await using var bridge = new Rfc2217ServerBridge(session, IPAddress.Loopback, 0);
        using var registration = session.AddObserver(bridge);
        Rfc2217PortSettings? changed = null;
        bridge.SettingsChanged += s => changed = s;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, bridge.Port, TestContext.CancellationToken);
        var stream = client.GetStream();

        // WILL COM-PORT, then SET-BAUDRATE 19200, SET-CONTROL DTR off, and data containing 0xFF (escaped on the wire).
        await stream.WriteAsync(new byte[] { 0xFF, 0xFB, 44 }, TestContext.CancellationToken);
        await stream.WriteAsync(Rfc2217Codec.EncodeSetBaudRate(19200), TestContext.CancellationToken);
        await stream.WriteAsync(Rfc2217Codec.EncodeSetControl(Rfc2217ControlValue.SetDtrStateOff), TestContext.CancellationToken);
        await stream.WriteAsync(new byte[] { 0x41, 0xFF, 0xFF, 0x42 }, TestContext.CancellationToken);

        var reply = new List<byte>();
        var buffer = new byte[256];
        var expectedBaudAck = Rfc2217Codec.EncodeSubnegotiation(101, [0, 0, 0x4B, 0]);
        while (!Contains(reply, expectedBaudAck))
        {
            var read = await stream.ReadAsync(buffer, TestContext.CancellationToken);
            Assert.IsGreaterThan(0, read, "server closed before acknowledging the baud rate");
            reply.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        Assert.IsTrue(Contains(reply, [0xFF, 0xFD, 44]), "server should answer WILL COM-PORT with DO");
        Assert.AreEqual(19200, bridge.Settings.BaudRate);
        Assert.IsFalse(bridge.Settings.Dtr);
        Assert.IsNotNull(changed);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (written)
            {
                if (written.Count >= 3)
                {
                    break;
                }
            }

            await Task.Delay(10, TestContext.CancellationToken);
        }

        lock (written)
        {
            CollectionAssert.AreEqual(new byte[] { 0x41, 0xFF, 0x42 }, written.ToArray());
        }

        // Device data containing 0xFF reaches the client doubled.
        await pipe.Writer.WriteAsync(new byte[] { 0x01, 0xFF }, TestContext.CancellationToken);
        reply.Clear();
        while (!Contains(reply, [0x01, 0xFF, 0xFF]))
        {
            var read = await stream.ReadAsync(buffer, TestContext.CancellationToken);
            Assert.IsGreaterThan(0, read);
            reply.AddRange(buffer.AsSpan(0, read).ToArray());
        }
    }

    [TestMethod]
    public async Task Client_SettingsAreAppliedToATransportThatImplementsIComPortControl()
    {
        var pipe = new Pipe();
        var transport = new Mock<ITransport>();
        var control = transport.As<IComPortControl>();
        control.Setup(c => c.SetBaudRate(It.IsAny<int>())).Returns(control.Object);
        control.Setup(c => c.SetDataBits(It.IsAny<int>())).Returns(control.Object);
        control.Setup(c => c.SetParity(It.IsAny<ComParity>())).Returns(control.Object);
        control.Setup(c => c.SetStopBits(It.IsAny<ComStopBits>())).Returns(control.Object);
        control.Setup(c => c.SetDtr(It.IsAny<bool>())).Returns(control.Object);
        control.Setup(c => c.SetRts(It.IsAny<bool>())).Returns(control.Object);
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        await using var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        await using var bridge = new Rfc2217ServerBridge(session, IPAddress.Loopback, 0);
        using var registration = session.AddObserver(bridge);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, bridge.Port, TestContext.CancellationToken);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 0xFF, 0xFB, 44 }, TestContext.CancellationToken);
        await stream.WriteAsync(Rfc2217Codec.EncodeSetBaudRate(57600), TestContext.CancellationToken);
        await stream.WriteAsync(Rfc2217Codec.EncodeSetControl(Rfc2217ControlValue.SetDtrStateOff), TestContext.CancellationToken);

        var reply = new List<byte>();
        var buffer = new byte[256];
        var expectedBaudAck = Rfc2217Codec.EncodeSubnegotiation(101, [0, 0, 0xE1, 0x00]);
        while (!Contains(reply, expectedBaudAck))
        {
            var read = await stream.ReadAsync(buffer, TestContext.CancellationToken);
            Assert.IsGreaterThan(0, read);
            reply.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        control.Verify(c => c.SetBaudRate(57600), Times.AtLeastOnce);
        control.Verify(c => c.SetDtr(false), Times.AtLeastOnce);
    }

    private static bool Contains(List<byte> haystack, byte[] needle) =>
        haystack.Count >= needle.Length && haystack.ToArray().AsSpan().IndexOf(needle) >= 0;
}
