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
    private async Task RunAsync(Func<IPage, string, Task> scenario, string? project = null, string? logsDirectory = null, CliOptions? options = null, Action<WebHost.Built>? configure = null, string? themesDirectory = null, string? converterToolsFile = null, string? manifestsDirectory = null)
    {
        var port = FreePort();
        var built = WebHost.Build(
            options ?? new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, Project = project },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "demo-token", ReadOnlyToken = "watch-token", Panel = "busylight", LogsDirectory = logsDirectory, ThemesDirectory = themesDirectory, ConverterToolsFile = converterToolsFile, ManifestsDirectory = manifestsDirectory },
            []);
        configure?.Invoke(built);
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

    /// <summary>A small session log in a temp folder: connect, send "hello", a reply, then a note-free close.</summary>
    internal static string SampleLogs()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"devterm-web-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var t0 = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var header = new DevTerm.Logging.SessionLogHeader { Created = t0, Connection = "loopback", Profile = "Scope", Presenters = ["ascii"] };
        DevTerm.Logging.SessionLogRecord Rec(DevTerm.Logging.SessionLogRecordKind kind, int ms, string? data = null) =>
            new() { Kind = kind, Timestamp = t0.AddMilliseconds(ms), Data = data is null ? default : System.Text.Encoding.ASCII.GetBytes(data) };
        new DevTerm.Logging.SessionLog(header,
        [
            Rec(DevTerm.Logging.SessionLogRecordKind.Open, 0),
            Rec(DevTerm.Logging.SessionLogRecordKind.Tx, 100, "*IDN?\n"),
            Rec(DevTerm.Logging.SessionLogRecordKind.Rx, 200, "ACME,Scope,1\n"),
            Rec(DevTerm.Logging.SessionLogRecordKind.Close, 300),
        ]).Save(Path.Combine(dir, "20261008-120000_Scope.jsonl"));
        return dir;
    }

    /// <summary>A 24-bit BMP gradient: what a scope's screen dump looks like to the Stream Monitor.</summary>
    internal static byte[] SampleBitmap()
    {
        const int w = 96, h = 48;
        var bytes = new byte[54 + (w * 3 * h)];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(w).CopyTo(bytes, 18);
        BitConverter.GetBytes(h).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var o = 54 + (((y * w) + x) * 3);
                bytes[o] = (byte)(x * 255 / w);
                bytes[o + 1] = (byte)(y * 255 / h);
                bytes[o + 2] = 160;
            }
        }

        return bytes;
    }

    private sealed class FakeRoutingLinkFactory : DevTerm.Configuration.IRoutingLinkFactory
    {
        public DevTerm.Configuration.IRoutingLink Create(DevTerm.Configuration.RoutingOptions options) => new FakeRoutingLink();
    }

    private sealed class FakeRoutingLink : DevTerm.Configuration.IRoutingLink
    {
        public event Action<Exception?>? Lost
        {
            add { }
            remove { }
        }

        public Task StartAsync(DevTerm.Core.Routing.MessageRouter router, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [TestMethod]
    public async Task ThemesPage_BuildsAndSavesATheme()
    {
        var themes = Path.Combine(Path.GetTempPath(), "devterm-web-themes-" + Guid.NewGuid().ToString("N"));
        await RunAsync(async (page, baseUrl) =>
        {
            await page.GotoAsync($"{baseUrl}/themes?token=demo-token");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(1000);
            await page.Locator("[data-seed]").SelectOptionAsync("dark");
            await page.Locator("[data-newname]").FillAsync("Bench Night");
            await page.Locator("[data-create]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-name]")).ToHaveValueAsync("Bench Night");
            await page.Locator("[data-role=Background] [data-color]").FillAsync("#102030");
            await Assertions.Expect(page.Locator("[data-role=Background] [data-hex]")).ToHaveTextAsync("#102030");
            await Assertions.Expect(page.Locator("[data-role=Background] [data-reset]")).ToBeEnabledAsync();
            await SaveAsync(page, "web-blazor-themes.png");
            await page.Locator("[data-name]").FillAsync("dark");
            await page.Locator("[data-save]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-message]")).ToContainTextAsync("reserved");
            await page.Locator("[data-name]").FillAsync("Bench Night");
            await page.Locator("[data-save]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-message]")).ToContainTextAsync("Saved 'Bench Night'");
        }, themesDirectory: themes);
        try
        {
            var loaded = DevTerm.Configuration.ThemeFile.Load(Path.Combine(themes, "Bench Night.json"));
            Assert.IsNotNull(loaded.Theme);
            Assert.AreEqual(DevTerm.Configuration.ThemeColor.Parse("#102030"), loaded.Theme[DevTerm.Configuration.ThemeRole.Background]);
        }
        finally
        {
            Directory.Delete(themes, true);
        }
    }

    [TestMethod]
    public Task ThemesPage_ReadOnlyViewer_CannotSave() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/themes?token=watch-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await page.Locator("[data-create]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-save]")).ToBeDisabledAsync();
    }, themesDirectory: Path.Combine(Path.GetTempPath(), "devterm-web-themes-none"));

    [TestMethod]
    public async Task ConvertersPage_AddsATool_ValidatesAndSaves()
    {
        var file = Path.Combine(Path.GetTempPath(), "devterm-web-conv-" + Guid.NewGuid().ToString("N") + ".json");
        await RunAsync(async (page, baseUrl) =>
        {
            await page.GotoAsync($"{baseUrl}/converters?token=demo-token");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(1000);
            await Assertions.Expect(page.Locator("[data-empty]")).ToBeVisibleAsync();
            await page.Locator("[data-add]").ClickAsync();
            await page.Locator("[data-save]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-message]")).ToContainTextAsync("path");
            await page.Locator("[data-name]").FillAsync("gs");
            await page.Locator("[data-path]").FillAsync("gswin64c.exe");
            await page.Locator("[data-args]").FillAsync("-r{dpi} -o {output} {input}");
            await page.Locator("[data-formats]").FillAsync("ps");
            await SaveAsync(page, "web-blazor-converters.png");
            await page.Locator("[data-save]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-message]")).ToHaveTextAsync("Saved.");
        }, converterToolsFile: file);
        try
        {
            var tools = new DevTerm.Configuration.ConverterToolsStore(file).Load();
            Assert.AreEqual("gs", tools.Single().Name);
            Assert.AreEqual("ps", tools.Single().Formats);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public Task ConvertersPage_ReadOnlyViewer_CannotEdit() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/converters?token=watch-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await Assertions.Expect(page.Locator("[data-add]")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-save]")).ToBeDisabledAsync();
    }, converterToolsFile: Path.Combine(Path.GetTempPath(), "devterm-web-conv-none.json"));

    [TestMethod]
    public async Task ManifestPage_BuildsAManifest_ChecksAndSavesIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"devterm-web-manifests-{Guid.NewGuid():N}");
        await RunAsync(async (page, baseUrl) =>
        {
            await page.GotoAsync($"{baseUrl}/manifest?token=demo-token");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(1000);
            await Assertions.Expect(page.Locator("[data-title]")).ToContainTextAsync("Manifest Editor");
            var name = page.Locator("[data-field=Name] input");
            await name.FillAsync("Bench Meter");
            await name.BlurAsync();
            await Assertions.Expect(page.Locator("[data-title]")).ToContainTextAsync("Bench Meter");
            await Assertions.Expect(page.Locator("[data-dirty]")).ToBeVisibleAsync();
            await page.Locator("[data-node]", new PageLocatorOptions { HasTextString = "Commands" }).First.ClickAsync();
            await page.Locator("[data-add]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-field=Template]")).ToBeVisibleAsync();
            await page.Locator("[data-check]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-status]")).ToContainTextAsync("Not valid");
            await page.Locator("[data-field=Name] input").FillAsync("Read");
            await page.Locator("[data-field=Name] input").BlurAsync();
            await page.Locator("[data-field=Template] input").FillAsync("MEAS?");
            await page.Locator("[data-field=Template] input").BlurAsync();
            await page.Locator("[data-check]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-status]")).ToContainTextAsync("Valid");
            await page.Locator("[data-save]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-dirty]")).ToHaveCountAsync(0);
            await SaveAsync(page, "web-blazor-manifest.png");
            await page.Locator("[data-undo]").ClickAsync();
        }, manifestsDirectory: folder);
        Assert.IsTrue(File.Exists(Path.Combine(folder, "Bench Meter", "device.json")) || Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Length == 1);
    }

    [TestMethod]
    public Task ManifestPage_ReadOnlyViewer_CannotEdit() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/manifest?token=watch-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await Assertions.Expect(page.Locator("[data-new]")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-save]")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-field=Name] input")).ToBeDisabledAsync();
    }, manifestsDirectory: Path.Combine(Path.GetTempPath(), "devterm-web-manifests-none"));

    [TestMethod]
    public Task RoutingPage_AddsARule_TestsItAndConnects() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/routing?token=demo-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await Assertions.Expect(page.Locator("[data-status]")).ToContainTextAsync("Stopped");
        await page.Locator("[data-host]").FillAsync("broker.test");
        await page.Locator("[data-addrule]").ClickAsync();
        await page.Locator("[data-match]").FillAsync("^T=(?<t>.+)$");
        await page.Locator("[data-topic]").FillAsync("bench/temp");
        await page.Locator("[data-payload]").FillAsync("${t}");
        await page.Locator("[data-sample]").FillAsync("T=21");
        await page.Locator("[data-test]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testline]")).ToContainTextAsync("topic bench/temp, payload 21");
        await page.Locator("[data-apply]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-message]")).ToContainTextAsync("Applied.");
        await Assertions.Expect(page.Locator("[data-status]")).ToContainTextAsync("Broker: Connected");
        await page.Locator("[data-direction]").SelectOptionAsync("BrokerToDevice");
        await Assertions.Expect(page.Locator("[data-confirm]")).ToBeVisibleAsync();
        await page.Locator("[data-direction]").SelectOptionAsync("DeviceToBroker");
        await SaveAsync(page, "web-blazor-routing.png");
        await page.Locator("[data-stop]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-status]")).ToContainTextAsync("Stopped");
    }, configure: built => built.Hub.Tab.RoutingLinkFactory = new FakeRoutingLinkFactory());

    [TestMethod]
    public Task RoutingPage_ReadOnlyViewer_CannotEdit() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/routing?token=watch-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await Assertions.Expect(page.Locator("[data-host]")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-apply]")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-addrule]")).ToBeDisabledAsync();
    });

    [TestMethod]
    public async Task RoutingConfirm_WaitsForTheViewersDecision()
    {
        var (built, _) = (WebHost.Build(new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true }, new WebOptions { Urls = $"http://127.0.0.1:{FreePort()}", Token = "t" }, []), 0);
        await using (built.Hub)
        {
            var rule = new DevTerm.Core.Routing.RoutingRule { Direction = DevTerm.Core.Routing.RoutingDirection.BrokerToDevice, Topic = "cmd", Send = "x", Confirm = true };
            var decision = built.Hub.Tab.RoutingConfirm!(rule, "x");
            Assert.IsFalse(decision.IsCompleted);
            var pending = built.Hub.PendingConfirms.Single();
            Assert.AreEqual("cmd", pending.Topic);
            Assert.IsTrue(built.Hub.ResolveConfirm(pending.Id, DevTerm.Core.Routing.RoutingConfirmChoice.Always));
            Assert.AreEqual(DevTerm.Core.Routing.RoutingConfirmChoice.Always, await decision);
            Assert.IsEmpty(built.Hub.PendingConfirms);
            Assert.IsFalse(built.Hub.ResolveConfirm(pending.Id, DevTerm.Core.Routing.RoutingConfirmChoice.Drop));
        }
    }

    [TestMethod]
    public async Task MonitorPage_ShowsACapturedImage_AndSavesIt()
    {
        var exports = Path.Combine(Path.GetTempPath(), $"devterm-web-exports-{Guid.NewGuid():N}");
        var bitmap = SampleBitmap();
        using var device = new TcpListener(IPAddress.Loopback, 0);
        device.Start();
        var gate = new TaskCompletionSource();
        _ = Task.Run(async () =>
        {
            using var client = await device.AcceptTcpClientAsync();
            await gate.Task;
            await client.GetStream().WriteAsync(bitmap);
            await Task.Delay(5000);
        });
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = ((IPEndPoint)device.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture), Presenter = ["hex"], Tui = false, Cli = true, ExportDirectory = exports };
        var toolsFile = Path.Combine(exports, "tools.json");
        Directory.CreateDirectory(exports);
        new DevTerm.Configuration.ConverterToolsStore(toolsFile).Save([new DevTerm.Configuration.StreamConvertToolOptions { Name = "copyit", Path = "cmd.exe", Arguments = "/c copy /Y {input} {output}", OutputExtension = "png" }]);
        await RunAsync(async (page, baseUrl) =>
        {
            await page.GotoAsync($"{baseUrl}/monitor?token=demo-token");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(1000);
            await Assertions.Expect(page.Locator("[data-state]")).ToContainTextAsync("Monitoring");
            await Assertions.Expect(page.Locator("[data-empty]")).ToContainTextAsync("Nothing captured yet");
            gate.SetResult();
            await Assertions.Expect(page.Locator("[data-detail]")).ToContainTextAsync("BMP image");
            await Assertions.Expect(page.Locator("[data-saved]")).ToContainTextAsync("Saved to");
            await Assertions.Expect(page.Locator("[data-preview]")).ToBeVisibleAsync();
            Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.querySelector('[data-preview]').complete && document.querySelector('[data-preview]').naturalWidth === 96"));
            await page.Locator("[data-search]").FillAsync("zzz");
            await Assertions.Expect(page.Locator("[data-empty]")).ToContainTextAsync("No capture matches");
            await page.Locator("[data-search]").FillAsync("bmp");
            await Assertions.Expect(page.Locator("[data-capture]")).ToHaveCountAsync(1);
            await SaveAsync(page, "web-blazor-monitor.png");
            await page.Locator("[data-search]").FillAsync(string.Empty);
            await page.Locator("[data-convertmode]").SelectOptionAsync(new SelectOptionValue { Label = "copyit" });
            await page.Locator("[data-convert]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-convertresult]")).ToContainTextAsync("Converted to");
            await Assertions.Expect(page.Locator("[data-capture]")).ToHaveCountAsync(2);
            await page.Locator("[data-toggle]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-state]")).ToContainTextAsync("Stopped");
        }, options: options, converterToolsFile: toolsFile);
        Assert.AreEqual(1, Directory.GetFiles(exports, "*.bmp").Length);
        Assert.AreEqual(1, Directory.GetFiles(exports, "*.png").Length);
    }

    [TestMethod]
    public Task MonitorPage_ReadOnlyViewer_CannotStartOrStop() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/monitor?token=watch-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await Assertions.Expect(page.Locator("[data-toggle]")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-state]")).ToContainTextAsync("Stopped");
    });

    [TestMethod]
    public Task PlaybackPage_OpensALog_StepsAndPlaysToTheEnd() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/playback?token=demo-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await page.Locator("[data-open]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-description]")).ToContainTextAsync("Scope (loopback)");
        await page.Locator("[data-step]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-output]")).ToContainTextAsync("[dev-term] Connected.");
        await page.Locator("[data-end]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-output]")).ToContainTextAsync("[ascii] ACME,Scope,1");
        await Assertions.Expect(page.Locator("[data-output]")).ToContainTextAsync("[tx] *IDN?");
        await Assertions.Expect(page.Locator("[data-position]")).ToContainTextAsync("End");
        await page.Locator("[data-rewind]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-output]")).Not.ToContainTextAsync("ACME");
        await page.Locator("[data-speed]").SelectOptionAsync("Max");
        await page.Locator("[data-play]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-output]")).ToContainTextAsync("ACME,Scope,1");
        await page.Locator("[data-jump]").FillAsync("nonsense");
        await page.Locator("[data-go]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-error]")).ToContainTextAsync("isn't a record number");
        await page.Locator("[data-jump]").FillAsync("2");
        await page.Locator("[data-go]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-position]")).ToContainTextAsync("2/4");
        await SaveAsync(page, "web-blazor-playback.png");
    }, logsDirectory: SampleLogs());

    [TestMethod]
    public Task PlaybackPage_AddsANote_ButNotForAReadOnlyViewer() => RunAsync(async (page, baseUrl) =>
    {
        await page.GotoAsync($"{baseUrl}/playback?token=watch-token");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);
        await page.Locator("[data-open]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-addnote]")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-savetrim]")).ToBeDisabledAsync();
    }, logsDirectory: SampleLogs());

    [TestMethod]
    public async Task PlaybackLibrary_OnlyResolvesFilesInsideItsFolder()
    {
        var dir = SampleLogs();
        var library = new PlaybackLibrary(dir);
        Assert.AreEqual(1, library.List().Count);
        Assert.IsNotNull(library.Resolve("20261008-120000_Scope.jsonl"));
        Assert.IsNull(library.Resolve("..\\secret.jsonl"));
        Assert.IsNull(library.Resolve(Path.Combine(dir, "20261008-120000_Scope.jsonl")));
        Assert.IsNull(library.Resolve("missing.jsonl"));
        await Task.CompletedTask;
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

        await Assertions.Expect(page.Locator("#xonlabel")).ToBeHiddenAsync();
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
