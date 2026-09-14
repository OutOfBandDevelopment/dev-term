using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Moq;

namespace DevTerm.Core.Tests.Sessions;

[TestClass]
public sealed class SessionTests
{
    [TestMethod]
    public async Task OpenAsync_DelegatesToTransport()
    {
        var transport = new Mock<ITransport>();
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
    public async Task TransportDataReceived_RaisesOutputForEveryPresenter()
    {
        var transport = new Mock<ITransport>();

        var hex = new Mock<IPresenter>();
        hex.SetupGet(p => p.Name).Returns("hex");
        hex.Setup(p => p.Render(It.IsAny<ReadOnlyMemory<byte>>())).Returns("2A");

        var ascii = new Mock<IPresenter>();
        ascii.SetupGet(p => p.Name).Returns("ascii");
        ascii.Setup(p => p.Render(It.IsAny<ReadOnlyMemory<byte>>())).Returns("*");

        await using var session = new Session(transport.Object, new Pipeline([hex.Object, ascii.Object]));

        var received = new List<PresenterOutput>();
        session.Output += (_, output) => received.Add(output);

        transport.Raise(t => t.DataReceived += null, transport.Object, new TransportDataReceivedEventArgs(new byte[] { 0x2A }));

        CollectionAssert.AreEqual(
            new[] { new PresenterOutput("hex", "2A"), new PresenterOutput("ascii", "*") },
            received);
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
    public void State_ReflectsTransportState()
    {
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);

        var session = new Session(transport.Object, new Pipeline([]));

        Assert.AreEqual(ConnectionState.Open, session.State);
    }
}
