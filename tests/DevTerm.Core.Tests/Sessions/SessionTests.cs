using System.Buffers;
using System.IO.Pipelines;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Moq;

namespace DevTerm.Core.Tests.Sessions;

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

        await session.OpenAsync();

        transport.Verify(t => t.OpenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CloseAsync_DelegatesToTransport()
    {
        var transport = new Mock<ITransport>();
        await using var session = new Session(transport.Object, new Pipeline([]));

        await session.CloseAsync();

        transport.Verify(t => t.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SendAsync_DelegatesToTransport()
    {
        var transport = new Mock<ITransport>();
        await using var session = new Session(transport.Object, new Pipeline([]));
        var payload = new byte[] { 1, 2, 3 };

        await session.SendAsync(payload);

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

        await session.OpenAsync();
        await pipe.Writer.WriteAsync(new byte[] { 0x2A });

        await outputTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        CollectionAssert.AreEqual(
            new[] { new PresenterOutput("hex", "2A"), new PresenterOutput("ascii", "*") },
            received);
    }

    [TestMethod]
    public async Task RemoteCompletesInput_StopsTheReadLoopWithoutHanging()
    {
        var (transport, pipe) = CreateOpenableTransport();
        await using var session = new Session(transport.Object, new Pipeline([]));

        await session.OpenAsync();
        await pipe.Writer.CompleteAsync();

        // The read loop should observe completion and exit on its own; CloseAsync should not
        // hang waiting for a read loop that never stops.
        await session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));

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
        await session.OpenAsync();

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        transport.Verify(t => t.DisposeAsync(), Times.Once);
    }

    [TestMethod]
    public void State_ReflectsTransportState()
    {
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);

        var session = new Session(transport.Object, new Pipeline([]));

        Assert.AreEqual(ConnectionState.Open, session.State);
    }
}
