using System.Net;
using System.Net.Sockets;
using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Tcp.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Tcp)]
[TestClass]
public sealed class SystemTcpConnectionSourceTests
{
    private readonly SystemTcpConnectionSource _source = new();

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task AcceptAsync_WithAHostname_ResolvesItInsteadOfThrowingFormatException()
    {
        // Regression test for bug 056: AcceptAsync used to call IPAddress.Parse(options.Host)
        // directly on the raw host string, which throws FormatException for a non-IP-literal host
        // like "localhost" - `--listen true --host localhost` never got as far as listening. See
        // docs/bugs/fixed/056-tcp-listen-rejects-hostnames.md.
        var port = GetFreePort();
        var options = new TcpTransportOptions { Mode = TcpTransportMode.Listener, Host = "localhost", Port = port };
        var acceptTask = _source.AcceptAsync(options, TestContext.CancellationToken);

        // "localhost" can resolve to an IPv4 or an IPv6 loopback address first depending on the
        // machine's resolver order, and AcceptAsync binds only whichever one it resolves to (see
        // the type's remarks) - connecting by the same hostname, not a hardcoded literal, lets the
        // client try both families instead of coupling this test to that order.
        using var client = new TcpClient();
        await client.ConnectAsync("localhost", port, TestContext.CancellationToken);

        using var connection = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsNotNull(connection);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task AcceptAsync_WithNoHost_AcceptsAnIPv6LoopbackPeer()
    {
        // Regression test for bug 056: AcceptAsync used to bind IPAddress.Any unconditionally,
        // which is IPv4-only - an IPv6 peer could never connect in listen mode with no host set.
        // See docs/bugs/fixed/056-tcp-listen-rejects-hostnames.md.
        var port = GetFreePort();
        var options = new TcpTransportOptions { Mode = TcpTransportMode.Listener, Host = null, Port = port };
        var acceptTask = _source.AcceptAsync(options, TestContext.CancellationToken);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.IPv6Loopback, port, TestContext.CancellationToken);

        using var connection = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsNotNull(connection);
    }

    [TestMethod]
    public async Task AcceptAsync_WithNoHost_StillAcceptsAnIPv4LoopbackPeer()
    {
        var port = GetFreePort();
        var options = new TcpTransportOptions { Mode = TcpTransportMode.Listener, Host = null, Port = port };
        var acceptTask = _source.AcceptAsync(options, TestContext.CancellationToken);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, TestContext.CancellationToken);

        using var connection = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsNotNull(connection);
    }

    [TestMethod]
    public async Task AcceptAsync_WithAnIpLiteralHost_StillBindsToThatAddress()
    {
        var port = GetFreePort();
        var options = new TcpTransportOptions { Mode = TcpTransportMode.Listener, Host = "127.0.0.1", Port = port };
        var acceptTask = _source.AcceptAsync(options, TestContext.CancellationToken);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, TestContext.CancellationToken);

        using var connection = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsNotNull(connection);
    }

    public required TestContext TestContext { get; set; }
}
