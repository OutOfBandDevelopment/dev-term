using System.Text;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Vxi11.Tests;

/// <summary>Drives the transport against a real loopback ONC-RPC server (no hardware).</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Vxi11)]
[TestClass]
public sealed class Vxi11TransportTests
{
    public TestContext TestContext { get; set; } = null!;

    private static async Task<string> ReadLineAsync(Vxi11Transport transport, CancellationToken cancellationToken)
    {
        var received = new StringBuilder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!received.ToString().Contains('\n'))
        {
            var result = await transport.Input.ReadAsync(timeout.Token);
            received.Append(Encoding.ASCII.GetString(result.Buffer));
            transport.Input.AdvanceTo(result.Buffer.End);
        }

        return received.ToString();
    }

    private static Vxi11Transport Create(FakeVxi11Server server, bool useFixedPort) =>
        new(new RedirectingConnectionSource(server), Options.Create(new Vxi11TransportOptions
        {
            Host = "127.0.0.1",
            Port = useFixedPort ? server.CorePort : 0,
        }));

    [TestMethod]
    public async Task Identify_RoundTrip_ThroughThePortmapper()
    {
        using var server = new FakeVxi11Server();
        await using var transport = Create(server, useFixedPort: false);

        await transport.OpenAsync(TestContext.CancellationToken);
        await transport.WriteAsync("*IDN?\n"u8.ToArray(), TestContext.CancellationToken);

        Assert.AreEqual("FAKE,MODEL,1,2.0\n", await ReadLineAsync(transport, TestContext.CancellationToken));
        Assert.AreEqual("inst0", server.DeviceName);
        Assert.AreEqual("*IDN?\n", server.Written[0]);
    }

    [TestMethod]
    public async Task FixedPort_SkipsThePortmapper()
    {
        using var server = new FakeVxi11Server();
        await using var transport = Create(server, useFixedPort: true);

        await transport.OpenAsync(TestContext.CancellationToken);
        await transport.WriteAsync("hello\n"u8.ToArray(), TestContext.CancellationToken);

        Assert.AreEqual("hello\n", await ReadLineAsync(transport, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ReplyEndedWithoutLineFeed_GetsOneAppended()
    {
        using var server = new FakeVxi11Server(endsWithoutLineFeed: true);
        await using var transport = Create(server, useFixedPort: true);

        await transport.OpenAsync(TestContext.CancellationToken);
        await transport.WriteAsync("abc\n"u8.ToArray(), TestContext.CancellationToken);

        Assert.AreEqual("abc\n", await ReadLineAsync(transport, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task CloseAsync_DestroysTheLinkAndCloses()
    {
        using var server = new FakeVxi11Server();
        var transport = Create(server, useFixedPort: true);
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.CloseAsync(TestContext.CancellationToken);

        Assert.IsTrue(server.LinkDestroyed);
        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    public async Task OpenAsync_NothingListening_FaultsAndThrows()
    {
        var server = new FakeVxi11Server();
        server.Dispose();
        var transport = new Vxi11Transport(
            new RedirectingConnectionSource(server),
            Options.Create(new Vxi11TransportOptions { Host = "127.0.0.1", WriteTimeoutMs = 1000 }));

        await Assert.ThrowsAsync<Exception>(() => transport.OpenAsync(TestContext.CancellationToken));

        Assert.AreEqual(ConnectionState.Faulted, transport.State);
    }

    [TestMethod]
    public async Task Write_WhenNotOpen_Throws()
    {
        using var server = new FakeVxi11Server();
        var transport = Create(server, useFixedPort: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.WriteAsync("x"u8.ToArray(), TestContext.CancellationToken));
    }

    /// <summary>Sends the portmapper's well-known port 111 to the fake's ephemeral one; everything else goes where asked.</summary>
    private sealed class RedirectingConnectionSource(FakeVxi11Server server) : ITcpConnectionSource
    {
        public Task<ITcpConnection> AcceptAsync(TcpTransportOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ITcpConnection> ConnectAsync(TcpTransportOptions options, CancellationToken cancellationToken)
        {
            var redirected = new TcpTransportOptions
            {
                Mode = TcpTransportMode.Client,
                Host = options.Host,
                Port = options.Port == 111 ? server.PortmapperPort : options.Port,
                WriteTimeoutMs = options.WriteTimeoutMs,
            };
            return new SystemTcpConnectionSource().ConnectAsync(redirected, cancellationToken);
        }
    }
}
