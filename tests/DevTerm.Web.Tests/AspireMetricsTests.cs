using System.Net.Sockets;
using DevTerm.Test.Utilities;
using Microsoft.Playwright;

namespace DevTerm.Web.Tests;

/// <summary>
/// Reads the Aspire dashboard's Metrics page for the <c>devterm</c> resource in a real browser (its Blazor circuit needs one).
/// Needs <c>docker compose up -d aspire-dashboard</c> in <c>containers/</c> and at least one <c>--otlp</c> run; skips otherwise.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Web)]
public sealed class AspireMetricsTests
{
    [TestMethod]
    public async Task MetricsPage_ListsTheDevTermInstruments()
    {
        using (var probe = new TcpClient())
        {
            try
            {
                await probe.ConnectAsync("127.0.0.1", 18888);
            }
            catch (SocketException)
            {
                Assert.Inconclusive("The Aspire dashboard is not running on 127.0.0.1:18888.");
            }
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync("http://127.0.0.1:18888/metrics/resource/devterm");
        await page.WaitForTimeoutAsync(5000);
        var text = await page.InnerTextAsync("body");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "aspire-metrics.txt"), text);
        StringAssert.Contains(text, "devterm.bytes.sent");
    }
}
