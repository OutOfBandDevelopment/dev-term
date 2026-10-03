using System.Net.Sockets;
using System.Text;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Vxi11.Tests;

/// <summary>Identifies the bench Rigol DG1062Z over VXI-11; Inconclusive when it is not reachable.</summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Vxi11)]
[TestCategory(TestCategories.Hardware)]
[TestClass]
public sealed class RealHardwareVxi11Tests
{
    private const string _host = "192.168.0.87";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Dg1062z_AnswersIdentify()
    {
        using (var probe = new TcpClient())
        {
            try
            {
                using var probeTimeout = new CancellationTokenSource(1500);
                await probe.ConnectAsync(_host, 111, probeTimeout.Token);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                Assert.Inconclusive($"No VXI-11 instrument answered on {_host}:111.");
            }
        }

        await using var transport = new Vxi11Transport(new SystemTcpConnectionSource(), Options.Create(new Vxi11TransportOptions { Host = _host, AppendLineFeedAtEnd = false }));
        await transport.OpenAsync(TestContext.CancellationToken);
        await transport.WriteAsync("*IDN?\n"u8.ToArray(), TestContext.CancellationToken);

        var received = new StringBuilder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!received.ToString().Contains('\n'))
        {
            var result = await transport.Input.ReadAsync(timeout.Token);
            received.Append(Encoding.ASCII.GetString(result.Buffer));
            transport.Input.AdvanceTo(result.Buffer.End);
        }

        TestContext.WriteLine(received.ToString());
        StringAssert.Contains(received.ToString(), "DG1062Z");
    }
}
