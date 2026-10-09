using System.Net;
using System.Net.Sockets;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Microsoft.Playwright;

namespace DevTerm.Web.Tests;

/// <summary>
/// The web counterpart of the TUI/WPF layout reviews: every page is opened in a real headless browser at desktop and phone width,
/// in light and dark, and checked for what a reviewer looking at a screenshot would flag - horizontal page overflow, overlapping
/// siblings, clipped text, unusably small controls and low text contrast. Review captures land in <c>artifacts/ui-review/web</c>.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Web)]
public sealed class WebLayoutReviewTests
{
    private const string _checker = """
        (minContrast) => {
          const problems = [];
          const name = e => e.tagName.toLowerCase() + (e.id ? '#' + e.id : '') + (e.dataset && e.dataset.control ? '[' + e.dataset.control + ']' : '') + (typeof e.className === 'string' && e.className ? '.' + e.className.split(' ')[0] : '');
          const visible = e => { const r = e.getBoundingClientRect(); const cs = getComputedStyle(e); return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none' && !e.closest('[hidden]'); };
          const all = [...document.body.querySelectorAll('*')].filter(e => !['SCRIPT', 'STYLE', 'OPTION', 'NOSCRIPT'].includes(e.tagName) && visible(e));
          if (document.documentElement.scrollWidth > window.innerWidth + 1) problems.push('[bounds] page scrolls sideways: ' + document.documentElement.scrollWidth + ' > ' + window.innerWidth);
          const parse = c => { const m = c.match(/rgba?\(([^)]+)\)/); const v = m[1].split(',').map(Number); return { r: v[0], g: v[1], b: v[2], a: v.length > 3 ? v[3] : 1 }; };
          const lum = c => { const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); };
          for (const e of all) {
            const r = e.getBoundingClientRect();
            const scroller = (() => { for (let p = e.parentElement; p && p !== document.body; p = p.parentElement) { const o = getComputedStyle(p).overflowX; if (o === 'auto' || o === 'scroll' || o === 'hidden') return p; } return null; })();
            if (!scroller && (r.right > window.innerWidth + 1 || r.left < -1)) problems.push('[bounds] ' + name(e) + ' lies outside the window (' + Math.round(r.left) + '..' + Math.round(r.right) + ')');
            const cs = getComputedStyle(e);
            const ownText = [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim());
            if (ownText && ['hidden', 'clip'].includes(cs.overflowX) && e.scrollWidth > e.clientWidth + 1 && !e.title) problems.push('[clip] ' + name(e) + ' cuts off its text (' + e.scrollWidth + ' > ' + e.clientWidth + ')');
            if (['BUTTON', 'INPUT', 'SELECT', 'TEXTAREA'].includes(e.tagName) && e.type !== 'hidden' && (r.height < 14 || r.width < 14)) problems.push('[size] ' + name(e) + ' is ' + Math.round(r.width) + 'x' + Math.round(r.height));
            if (ownText && !e.disabled && !(e.closest('button,select') && e.closest('button,select').disabled)) {
              let bg = { r: 255, g: 255, b: 255, a: 1 };
              const layers = [];
              for (let p = e; p; p = p.parentElement) { const c = parse(getComputedStyle(p).backgroundColor); if (c.a > 0) { layers.push(c); if (c.a >= 1) break; } }
              for (const c of layers.reverse()) bg = { r: c.r * c.a + bg.r * (1 - c.a), g: c.g * c.a + bg.g * (1 - c.a), b: c.b * c.a + bg.b * (1 - c.a), a: 1 };
              const l1 = lum(parse(cs.color)), l2 = lum(bg);
              const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
              if (ratio < minContrast) problems.push('[contrast] ' + name(e) + ' "' + e.textContent.trim().slice(0, 30) + '" is ' + ratio.toFixed(2) + ':1');
            }
          }
          for (const parent of new Set(all.map(e => e.parentElement))) {
            const kids = all.filter(k => k.parentElement === parent && ['static', 'relative'].includes(getComputedStyle(k).position) && getComputedStyle(k).display !== 'inline');
            for (let i = 0; i < kids.length; i++) for (let j = i + 1; j < kids.length; j++) {
              const a = kids[i].getBoundingClientRect(), b = kids[j].getBoundingClientRect();
              if (Math.min(a.right, b.right) - Math.max(a.left, b.left) > 2 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 2) problems.push('[overlap] ' + name(kids[i]) + ' and ' + name(kids[j]));
            }
          }
          return problems;
        }
        """;

    public required TestContext TestContext { get; set; }

    private static string ReviewDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevTerm.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "artifacts", "ui-review", "web");
    }

    [TestMethod]
    [DataRow("light", 1280, 800)]
    [DataRow("dark", 1280, 800)]
    [DataRow("light", 400, 800)]
    [DataRow("dark", 400, 800)]
    public async Task TerminalPage_WithADevicePanelOpen_HasNoLayoutProblems(string theme, int width, int height)
    {
        var problems = await ReviewAsync(theme, width, height, async (page, baseUrl) =>
        {
            await page.GotoAsync($"{baseUrl}/?token=demo-token");
            await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("connected");
            await Assertions.Expect(page.Locator("#device option[value=demo]")).ToHaveCountAsync(1);
            await page.Locator("#device").SelectOptionAsync("demo");
            await Assertions.Expect(page.Locator("[data-control=apply] button")).ToBeVisibleAsync();
            await page.Locator("#line").FillAsync("hello");
            await page.Locator("#line").PressAsync("Enter");
            await Assertions.Expect(page.Locator("#out")).ToContainTextAsync("From Loopback test");
        }, "terminal");
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    [DataRow("/connections", 1280, 800)]
    [DataRow("/connections", 400, 800)]
    [DataRow("/profiles", 1280, 800)]
    [DataRow("/profiles", 400, 800)]
    [DataRow("/panel", 1280, 800)]
    [DataRow("/panel", 400, 800)]
    [DataRow("/monitor", 1280, 800)]
    [DataRow("/monitor", 400, 800)]
    public async Task BlazorPage_HasNoLayoutProblems(string path, int width, int height)
    {
        var problems = await ReviewAsync("light", width, height, async (page, baseUrl) =>
        {
            await page.GotoAsync($"{baseUrl}{path}?token=demo-token");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(1000);
        }, path.TrimStart('/'));
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    [DataRow(1280, 800)]
    [DataRow(400, 800)]
    public async Task PlaybackPage_WithALogOpen_HasNoLayoutProblems(int width, int height)
    {
        var problems = await ReviewAsync("light", width, height, async (page, baseUrl) =>
        {
            await page.GotoAsync($"{baseUrl}/playback?token=demo-token");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(1000);
            await page.Locator("[data-open]").ClickAsync();
            await page.Locator("[data-end]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-output]")).ToContainTextAsync("ACME,Scope,1");
        }, "playback", WebScreenshotTests.SampleLogs());
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    [DataRow(1280, 800)]
    [DataRow(400, 800)]
    public async Task MonitorPage_WithACapture_HasNoLayoutProblems(int width, int height)
    {
        using var device = new TcpListener(IPAddress.Loopback, 0);
        device.Start();
        var gate = new TaskCompletionSource();
        _ = Task.Run(async () =>
        {
            using var client = await device.AcceptTcpClientAsync();
            await gate.Task;
            await client.GetStream().WriteAsync(WebScreenshotTests.SampleBitmap());
            await Task.Delay(5000);
        });
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = ((IPEndPoint)device.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture), Presenter = ["hex"], Tui = false, Cli = true, ExportDirectory = Path.Combine(Path.GetTempPath(), $"devterm-web-exports-{Guid.NewGuid():N}") };
        var problems = await ReviewAsync("light", width, height, async (page, baseUrl) =>
        {
            await page.GotoAsync($"{baseUrl}/monitor?token=demo-token");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(1000);
            gate.SetResult();
            await Assertions.Expect(page.Locator("[data-preview]")).ToBeVisibleAsync();
        }, "monitor-capture", options: options);
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    }

    private static async Task<IReadOnlyList<string>> ReviewAsync(string theme, int width, int height, Func<IPage, string, Task> open, string pageName, string? logsDirectory = null, CliOptions? options = null)
    {
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        var project = Path.Combine(Path.GetTempPath(), $"devterm-web-review-{Guid.NewGuid():N}.json");
        ProjectFile.From("Review", [("Scope", new CliOptions { Transport = "loopback", Presenter = ["ascii"] })]).Save(project);
        var built = WebHost.Build(
            options ?? new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, Project = project },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "demo-token", Panel = pageName == "panel" ? "demo" : null, LogsDirectory = logsDirectory },
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
                return [];
            }

            await using (browser)
            {
                var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = width, Height = height }, ColorScheme = ColorScheme.Light });
                await open(page, $"http://127.0.0.1:{port}");
                if (await page.Locator("#theme").CountAsync() > 0)
                {
                    await page.Locator("#theme").SelectOptionAsync(theme);
                }

                await Task.Delay(300);
                Directory.CreateDirectory(ReviewDirectory());
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ReviewDirectory(), $"{pageName}-{theme}-{width}x{height}.png") });
                var found = await page.EvaluateAsync<string[]>(_checker, 4.5);
                await built.App.StopAsync();
                return found;
            }
        }
    }
}
