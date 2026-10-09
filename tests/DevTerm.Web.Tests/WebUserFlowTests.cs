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
    private static string FlowProject()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devterm-web-flow-{Guid.NewGuid():N}.json");
        ProjectFile.From("Flow", [("Fresh", new CliOptions { Transport = "loopback", Presenter = ["ascii"], Parser = "ascii" })]).Save(file);
        return file;
    }

    protected override async Task RunAsync(Func<IFrontEndDriver, Task> flow)
    {
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, Project = FlowProject() },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "demo-token" },
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
                await flow(new Driver(page, built.Hub));
            }

            await built.App.StopAsync();
        }
    }

    private sealed class Driver(IPage page, SessionHub hub) : IFrontEndDriver
    {
        private string? _logPath;
        private string? _wantedPath;

        public string Name => "Web";

        public bool CanDisconnect => true;

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
            return status.Contains("connected", StringComparison.OrdinalIgnoreCase) && !status.Contains("disconnected", StringComparison.OrdinalIgnoreCase) && !status.Contains("session closed", StringComparison.OrdinalIgnoreCase);
        }

        public async Task DisconnectAsync()
        {
            await page.Locator("#toggle", new PageLocatorOptions { HasText = "Disconnect" }).ClickAsync();
            await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("session closed");
        }

        public async Task ReconnectAsync()
        {
            await page.Locator("#toggle", new PageLocatorOptions { HasText = "Connect" }).ClickAsync();
            await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("Connected to");
            await Assertions.Expect(page.Locator("#toggle")).ToHaveTextAsync("Disconnect");
        }

        public bool CanSwitchProfile => true;

        public async Task SwitchToLoopbackProfileAsync()
        {
            await page.Locator("#switchprofile").SelectOptionAsync("Fresh");
            await page.Locator("#switch").ClickAsync();
            await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("Switched to Fresh.");
            await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("(Fresh)");
        }

        public bool CanUsePanel => true;

        public async Task ApplyDemoPanelAsync()
        {
            await Assertions.Expect(page.Locator("#device option[value=demo]")).ToHaveCountAsync(1);
            await page.Locator("#device").SelectOptionAsync("demo");
            await page.Locator("[data-control=led] input").CheckAsync();
            await page.Locator("[data-control=level] input").EvaluateAsync("e => { e.value = '7'; e.dispatchEvent(new Event('change', { bubbles: true })); }");
            await page.Locator("[data-control=apply] button").ClickAsync();
        }

        public bool CanLog => true;

        public async Task StartLoggingAsync(string path)
        {
            await page.Locator("#log", new PageLocatorOptions { HasText = "Start logging" }).ClickAsync();
            await Assertions.Expect(page.Locator("#log")).ToHaveTextAsync("Stop logging");
            _logPath = hub.LogPath;
            _wantedPath = path;
        }

        public async Task StopLoggingAsync()
        {
            await page.Locator("#log", new PageLocatorOptions { HasText = "Stop logging" }).ClickAsync();
            await Assertions.Expect(page.Locator("#log")).ToHaveTextAsync("Start logging");
            // The page logs to the default ~/.dev-term/logs name; hand the file to the shared flow's path.
            File.Copy(_logPath!, _wantedPath!, overwrite: true);
            File.Delete(_logPath!);
        }
    }
}
