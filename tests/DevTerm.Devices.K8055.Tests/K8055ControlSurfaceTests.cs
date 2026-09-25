using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Moq;

namespace DevTerm.Devices.K8055.Tests;

/// <summary>
/// Verifies the exact bytes <see cref="K8055ControlSurface"/> sends for each command, against a
/// mocked <see cref="ITransport"/> behind a real <see cref="Session"/> — no real HID device
/// involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class K8055ControlSurfaceTests
{
    private static (Session Session, Mock<ITransport> Transport) CreateSurfaceSession()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        return (session, transport);
    }

    [TestMethod]
    public async Task InvokeAsync_TogglingDigitalOut1On_SendsSetOutputsFrameWithBit0Set()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await surface.InvokeAsync("digitalOut1", "1", TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x05, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_TogglingDigitalOut8On_SetsBit7()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await surface.InvokeAsync("digitalOut8", "1", TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x05, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_TwoDigitalOutMutations_SendsCombinedStateOnTheSecond()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await surface.InvokeAsync("digitalOut1", "1", TestContext.CancellationToken);
        await surface.InvokeAsync("digitalOut3", "1", TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x05, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_TogglingDigitalOutOff_ClearsItsBit()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await surface.InvokeAsync("digitalOut1", "1", TestContext.CancellationToken);
        await surface.InvokeAsync("digitalOut1", "0", TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_SetAnalogOut1_SendsValueInFrame()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await surface.InvokeAsync("analogOut1", "128", TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x05, 0x00, 128, 0x00, 0x00, 0x00, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_SetAnalogOut2_PreservesPriorAnalogOut1()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await surface.InvokeAsync("analogOut1", "128", TestContext.CancellationToken);
        await surface.InvokeAsync("analogOut2", "64", TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x05, 0x00, 128, 64, 0x00, 0x00, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_ResetCounter1_SendsFixedFrame()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await surface.InvokeAsync("resetCounter1", null, TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_ResetCounter2_SendsFixedFrame()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await surface.InvokeAsync("resetCounter2", null, TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownCommand_Throws()
    {
        var (session, _) = CreateSurfaceSession();
        var surface = new K8055ControlSurface(session);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => surface.InvokeAsync("notARealCommand", null, TestContext.CancellationToken));
    }

    public TestContext TestContext { get; set; }
}
