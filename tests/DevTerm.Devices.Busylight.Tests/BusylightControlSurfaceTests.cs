using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Moq;

namespace DevTerm.Devices.Busylight.Tests;

/// <summary>
/// Verifies the exact bytes <see cref="BusylightControlSurface"/> sends on "apply", against a
/// mocked <see cref="ITransport"/> behind a real <see cref="Session"/> — no real HID device
/// involved, so this is <c>UNIT</c>. Every other command only mutates internal state, so these
/// tests set state then invoke "apply" to observe the resulting frame, per
/// docs/design/features/kuando-busylight-protocol.md's confirmed single-command shape.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class BusylightControlSurfaceTests
{
    private static (Session Session, Mock<ITransport> Transport) CreateSurfaceSession()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        return (session, transport);
    }

    [TestMethod]
    public async Task InvokeAsync_SetColorRedThenApply_SendsRedFrameWithDefaultTimingAndAudio()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await surface.InvokeAsync("color", "Red");
        await surface.InvokeAsync("apply", null);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x01, 0x00, 0x80 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_SetColorBlue_UsesCorrectRgbByteOrder()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await surface.InvokeAsync("color", "Blue");
        await surface.InvokeAsync("apply", null);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x01, 0x00, 0x80 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_BlinkModeSlow_SetsOnOffPreset()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await surface.InvokeAsync("blinkMode", "Slow");
        await surface.InvokeAsync("apply", null);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x50, 0x50, 0x80 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_ExplicitOnOffBytes_OverrideDefaults()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await surface.InvokeAsync("onMs", "10");
        await surface.InvokeAsync("offMs", "20");
        await surface.InvokeAsync("apply", null);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 10, 20, 0x80 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_Mute_ClearsThePlayBitInTheAudioByte()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await surface.InvokeAsync("mute", "1");
        await surface.InvokeAsync("apply", null);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00 })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_TrackAndVolume_PackIntoTheAudioByte()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await surface.InvokeAsync("track", "Nordic");
        await surface.InvokeAsync("volume", "5");
        await surface.InvokeAsync("apply", null);

        // Play bit set (not muted) | track index 1 (Nordic) << 3 | volume 5 = 0x80 | 0x08 | 0x05.
        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x8D })),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_CustomColor_IsANoOp()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await surface.InvokeAsync("customColor", null);

        transport.Verify(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task InvokeAsync_ProgramSequence_IsANoOp()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await surface.InvokeAsync("programSequence", null);

        transport.Verify(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownCommand_Throws()
    {
        var (session, _) = CreateSurfaceSession();
        var surface = new BusylightControlSurface(session);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => surface.InvokeAsync("notARealCommand", null));
    }
}
