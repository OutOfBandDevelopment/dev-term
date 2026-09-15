using System.Buffers;
using System.IO.Pipelines;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;
using Moq;

namespace DevTerm.Transports.Hid.Tests;

[TestClass]
public sealed class HidTransportTests
{
    private static IOptions<HidTransportOptions> Options(int vendorId = 0x1915, int productId = 0xAFDA) =>
        Microsoft.Extensions.Options.Options.Create(new HidTransportOptions { VendorId = vendorId, ProductId = productId });

    /// <summary>
    /// A device double whose <see cref="IHidDevice.BaseStream"/> is backed by a real
    /// <see cref="Pipe"/>: the transport's background pump reads from it for real, and the test
    /// plays "a report arrived" by writing to <c>DevicePipe.Writer</c> — no need to simulate the
    /// read side by mocking a <see cref="Stream"/> or spinning up a real HID read thread.
    /// </summary>
    private static (Mock<IHidDevice> Device, Pipe DevicePipe) CreateDevice()
    {
        var devicePipe = new Pipe();
        var device = new Mock<IHidDevice>();
        device.SetupGet(d => d.BaseStream).Returns(devicePipe.Reader.AsStream());
        return (device, devicePipe);
    }

    [TestMethod]
    public async Task OpenAsync_CreatesAndOpensDeviceFromFactory()
    {
        var (device, _) = CreateDevice();
        var factory = new Mock<IHidDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<HidTransportOptions>())).Returns(device.Object);

        var transport = new HidTransport(factory.Object, Options(vendorId: 0x1234));

        await transport.OpenAsync();

        factory.Verify(f => f.Create(It.Is<HidTransportOptions>(o => o.VendorId == 0x1234)), Times.Once);
        device.Verify(d => d.Open(), Times.Once);
        Assert.AreEqual(ConnectionState.Open, transport.State);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task OpenAsync_RaisesStateChangedThroughOpeningToOpen()
    {
        var (device, _) = CreateDevice();
        var factory = new Mock<IHidDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<HidTransportOptions>())).Returns(device.Object);

        var transport = new HidTransport(factory.Object, Options());
        var states = new List<ConnectionState>();
        transport.StateChanged += (_, e) => states.Add(e.Current);

        await transport.OpenAsync();

        CollectionAssert.AreEqual(new[] { ConnectionState.Opening, ConnectionState.Open }, states);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task OpenAsync_WhenDeviceThrows_TransitionsToFaultedAndRethrows()
    {
        var device = new Mock<IHidDevice>();
        device.Setup(d => d.Open()).Throws(new IOException("no matching HID device"));
        var factory = new Mock<IHidDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<HidTransportOptions>())).Returns(device.Object);

        var transport = new HidTransport(factory.Object, Options());

        await Assert.ThrowsExactlyAsync<IOException>(() => transport.OpenAsync());
        Assert.AreEqual(ConnectionState.Faulted, transport.State);
        device.Verify(d => d.Dispose(), Times.Once);
    }

    [TestMethod]
    public async Task WriteAsync_WhenNotOpen_Throws()
    {
        var transport = new HidTransport(Mock.Of<IHidDeviceFactory>(), Options());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.WriteAsync(new byte[] { 1 }));
    }

    [TestMethod]
    public async Task WriteAsync_WhenOpen_WritesBytesToDevice()
    {
        var (device, _) = CreateDevice();
        var factory = new Mock<IHidDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<HidTransportOptions>())).Returns(device.Object);

        var transport = new HidTransport(factory.Object, Options());
        await transport.OpenAsync();

        var payload = new byte[] { 0x01, 0x02, 0x03 };
        await transport.WriteAsync(payload);

        device.Verify(d => d.Write(payload, 0, payload.Length), Times.Once);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task WriteAsync_WithEmptyData_DoesNotCallDeviceWrite()
    {
        var (device, _) = CreateDevice();
        var factory = new Mock<IHidDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<HidTransportOptions>())).Returns(device.Object);

        var transport = new HidTransport(factory.Object, Options());
        await transport.OpenAsync();

        await transport.WriteAsync(ReadOnlyMemory<byte>.Empty);

        device.Verify(d => d.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task IncomingBytes_AreAvailableThroughInput()
    {
        var (device, devicePipe) = CreateDevice();
        var factory = new Mock<IHidDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<HidTransportOptions>())).Returns(device.Object);

        var transport = new HidTransport(factory.Object, Options());
        await transport.OpenAsync();

        var payload = new byte[] { 0xDE, 0xAD };
        await devicePipe.Writer.WriteAsync(payload);

        var result = await transport.Input.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(payload, result.Buffer.ToArray());
        transport.Input.AdvanceTo(result.Buffer.End);

        await transport.CloseAsync();
    }

    [TestMethod]
    public async Task DeviceClosesTheConnection_TransportTransitionsToClosed()
    {
        var (device, devicePipe) = CreateDevice();
        var factory = new Mock<IHidDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<HidTransportOptions>())).Returns(device.Object);

        var transport = new HidTransport(factory.Object, Options());
        var closedTcs = new TaskCompletionSource();
        transport.StateChanged += (_, e) =>
        {
            if (e.Current == ConnectionState.Closed)
            {
                closedTcs.TrySetResult();
            }
        };

        await transport.OpenAsync();
        await devicePipe.Writer.CompleteAsync();

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_ClosesAndDisposesDevice()
    {
        var (device, _) = CreateDevice();
        var factory = new Mock<IHidDeviceFactory>();
        factory.Setup(f => f.Create(It.IsAny<HidTransportOptions>())).Returns(device.Object);

        var transport = new HidTransport(factory.Object, Options());
        await transport.OpenAsync();

        await transport.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));

        device.Verify(d => d.Close(), Times.Once);
        device.Verify(d => d.Dispose(), Times.Once);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_WhenNeverOpened_DoesNothing()
    {
        var factory = new Mock<IHidDeviceFactory>();
        var transport = new HidTransport(factory.Object, Options());

        await transport.CloseAsync();

        factory.Verify(f => f.Create(It.IsAny<HidTransportOptions>()), Times.Never);
    }
}
