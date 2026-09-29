using System.Buffers;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;
using Moq;

namespace DevTerm.Transports.Ble.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Ble)]
[TestClass]
public sealed class BleTransportTests
{
    private static IOptions<BleTransportOptions> Options(
        string deviceId = "AA:BB:CC:DD:EE:FF",
        int connectTimeoutMs = 10000,
        int writeTimeoutMs = 5000) =>
        Microsoft.Extensions.Options.Options.Create(new BleTransportOptions
        {
            DeviceId = deviceId,
            ConnectTimeoutMs = connectTimeoutMs,
            WriteTimeoutMs = writeTimeoutMs,
        });

    [TestMethod]
    public async Task OpenAsync_ConnectsAdapterFromFactory()
    {
        var adapter = new Mock<IBleAdapter>();
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options(deviceId: "some-device-id"));

        await transport.OpenAsync(TestContext.CancellationToken);

        factory.Verify(f => f.Create(It.Is<BleTransportOptions>(o => o.DeviceId == "some-device-id")), Times.Once);
        adapter.Verify(a => a.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(ConnectionState.Open, transport.State);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task OpenAsync_RaisesStateChangedThroughOpeningToOpen()
    {
        var adapter = new Mock<IBleAdapter>();
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options());
        var states = new List<ConnectionState>();
        transport.StateChanged += (_, e) => states.Add(e.Current);

        await transport.OpenAsync(TestContext.CancellationToken);

        Assert.AreSequenceEqual([ConnectionState.Opening, ConnectionState.Open], states);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task OpenAsync_WhenAdapterThrows_TransitionsToFaultedAndRethrows()
    {
        var adapter = new Mock<IBleAdapter>();
        adapter.Setup(a => a.ConnectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("no response from peripheral"));
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options());

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => transport.OpenAsync(TestContext.CancellationToken));
        Assert.AreEqual(ConnectionState.Faulted, transport.State);
        adapter.Verify(a => a.DisposeAsync(), Times.Once);
    }

    [TestMethod]
    public async Task WriteAsync_WhenNotOpen_Throws()
    {
        var transport = new BleTransport(Mock.Of<IBleAdapterFactory>(), Options());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.WriteAsync(new byte[] { 1 }, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task WriteAsync_WhenOpen_WritesBytesToAdapter()
    {
        var adapter = new Mock<IBleAdapter>();
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options());
        await transport.OpenAsync(TestContext.CancellationToken);

        var payload = new byte[] { 0x01, 0x02, 0x03 };
        await transport.WriteAsync(payload, TestContext.CancellationToken);

        adapter.Verify(a => a.WriteAsync(payload, It.IsAny<CancellationToken>()), Times.Once);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task NotificationArrivingDuringConnectAsync_IsNotDropped()
    {
        // Regression test for bug 052: some devices send a greeting the instant notifications are
        // enabled, which happens inside adapter.ConnectAsync — but _pipe was only created after
        // ConnectAsync returned, so OnNotificationReceived silently dropped a notification arriving
        // that early. See docs/bugs/fixed/052-ble-notifications-before-pipe.md.
        var adapter = new Mock<IBleAdapter>();
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        adapter.Setup(a => a.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                adapter.Raise(a => a.NotificationReceived += null, adapter.Object, new ReadOnlyMemory<byte>(payload));
                return Task.CompletedTask;
            });
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options());

        await transport.OpenAsync(TestContext.CancellationToken);

        var result = await transport.Input.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(payload, result.Buffer.ToArray());
        transport.Input.AdvanceTo(result.Buffer.End);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task IncomingNotification_IsAvailableThroughInput()
    {
        var adapter = new Mock<IBleAdapter>();
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options());
        await transport.OpenAsync(TestContext.CancellationToken);

        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        adapter.Raise(a => a.NotificationReceived += null, adapter.Object, new ReadOnlyMemory<byte>(payload));

        var result = await transport.Input.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreSequenceEqual(payload, result.Buffer.ToArray());
        transport.Input.AdvanceTo(result.Buffer.End);

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task AdapterDisconnects_TransportTransitionsToClosed()
    {
        var adapter = new Mock<IBleAdapter>();
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options());
        var closedTcs = new TaskCompletionSource();
        transport.StateChanged += (_, e) =>
        {
            if (e.Current == ConnectionState.Closed)
            {
                closedTcs.TrySetResult();
            }
        };

        await transport.OpenAsync(TestContext.CancellationToken);
        adapter.Raise(a => a.Disconnected += null, adapter.Object, EventArgs.Empty);

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_DisconnectsAndDisposesAdapter()
    {
        var adapter = new Mock<IBleAdapter>();
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options());
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.CloseAsync(TestContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        adapter.Verify(a => a.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        adapter.Verify(a => a.DisposeAsync(), Times.Once);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_WhenNeverOpened_DoesNothing()
    {
        var factory = new Mock<IBleAdapterFactory>();
        var transport = new BleTransport(factory.Object, Options());

        await transport.CloseAsync(TestContext.CancellationToken);

        factory.Verify(f => f.Create(It.IsAny<BleTransportOptions>()), Times.Never);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task OpenAsync_ConnectNeverCompletes_ThrowsTimeoutExceptionAfterConnectTimeoutMs()
    {
        var adapter = new Mock<IBleAdapter>();
        adapter.Setup(a => a.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => Task.Delay(Timeout.Infinite, ct));
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options(connectTimeoutMs: 50));

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => transport.OpenAsync(TestContext.CancellationToken));
        Assert.AreEqual(ConnectionState.Faulted, transport.State);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task WriteAsync_WriteNeverCompletes_ThrowsTimeoutExceptionAfterWriteTimeoutMs()
    {
        var adapter = new Mock<IBleAdapter>();
        adapter.Setup(a => a.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<ReadOnlyMemory<byte>, CancellationToken>((_, ct) => Task.Delay(Timeout.Infinite, ct));
        var factory = new Mock<IBleAdapterFactory>();
        factory.Setup(f => f.Create(It.IsAny<BleTransportOptions>())).Returns(adapter.Object);

        var transport = new BleTransport(factory.Object, Options(writeTimeoutMs: 50));
        await transport.OpenAsync(TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => transport.WriteAsync(new byte[] { 1 }, TestContext.CancellationToken));

        await transport.CloseAsync(TestContext.CancellationToken);
    }

    public required TestContext TestContext { get; set; }
}
