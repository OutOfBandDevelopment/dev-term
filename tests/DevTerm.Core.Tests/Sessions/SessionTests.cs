using System.Buffers;
using System.IO.Pipelines;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Core.Tests.Sessions;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class SessionTests
{
    /// <summary>
    /// A transport double backed by a real <see cref="Pipe"/>: the session's read loop reads
    /// from <see cref="Pipe.Reader"/> for real, and the test plays "incoming device bytes" by
    /// writing to <see cref="Pipe.Writer"/> — no need to mock <see cref="PipeReader"/> itself.
    /// </summary>
    private static (Mock<ITransport> Transport, Pipe Pipe) CreateOpenableTransport()
    {
        var pipe = new Pipe();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        return (transport, pipe);
    }

    [TestMethod]
    public async Task OpenAsync_DelegatesToTransport()
    {
        var (transport, _) = CreateOpenableTransport();
        await using var session = new Session(transport.Object, new Pipeline([]));

        await session.OpenAsync(TestContext.CancellationToken);

        transport.Verify(t => t.OpenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CloseAsync_DelegatesToTransport()
    {
        var transport = new Mock<ITransport>();
        await using var session = new Session(transport.Object, new Pipeline([]));

        await session.CloseAsync(TestContext.CancellationToken);

        transport.Verify(t => t.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SendAsync_DelegatesToTransport()
    {
        var transport = new Mock<ITransport>();
        await using var session = new Session(transport.Object, new Pipeline([]));
        var payload = new byte[] { 1, 2, 3 };

        await session.SendAsync(payload, TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(payload, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task IncomingBytes_RaiseOutputForEveryPresenter()
    {
        var (transport, pipe) = CreateOpenableTransport();

        var hex = new Mock<IPresenter>();
        hex.SetupGet(p => p.Name).Returns("hex");
        hex.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["2A"]);

        var ascii = new Mock<IPresenter>();
        ascii.SetupGet(p => p.Name).Returns("ascii");
        ascii.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["*"]);

        await using var session = new Session(transport.Object, new Pipeline([hex.Object, ascii.Object]));

        var received = new List<PresenterOutput>();
        var outputTcs = new TaskCompletionSource();
        session.Output += (_, output) =>
        {
            received.Add(output);
            if (received.Count == 2)
            {
                outputTcs.TrySetResult();
            }
        };

        await session.OpenAsync(TestContext.CancellationToken);
        await pipe.Writer.WriteAsync("*"u8.ToArray(), TestContext.CancellationToken);

        await outputTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.AreSequenceEqual(
            [new PresenterOutput("hex", "2A"), new PresenterOutput("ascii", "*")], received);
    }

    [TestMethod]
    public async Task RemoteCompletesInput_StopsTheReadLoopWithoutHanging()
    {
        var (transport, pipe) = CreateOpenableTransport();
        await using var session = new Session(transport.Object, new Pipeline([]));

        await session.OpenAsync(TestContext.CancellationToken);
        await pipe.Writer.CompleteAsync();

        // The read loop should observe completion and exit on its own; CloseAsync should not
        // hang waiting for a read loop that never stops.
        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        transport.Verify(t => t.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DisposeAsync_ClosesUnderlyingTransport()
    {
        var transport = new Mock<ITransport>();

        await using (new Session(transport.Object, new Pipeline([])))
        {
        }

        transport.Verify(t => t.DisposeAsync(), Times.Once);
    }

    [TestMethod]
    public async Task DisposeAsync_AfterOpen_StopsTheReadLoopWithoutHanging()
    {
        var (transport, _) = CreateOpenableTransport();
        var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        transport.Verify(t => t.DisposeAsync(), Times.Once);
    }

    [TestMethod]
    public async Task OpenAsync_AfterClose_RestartsTheReadLoopForRealIncomingData()
    {
        // Regression test: Session used to create its read-loop CancellationTokenSource once, in
        // the constructor, and only ever cancel it (never recreate it) - so re-opening after a
        // Close started the new read loop with an already-cancelled token, ending it immediately
        // and silently. A real scenario now that both front ends have a Connect/Disconnect menu
        // item, not just a hypothetical.
        var currentPipe = new Pipe();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(() => currentPipe.Reader);

        var presenter = new Mock<IPresenter>();
        presenter.SetupGet(p => p.Name).Returns("hex");
        presenter.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["2A"]);

        await using var session = new Session(transport.Object, new Pipeline([presenter.Object]));

        await session.OpenAsync(TestContext.CancellationToken);
        await session.CloseAsync(TestContext.CancellationToken);

        currentPipe = new Pipe();
        await session.OpenAsync(TestContext.CancellationToken);

        var received = new TaskCompletionSource();
        session.Output += (_, _) => received.TrySetResult();
        await currentPipe.Writer.WriteAsync("*"u8.ToArray(), TestContext.CancellationToken);

        await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task AddPresenter_WhileOpen_IsPickedUpByTheAlreadyRunningReadLoop()
    {
        // Regression test for the SCPI Measure/beep bug: a presenter resolved from the catalog
        // after the session was already built/opened (e.g. opening a device control panel) used to
        // never see incoming bytes, because Session's read loop holds one Pipeline reference for its
        // whole lifetime and nothing rebuilt it. AddPresenter must mutate that same live instance.
        var (transport, pipe) = CreateOpenableTransport();
        await using var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);

        var late = new Mock<IPresenter>();
        late.SetupGet(p => p.Name).Returns("scpi");
        late.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["reply"]);
        session.AddPresenter(late.Object);

        var received = new TaskCompletionSource<PresenterOutput>();
        session.Output += (_, output) => received.TrySetResult(output);
        await pipe.Writer.WriteAsync("*"u8.ToArray(), TestContext.CancellationToken);

        var output = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.AreEqual(new PresenterOutput("scpi", "reply"), output);
    }

    [TestMethod]
    public void State_ReflectsTransportState()
    {
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);

        var session = new Session(transport.Object, new Pipeline([]));

        Assert.AreEqual(ConnectionState.Open, session.State);
    }

    public required TestContext TestContext { get; set; }

    private static Task<SessionDisconnectedEventArgs> WaitForDisconnected(Session session)
    {
        var tcs = new TaskCompletionSource<SessionDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Disconnected += (_, e) => tcs.TrySetResult(e);
        return tcs.Task;
    }

    [TestMethod]
    public async Task ReadFailure_ClosesTheTransportAndRaisesDisconnectedWithTheError()
    {
        var (transport, pipe) = CreateOpenableTransport();
        await using var session = new Session(transport.Object, new Pipeline([]));
        var disconnected = WaitForDisconnected(session);
        await session.OpenAsync(TestContext.CancellationToken);

        // What StreamToPipePump does when the underlying stream throws (an unplugged cable, a reset socket).
        await pipe.Writer.CompleteAsync(new IOException("cable unplugged"));

        var e = await disconnected.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsInstanceOfType<IOException>(e.Error);
        StringAssert.Contains(e.Message, "cable unplugged");
        transport.Verify(t => t.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PeerClosesTheConnection_RaisesDisconnectedWithNoError()
    {
        var (transport, pipe) = CreateOpenableTransport();
        await using var session = new Session(transport.Object, new Pipeline([]));
        var disconnected = WaitForDisconnected(session);
        await session.OpenAsync(TestContext.CancellationToken);

        await pipe.Writer.CompleteAsync();

        var e = await disconnected.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsNull(e.Error);
        transport.Verify(t => t.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PresenterThrows_TreatedAsAFaultNotAnUnobservedCrash()
    {
        var (transport, pipe) = CreateOpenableTransport();
        var broken = new Mock<IPresenter>();
        broken.SetupGet(p => p.Name).Returns("broken");
        broken.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Throws(new FormatException("bad frame"));
        await using var session = new Session(transport.Object, new Pipeline([broken.Object]));
        var disconnected = WaitForDisconnected(session);
        await session.OpenAsync(TestContext.CancellationToken);

        await pipe.Writer.WriteAsync("x"u8.ToArray(), TestContext.CancellationToken);

        var e = await disconnected.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsInstanceOfType<FormatException>(e.Error);
    }

    [TestMethod]
    public async Task SendFailure_ClosesRaisesDisconnectedAndRethrows()
    {
        var (transport, _) = CreateOpenableTransport();
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        transport.Setup(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("device did not answer"));
        await using var session = new Session(transport.Object, new Pipeline([]));
        var disconnected = WaitForDisconnected(session);
        await session.OpenAsync(TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => session.SendAsync(new byte[] { 1 }, TestContext.CancellationToken));

        var e = await disconnected.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsInstanceOfType<TimeoutException>(e.Error);
        transport.Verify(t => t.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CallerClose_NeverRaisesDisconnected()
    {
        var (transport, _) = CreateOpenableTransport();
        await using var session = new Session(transport.Object, new Pipeline([]));
        var raised = false;
        session.Disconnected += (_, _) => raised = true;
        await session.OpenAsync(TestContext.CancellationToken);

        await session.CloseAsync(TestContext.CancellationToken);
        await Task.Delay(100, TestContext.CancellationToken);

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public async Task CloseAsync_TransportCloseThrows_DoesNotThrow()
    {
        var (transport, _) = CreateOpenableTransport();
        transport.Setup(t => t.CloseAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("already gone"));
        await using var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task OpenAsync_CalledAgainWhileAlreadyOpen_DoesNotStartASecondReadLoop()
    {
        // Regression test for bug 001: OpenAsync had no guard for an already-open session, so a
        // second call (e.g. a slow connect racing a second Connect click) started a second
        // PumpAsync on the same PipeReader. Two concurrent reads on one PipeReader throw
        // "Reading is already in progress", which faults the healthy connection and reports a
        // bogus disconnect - see docs/bugs/fixed/001-session-double-open.md.
        var (transport, pipe) = CreateOpenableTransport();
        var presenter = new Mock<IPresenter>();
        presenter.SetupGet(p => p.Name).Returns("p");
        presenter.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["got it"]);
        await using var session = new Session(transport.Object, new Pipeline([presenter.Object]));
        var disconnected = WaitForDisconnected(session);

        await session.OpenAsync(TestContext.CancellationToken);
        await session.OpenAsync(TestContext.CancellationToken);

        var received = new TaskCompletionSource();
        session.Output += (_, _) => received.TrySetResult();
        await pipe.Writer.WriteAsync("*"u8.ToArray(), TestContext.CancellationToken);

        var faultedEarly = await Task.WhenAny(disconnected, received.Task)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken) == disconnected;

        Assert.IsFalse(faultedEarly, "the second OpenAsync call should not fault the connection");
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task AfterAFault_OpenAsyncReconnectsAndReadsAgain()
    {
        var firstPipe = new Pipe();
        var secondPipe = new Pipe();
        var transport = new Mock<ITransport>();
        transport.SetupSequence(t => t.Input).Returns(firstPipe.Reader).Returns(secondPipe.Reader);

        var presenter = new Mock<IPresenter>();
        presenter.SetupGet(p => p.Name).Returns("p");
        presenter.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["got it"]);

        await using var session = new Session(transport.Object, new Pipeline([presenter.Object]));
        var disconnected = WaitForDisconnected(session);
        await session.OpenAsync(TestContext.CancellationToken);
        await firstPipe.Writer.CompleteAsync(new IOException("dropped"));
        await disconnected.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        var output = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Output += (_, o) => output.TrySetResult(o.Text);
        await session.OpenAsync(TestContext.CancellationToken);
        await secondPipe.Writer.WriteAsync("x"u8.ToArray(), TestContext.CancellationToken);

        Assert.AreEqual("got it", await output.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
    }
}
