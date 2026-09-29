using System.IO.Pipelines;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Devices.ZoomH4n.Tests;

/// <summary>
/// Verifies <see cref="ZoomH4nControlSurface"/>'s init handshake and per-button press/release frames
/// against a real <see cref="Session"/> whose <see cref="ITransport"/> is mocked (a <see cref="Pipe"/>
/// backs <see cref="ITransport.Input"/> directly, so a reply to the handshake's probe byte can be
/// written back synchronously from within the mocked <c>WriteAsync</c> — no real serial hardware
/// involved, so this is <c>UNIT</c>), per docs/design/proposals/zoom-h4n-remote-protocol.md.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Serial)]
[TestCategory(TestCategories.Zoom_H4n)]
[TestClass]
public sealed class ZoomH4nControlSurfaceTests
{
    public required TestContext TestContext { get; set; }

    // A session whose device answers the handshake's first 0x00 probe with a high-bit byte
    // immediately (written directly into the pipe from inside the mocked WriteAsync, so there's no
    // real-time race against the surface's ~30ms per-attempt wait).
    private static async Task<(Session Session, List<byte[]> Writes)> OpenRespondingSessionAsync(CancellationToken cancellationToken)
    {
        var pipe = new Pipe();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);

        var writes = new List<byte[]>();
        var handshakeAnswered = false;
        transport.Setup(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ReadOnlyMemory<byte> data, CancellationToken _) =>
            {
                var bytes = data.ToArray();
                writes.Add(bytes);
                if (!handshakeAnswered && bytes is [0x00])
                {
                    handshakeAnswered = true;
                    await pipe.Writer.WriteAsync(new byte[] { 0x81 }, CancellationToken.None);
                }
            });

        var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(cancellationToken);
        return (session, writes);
    }

    [TestMethod]
    public async Task InvokeAsync_FirstCommand_RunsHandshakeThenSendsPressAndRelease()
    {
        var (session, writes) = await OpenRespondingSessionAsync(TestContext.CancellationToken);
        await using var _ = session;
        var surface = new ZoomH4nControlSurface(session);

        await surface.InvokeAsync("record", null, TestContext.CancellationToken);

        Assert.HasCount(4, writes);
        CollectionAssert.AreEqual(new byte[] { 0x00 }, writes[0]);
        CollectionAssert.AreEqual(new byte[] { 0xA1, 0x80, 0x00 }, writes[1]);
        CollectionAssert.AreEqual(new byte[] { 0x81, 0x00 }, writes[2]);
        CollectionAssert.AreEqual(new byte[] { 0x80, 0x00 }, writes[3]);
    }

    [TestMethod]
    public async Task InvokeAsync_SecondCommand_DoesNotRepeatTheHandshake()
    {
        var (session, writes) = await OpenRespondingSessionAsync(TestContext.CancellationToken);
        await using var _ = session;
        var surface = new ZoomH4nControlSurface(session);

        await surface.InvokeAsync("record", null, TestContext.CancellationToken);
        writes.Clear();
        await surface.InvokeAsync("play", null, TestContext.CancellationToken);

        Assert.HasCount(2, writes);
        CollectionAssert.AreEqual(new byte[] { 0x82, 0x00 }, writes[0]);
        CollectionAssert.AreEqual(new byte[] { 0x80, 0x00 }, writes[1]);
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownCommand_ThrowsWithoutSendingAnything()
    {
        var (session, writes) = await OpenRespondingSessionAsync(TestContext.CancellationToken);
        await using var _ = session;
        var surface = new ZoomH4nControlSurface(session);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => surface.InvokeAsync("notARealCommand", null, TestContext.CancellationToken));

        Assert.IsEmpty(writes);
    }

    [TestMethod]
    public void PreviewCommand_KnownButton_ShowsPressThenReleaseHex_WithNoSideEffect()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        var surface = new ZoomH4nControlSurface(session);

        var preview = surface.PreviewCommand("volUp", null);

        Assert.AreEqual("80 08 80 00", preview);
        transport.Verify(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void PreviewCommand_UnknownCommand_ReturnsNull()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        var surface = new ZoomH4nControlSurface(session);

        Assert.IsNull(surface.PreviewCommand("notARealCommand", null));
    }

    // Regression tests for bug 020: the surface binds its wake watcher into the session's live
    // pipeline (see the class doc comment) but had no way to unbind it again, so every panel open
    // left another watcher scanning every received byte for the rest of the session. See
    // docs/bugs/020-zoomh4n-wake-watcher-leak.md.

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Dispose_RemovesTheWakeWatcherFromTheSessionPipeline()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        var surface = new ZoomH4nControlSurface(session);

        Assert.HasCount(1, session.Presenters);

        surface.Dispose();

        Assert.IsEmpty(session.Presenters);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void OpeningAndClosingThePanelTwice_LeavesNoWatcherInThePipeline()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));

        using (new ZoomH4nControlSurface(session))
        {
        }

        using (new ZoomH4nControlSurface(session))
        {
        }

        Assert.IsEmpty(session.Presenters);
    }
}
