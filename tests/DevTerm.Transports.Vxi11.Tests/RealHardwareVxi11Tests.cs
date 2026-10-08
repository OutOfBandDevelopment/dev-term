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
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Dg1062z_AnswersIdentify()
    {
        // The unit takes a DHCP address (.87, then .127), so find it by LXI discovery instead of a fixed host.
        var found = await LxiDiscovery.ScanAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), TestContext.CancellationToken);
        var device = found.FirstOrDefault(d => d.Identity.Contains("DG1062Z", StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            Assert.Inconclusive("No DG1062Z answered LXI discovery.");
        }

        var host = device.Host;
        await using var transport = new Vxi11Transport(new SystemTcpConnectionSource(), Options.Create(new Vxi11TransportOptions { Host = host, AppendLineFeedAtEnd = false }));
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
