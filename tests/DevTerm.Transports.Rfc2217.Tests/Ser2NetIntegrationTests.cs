using System.Net.Sockets;
using System.Text;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Rfc2217.Tests;

/// <summary>Round trip through the real ser2net RFC 2217 server in containers/ (its virtual port echoes).</summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Rfc2217)]
[TestClass]
public sealed class Ser2NetIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task SentBytes_ComeBackFromTheServersEchoPort()
    {
        using (var probe = new TcpClient())
        {
            try
            {
                await probe.ConnectAsync("127.0.0.1", 2217, TestContext.CancellationToken);
            }
            catch (SocketException)
            {
                Assert.Inconclusive("No ser2net on 127.0.0.1:2217; start containers/docker-compose.yml.");
            }
        }

        var options = new Rfc2217TransportOptions { Host = "127.0.0.1", Port = 2217, BaudRate = 9600 };
        await using var transport = new Rfc2217Transport(new SystemTcpConnectionSource(), Options.Create(options));
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync("ping\n"u8.ToArray(), TestContext.CancellationToken);

        var received = new StringBuilder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(5000);
        while (!received.ToString().Contains("ping", StringComparison.Ordinal))
        {
            var result = await transport.Input.ReadAsync(timeout.Token);
            received.Append(Encoding.UTF8.GetString(result.Buffer));
            transport.Input.AdvanceTo(result.Buffer.End);
        }

        Assert.AreEqual(ConnectionState.Open, transport.State);
        Assert.IsTrue(transport.ComPortControlNegotiated);
    }
}
