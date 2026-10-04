using System.Net;
using System.Net.Sockets;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Microsoft.Playwright;

namespace DevTerm.Web.Tests;

/// <summary>The shared user flows, driven through the real web page in headless Edge (Playwright).</summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Web)]
public sealed class WebUserFlowTests : UserFlowTestsBase
{
    protected override async Task RunAsync(Func<IFrontEndDriver, Task> flow)
    {
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "demo-token", Panel = "demo" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            using var playwright = await Playwright.CreateAsync();
            IBrowser browser;
            try
            {
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true });
            }
            catch (PlaywrightException ex)
            {
                Assert.Inconclusive("Microsoft Edge could not be launched by Playwright: " + ex.Message);
                return;
            }

            await using (browser)
            {
                var page = await browser.NewPageAsync();
                await page.GotoAsync($"http://127.0.0.1:{port}/?token=demo-token");
                await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("connected");
                await flow(new Driver(browser, page, $"http://127.0.0.1:{port}"));
            }

            await built.App.StopAsync();
        }
    }

    private sealed class Driver(IBrowser browser, IPage page, string baseUrl) : IFrontEndDriver
    {
        public string Name => "Web";

        public bool CanDisconnect => false;

        public async Task SendAsync(string line)
        {
            await page.Locator("#line").FillAsync(line);
            await page.Locator("#line").PressAsync("Enter");
        }

        public async Task<string> OutputAsync() => await page.Locator("#out").TextContentAsync() ?? string.Empty;

        public async Task<bool> WaitForOutputAsync(string text, TimeSpan timeout)
        {
            try
            {
                await Assertions.Expect(page.Locator("#out")).ToContainTextAsync(text, new LocatorAssertionsToContainTextOptions { Timeout = (float)timeout.TotalMilliseconds });
                return true;
            }
            catch (PlaywrightException)
            {
                return false;
            }
        }

        public async Task<bool> IsConnectedAsync()
        {
            var status = await page.Locator("#status").TextContentAsync() ?? string.Empty;
            return status.Contains("connected", StringComparison.OrdinalIgnoreCase) && !status.Contains("disconnected", StringComparison.OrdinalIgnoreCase);
        }

        public Task DisconnectAsync() => throw new NotSupportedException();

        public Task ReconnectAsync() => throw new NotSupportedException();

        public bool CanSwitchProfile => false;

        public Task SwitchToLoopbackProfileAsync() => throw new NotSupportedException();

        public bool CanUsePanel => true;

        public async Task ApplyDemoPanelAsync()
        {
            var panel = await browser.NewPageAsync();
            await panel.GotoAsync($"{baseUrl}/panel?token=demo-token");
            await panel.Locator("[data-control=apply] button").WaitForAsync();
            // The Blazor circuit attaches its handlers a moment after the server-rendered page appears.
            await Task.Delay(1500);
            await panel.Locator("[data-control=led] input").CheckAsync();
            await panel.Locator("[data-control=level] input").EvaluateAsync("e => { e.value = '7'; e.dispatchEvent(new Event('change', { bubbles: true })); }");
            await panel.Locator("[data-control=apply] button").ClickAsync();
        }

        public bool CanLog => false;

        public Task StartLoggingAsync(string path) => throw new NotSupportedException();

        public Task StopLoggingAsync() => throw new NotSupportedException();
    }
}
