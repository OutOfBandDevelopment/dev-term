using System.Diagnostics;
using System.IO.Pipelines;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Core.Tests.Sessions;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class SessionLimitsTests
{
    public TestContext TestContext { get; set; } = null!;

    private static Mock<ITransport> OpenableTransport()
    {
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(new Pipe().Reader);
        return transport;
    }

    [TestMethod]
    public async Task SendAsync_WithMinInterval_SpacesConsecutiveSends()
    {
        var transport = OpenableTransport();
        await using var session = new Session(transport.Object, new Pipeline([]))
        {
            Limits = new SessionLimits { MinSendIntervalMs = 100 },
        };

        var clock = Stopwatch.StartNew();
        await session.SendAsync(new byte[] { 1 }, TestContext.CancellationToken);
        await session.SendAsync(new byte[] { 2 }, TestContext.CancellationToken);

        Assert.IsGreaterThanOrEqualTo(90, clock.ElapsedMilliseconds);
    }

    [TestMethod]
    public async Task OpenAsync_RetriesAfterFailure_ThenSucceeds()
    {
        var transport = OpenableTransport();
        var calls = 0;
        transport.Setup(t => t.OpenAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++calls < 3 ? Task.FromException(new IOException("down")) : Task.CompletedTask);
        await using var session = new Session(transport.Object, new Pipeline([]))
        {
            Limits = new SessionLimits { ConnectRetries = 2, ConnectRetryDelayMs = 1 },
        };

        await session.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    public async Task OpenAsync_ExhaustsRetries_ThrowsLastError()
    {
        var transport = OpenableTransport();
        transport.Setup(t => t.OpenAsync(It.IsAny<CancellationToken>())).Returns(Task.FromException(new IOException("down")));
        await using var session = new Session(transport.Object, new Pipeline([]))
        {
            Limits = new SessionLimits { ConnectRetries = 1, ConnectRetryDelayMs = 1 },
        };

        await Assert.ThrowsExactlyAsync<IOException>(() => session.OpenAsync(TestContext.CancellationToken));
        transport.Verify(t => t.OpenAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task OpenAsync_ConnectTimeout_ThrowsTimeoutException()
    {
        var transport = OpenableTransport();
        transport.Setup(t => t.OpenAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct => await Task.Delay(Timeout.Infinite, ct));
        await using var session = new Session(transport.Object, new Pipeline([]))
        {
            Limits = new SessionLimits { ConnectTimeoutMs = 50 },
        };

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => session.OpenAsync(TestContext.CancellationToken));
    }
}
