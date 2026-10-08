using System.Net;
using System.Net.Sockets;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Microsoft.Playwright;

namespace DevTerm.Web.Tests;

/// <summary>
/// Real browser captures of the web host for docs/user-guide/web-terminal.md, driven by Playwright against the installed
/// Microsoft Edge (no browser download needed). Inconclusive when Edge cannot be launched.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Web)]
public class WebScreenshotTests
{
    public required TestContext TestContext { get; set; }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevTerm.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Starts a loopback web host and a headless Edge page, runs <paramref name="scenario"/>, then tears both down.</summary>
    private async Task RunAsync(Func<IPage, string, Task> scenario, string? project = null)
    {
        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, Project = project },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "demo-token", ReadOnlyToken = "watch-token", Panel = "busylight" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            IPlaywright playwright = await Playwright.CreateAsync();
            try
            {
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
                    var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 700 }, ColorScheme = ColorScheme.Light });
                    await scenario(page, $"http://127.0.0.1:{port}");
                }
            }
            finally
            {
                playwright.Dispose();
                await built.App.StopAsync();
            }
        }
    }

    private static Task SaveAsync(IPage page, string imageName) =>
        page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(RepoRoot(), "docs", "user-guide", "images", imageName) });

    private static async Task WaitConnectedAsync(IPage page) =>
        await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("connected");

    private static string BenchProject()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devterm-web-shot-{Guid.NewGuid():N}.json");
        ProjectFile.From("Bench", [("Scope", new CliOptions { Transport = "loopback", Presenter = ["ascii"] }), ("Supply", new CliOptions { Transport = "loopback", Presenter = ["hex"] })]).Save(file);
        return file;
    }

    [TestMethod]
    public Task ConnectionsPage_OpenAndClose_Screenshot() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/connections?token=demo-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000); // the circuit must be interactive before a click on the prerendered button counts
        await page.Locator("[data-project=Scope] button").ClickAsync();
        await Assertions.Expect(page.Locator("[data-open]")).ToHaveCountAsync(1);
        await SaveAsync(page, "web-blazor-connections.png");
        await page.Locator("[data-open] button").ClickAsync();
        await Assertions.Expect(page.Locator("[data-open]")).ToHaveCountAsync(0);
    }, BenchProject());

    [TestMethod]
    public Task ConnectionsPage_ReadOnly_DisablesTheButtons() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/connections?token=watch-token");
        await Assertions.Expect(page.GetByText("Read-only viewer")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-project=Scope] button")).ToBeDisabledAsync();
    }, BenchProject());

    [TestMethod]
    public Task TerminalPage_Screenshot() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/?token=demo-token");
        await WaitConnectedAsync(page);
        await SaveAsync(page, "web-terminal-page.png");
    });

    [TestMethod]
    public Task BlazorPanelPage_Screenshot() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/panel?token=demo-token");
        await page.Locator("[data-control]").First.WaitForAsync();
        await SaveAsync(page, "web-blazor-panel.png");
    });

    [TestMethod]
    public Task BlazorPanelPage_ReadOnly_Screenshot() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/panel?token=watch-token");
        await Assertions.Expect(page.GetByText("Read-only viewer")).ToBeVisibleAsync();
        await SaveAsync(page, "web-blazor-panel-readonly.png");
    });

    /// <summary>Types a line in the real terminal page and waits for the loopback device's reply to appear in the DOM.</summary>
    [TestMethod]
    public Task TerminalPage_TypingALine_ShowsTheDeviceReplyInTheBrowser() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/?token=demo-token");
        await WaitConnectedAsync(page);
        await page.Locator("#line").FillAsync("hello");
        await page.Locator("#line").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("From Loopback test");
        await SaveAsync(page, "web-terminal-reply.png");
    });
}
