using System.Buffers;
using System.IO.Pipelines;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Moq;

namespace DevTerm.Core.Tests.Sessions;

[TestCategory("UNIT")]
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
        await pipe.Writer.WriteAsync(new byte[] { 0x2A }, TestContext.CancellationToken);

        await outputTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.AreSequenceEqual(
            new[] { new PresenterOutput("hex", "2A"), new PresenterOutput("ascii", "*") }, received);
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
        await currentPipe.Writer.WriteAsync(new byte[] { 0x2A }, TestContext.CancellationToken);

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
        await pipe.Writer.WriteAsync(new byte[] { 0x2A }, TestContext.CancellationToken);

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

    public TestContext TestContext { get; set; }
}
