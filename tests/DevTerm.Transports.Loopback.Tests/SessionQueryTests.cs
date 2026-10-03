using System.Buffers;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Loopback.Tests;

/// <summary>A request that gets no reply raises a timeout instead of waiting forever.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Loopback)]
[TestClass]
public sealed class SessionQueryTests
{
    private sealed class ChunkPresenter : IPresenter
    {
        public string Name => "chunk";

        public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) => [Encoding.ASCII.GetString(data.ToArray())];
    }

    public required TestContext TestContext { get; set; }

    private async Task<Session> OpenAsync(int responseTimeoutMs)
    {
        var session = new Session(new LoopbackTransport(Options.Create(new LoopbackTransportOptions())), new Pipeline([new ChunkPresenter()]))
        {
            Limits = new SessionLimits { ResponseTimeoutMs = responseTimeoutMs },
        };
        await session.OpenAsync(TestContext.CancellationToken);
        return session;
    }

    [TestMethod]
    public async Task Query_ReturnsTheReply()
    {
        await using var session = await OpenAsync(5000);

        var reply = await session.QueryAsync("hello\r\n"u8.ToArray(), TestContext.CancellationToken);

        StringAssert.Contains(reply.Text, "From Loopback test");
    }

    [TestMethod]
    public async Task Query_WithNoReply_ThrowsTimeoutException()
    {
        await using var session = await OpenAsync(300);

        await Assert.ThrowsExactlyAsync<TimeoutException>(async () =>
            await session.QueryAsync("\r\n"u8.ToArray(), TestContext.CancellationToken));
    }
}
