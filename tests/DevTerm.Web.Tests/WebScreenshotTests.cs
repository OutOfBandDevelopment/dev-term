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
    public Task ProfilesPage_AddEditAndDelete_Screenshot() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/profiles?token=demo-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000); // the circuit must be interactive before a click on the prerendered button counts
        await Assertions.Expect(page.Locator("[data-project]")).ToHaveCountAsync(2);
        await SaveAsync(page, "web-blazor-profiles.png");

        await page.Locator("#new").ClickAsync();
        await page.Locator("input[data-field=Name]").FillAsync("Bench TCP");
        await page.Locator("[data-field=Transport] select").SelectOptionAsync("tcp");
        await page.Locator("[data-field=Host] input").FillAsync("10.0.0.5");
        await page.Locator("[data-field=TcpPort] input").FillAsync("23");
        await page.Locator("[data-field=TcpPort] input").BlurAsync();
        await SaveAsync(page, "web-blazor-profile-editor.png");
        await page.Locator("#save").ClickAsync();
        await Assertions.Expect(page.Locator("[data-project]")).ToHaveCountAsync(3);

        await page.Locator("[data-project=\"Bench TCP\"] button", new PageLocatorOptions { HasText = "Delete" }).ClickAsync();
        await page.Locator("[data-project=\"Bench TCP\"] button", new PageLocatorOptions { HasText = "Really delete?" }).ClickAsync();
        await Assertions.Expect(page.Locator("[data-project]")).ToHaveCountAsync(2);
    }, BenchProject());

    [TestMethod]
    public Task ProfilesPage_AnUnnamedConnection_ShowsTheValidationError() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/profiles?token=demo-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await page.Locator("#new").ClickAsync();
        await page.Locator("#save").ClickAsync();
        await Assertions.Expect(page.Locator("[data-error=page]")).ToContainTextAsync("needs a name");
    }, BenchProject());

    [TestMethod]
    public Task ProfilesPage_ReadOnly_DisablesEditing() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/profiles?token=watch-token");
        await Assertions.Expect(page.GetByText("Read-only viewer")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#new")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-project=Scope] button").First).ToBeDisabledAsync();
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

    /// <summary>The toolbar's menu equivalents: Send as, Echo sent commands, Clear output, history and Theme all work in the page.</summary>
    [TestMethod]
    public Task TerminalPage_Toolbar_SendAsEchoClearHistoryAndTheme() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/?token=demo-token");
        await WaitConnectedAsync(page);
        await Task.Delay(500);

        await page.Locator("#parser").SelectOptionAsync("hex");
        await page.Locator("#echo").CheckAsync();
        await page.Locator("#line").FillAsync("68 65 6c 6c 6f");
        await page.Locator("#line").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("Out> 68 65 6c 6c 6f");
        await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("From Loopback test");

        await page.Locator("#line").PressAsync("ArrowUp");
        await Assertions.Expect(page.Locator("#line")).ToHaveValueAsync("68 65 6c 6c 6f");

        await page.Locator("#clear").ClickAsync();
        await Assertions.Expect(page.Locator("#out")).Not.ToContainTextAsync("From Loopback test");

        await page.Locator("#theme").SelectOptionAsync("dark");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await page.Locator("#theme").SelectOptionAsync("light");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
    });

    /// <summary>Start logging, send a line, stop: the log file exists and holds the reply; the download link serves it.</summary>
    [TestMethod]
    public Task TerminalPage_Toolbar_LoggingRecordsTheSession() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/?token=demo-token");
        await WaitConnectedAsync(page);
        await Task.Delay(500);
        await page.Locator("#log").ClickAsync();
        await Assertions.Expect(page.Locator("#log")).ToHaveTextAsync("Stop logging");
        await page.Locator("#line").FillAsync("hello");
        await page.Locator("#line").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("From Loopback test");
        var response = await page.APIRequest.GetAsync($"{baseUrl}/api/sessions/main/log?token=demo-token");
        Assert.IsTrue(response.Ok);
        StringAssert.Contains(await response.TextAsync(), Convert.ToBase64String("From Loopback test"u8.ToArray())[..20]);
        await page.Locator("#log").ClickAsync();
        await Assertions.Expect(page.Locator("#log")).ToHaveTextAsync("Start logging");
    });

    /// <summary>The Device menu opens a plugin panel and a control on it reaches the device.</summary>
    [TestMethod]
    public Task TerminalPage_DeviceMenu_OpensAPanelAndItsControlsReachTheDevice() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/?token=demo-token");
        await WaitConnectedAsync(page);
        await Assertions.Expect(page.Locator("#device option[value=demo]")).ToHaveCountAsync(1);
        await page.Locator("#device").SelectOptionAsync("demo");
        await page.Locator("[data-control=led] input").CheckAsync();
        await page.Locator("[data-control=apply] button").ClickAsync();
        await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("Unrecognized: SET LED=1");
        await page.Locator("#device").SelectOptionAsync("");
        await Assertions.Expect(page.Locator("#panel")).ToBeHiddenAsync();
    });

    /// <summary>The host's own tab can be disconnected and connected again from the page.</summary>
    [TestMethod]
    public Task TerminalPage_MainSession_DisconnectsAndReconnects() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/?token=demo-token");
        await WaitConnectedAsync(page);
        await Assertions.Expect(page.Locator("#toggle")).ToHaveTextAsync("Disconnect");
        await page.Locator("#toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("session closed");
        await Assertions.Expect(page.Locator("#toggle")).ToHaveTextAsync("Connect");
        await SaveAsync(page, "web-terminal-disconnected.png");
        await page.Locator("#toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#toggle")).ToHaveTextAsync("Disconnect");
    });

    /// <summary>Switching the main tab to another saved profile replaces its session in place and keeps the tab.</summary>
    [TestMethod]
    public Task TerminalPage_MainSession_SwitchesProfileInPlace() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/?token=demo-token");
        await WaitConnectedAsync(page);
        await Task.Delay(500);
        await page.Locator("#switchprofile").SelectOptionAsync("Supply");
        await page.Locator("#switch").ClickAsync();
        await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("(Supply)");
        await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("Switched to Supply.");
        await page.Locator("#line").FillAsync("68 65 6c 6c 6f");
        await page.Locator("#line").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("[hex]");
        await Assertions.Expect(page.Locator("button.tab")).ToHaveCountAsync(1);
        await SaveAsync(page, "web-terminal-switched.png");
    }, BenchProject());

    /// <summary>Opens two saved profiles as tabs beside the host's own session; each keeps its own output, and closing one leaves the rest.</summary>
    [TestMethod]
    public Task TerminalPage_MultipleSessions_OpenSwitchAndClose() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/?token=demo-token");
        await WaitConnectedAsync(page);
        await Task.Delay(500); // the profile list loads after the page does

        await page.Locator("#profile").SelectOptionAsync("Scope");
        await page.Locator("#open").ClickAsync();
        await Assertions.Expect(page.Locator("button.tab")).ToHaveCountAsync(2);
        await page.Locator("#line").FillAsync("hello");
        await page.Locator("#line").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("From Loopback test");

        await page.Locator("#profile").SelectOptionAsync("Supply");
        await page.Locator("#open").ClickAsync();
        await Assertions.Expect(page.Locator("button.tab")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("#out")).Not.ToContainTextAsync("From Loopback test"); // a different session
        await SaveAsync(page, "web-terminal-tabs.png");

        await page.Locator("button.tab[data-tab=main]").ClickAsync();
        await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("dev-term");
        await page.Locator("button.close").First.ClickAsync();
        await Assertions.Expect(page.Locator("button.tab")).ToHaveCountAsync(2);
    }, BenchProject());
}
