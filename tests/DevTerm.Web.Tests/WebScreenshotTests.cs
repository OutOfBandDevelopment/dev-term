using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Web.Tests;

/// <summary>
/// Real browser captures of the web host for docs/user-guide/web-terminal.md: headless Microsoft Edge loads the page from
/// a running host and its screenshot is written to docs/user-guide/images. Inconclusive when Edge is not installed.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Web)]
public class WebScreenshotTests
{
    private static readonly string[] _edgePaths =
    [
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    ];

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

    private async Task CaptureAsync(string path, string readOnlyToken, string imageName)
    {
        var edge = _edgePaths.FirstOrDefault(File.Exists);
        if (edge is null)
        {
            Assert.Inconclusive("Microsoft Edge is not installed.");
        }

        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "demo-token", ReadOnlyToken = "watch-token", Panel = "busylight" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            var output = Path.Combine(RepoRoot(), "docs", "user-guide", "images", imageName);
            var profile = Path.Combine(Path.GetTempPath(), "devterm-edge-" + Guid.NewGuid().ToString("N"));
            try
            {
                var token = readOnlyToken.Length > 0 ? readOnlyToken : "demo-token";
                var info = new ProcessStartInfo(edge)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                foreach (var argument in new[]
                {
                    "--headless=new", "--disable-gpu", "--no-first-run", $"--user-data-dir={profile}", "--window-size=1000,700",
                    "--virtual-time-budget=6000", $"--screenshot={output}", $"http://127.0.0.1:{port}{path}?token={token}",
                })
                {
                    info.ArgumentList.Add(argument);
                }

                using var process = Process.Start(info)!;
                var stderr = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);
                if (!process.WaitForExit(60000))
                {
                    process.Kill(entireProcessTree: true);
                    Assert.Fail("Edge did not finish within 60 s.");
                }

                await stderr;
                Assert.IsTrue(File.Exists(output) && new FileInfo(output).Length > 2000, "Edge wrote no screenshot.");
            }
            finally
            {
                try
                {
                    Directory.Delete(profile, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public Task TerminalPage_Screenshot() => CaptureAsync("/", string.Empty, "web-terminal-page.png");

    [TestMethod]
    public Task BlazorPanelPage_Screenshot() => CaptureAsync("/panel", string.Empty, "web-blazor-panel.png");

    [TestMethod]
    public Task BlazorPanelPage_ReadOnly_Screenshot() => CaptureAsync("/panel", "watch-token", "web-blazor-panel-readonly.png");

    /// <summary>Drives the real terminal page in headless Edge over the DevTools protocol: type a line, submit, read the reply from the DOM.</summary>
    [TestMethod]
    public async Task TerminalPage_TypingALine_ShowsTheDeviceReplyInTheBrowser()
    {
        var edge = _edgePaths.FirstOrDefault(File.Exists);
        if (edge is null)
        {
            Assert.Inconclusive("Microsoft Edge is not installed.");
        }

        var webPort = FreePort();
        var debugPort = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true },
            new WebOptions { Urls = $"http://127.0.0.1:{webPort}", Token = "demo-token" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        var profile = Path.Combine(Path.GetTempPath(), "devterm-edge-" + Guid.NewGuid().ToString("N"));
        Process? process = null;
        try
        {
            await using (built.Hub)
            {
                var info = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", $"--user-data-dir={profile}", $"--remote-debugging-port={debugPort}", "about:blank" })
                {
                    info.ArgumentList.Add(argument);
                }

                process = Process.Start(info)!;
                _ = process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken);
                _ = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

                using var http = new HttpClient();
                string? target = null;
                for (var attempt = 0; attempt < 60 && target is null; attempt++)
                {
                    try
                    {
                        using var targets = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{debugPort}/json/list", TestContext.CancellationToken));
                        target = targets.RootElement.EnumerateArray().FirstOrDefault(t => t.GetProperty("type").GetString() == "page").ValueKind == JsonValueKind.Object
                            ? targets.RootElement.EnumerateArray().First(t => t.GetProperty("type").GetString() == "page").GetProperty("webSocketDebuggerUrl").GetString()
                            : null;
                    }
                    catch (HttpRequestException)
                    {
                    }

                    if (target is null)
                    {
                        await Task.Delay(250, TestContext.CancellationToken);
                    }
                }

                Assert.IsNotNull(target, "Edge's DevTools endpoint never came up.");
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(target), TestContext.CancellationToken);
                var id = 0;

                async Task<string> CallAsync(string method, object parameters)
                {
                    var thisId = ++id;
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id = thisId, method, @params = parameters });
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, TestContext.CancellationToken);
                    var buffer = new byte[65536];
                    while (true)
                    {
                        var message = new StringBuilder();
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(buffer, TestContext.CancellationToken);
                            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        while (!result.EndOfMessage);
                        using var doc = JsonDocument.Parse(message.ToString());
                        if (doc.RootElement.TryGetProperty("id", out var replyId) && replyId.GetInt32() == thisId)
                        {
                            return message.ToString();
                        }
                    }
                }

                async Task<string> EvalAsync(string expression)
                {
                    using var doc = JsonDocument.Parse(await CallAsync("Runtime.evaluate", new { expression, returnByValue = true }));
                    return doc.RootElement.GetProperty("result").GetProperty("result").TryGetProperty("value", out var value) ? value.ToString() : string.Empty;
                }

                await CallAsync("Page.navigate", new { url = $"http://127.0.0.1:{webPort}/?token=demo-token" });
                for (var attempt = 0; attempt < 40 && !(await EvalAsync("document.getElementById('status') ? document.getElementById('status').textContent : ''")).Contains("connected", StringComparison.Ordinal); attempt++)
                {
                    await Task.Delay(250, TestContext.CancellationToken);
                }

                await EvalAsync("(() => { const i = document.getElementById('line'); i.value = 'hello'; document.getElementById('f').requestSubmit(); })()");
                var text = string.Empty;
                for (var attempt = 0; attempt < 40 && !text.Contains("From Loopback test", StringComparison.Ordinal); attempt++)
                {
                    await Task.Delay(250, TestContext.CancellationToken);
                    text = await EvalAsync("document.getElementById('out').textContent");
                }

                StringAssert.Contains(text, "From Loopback test");
                await built.App.StopAsync();
            }
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            process?.Dispose();
            try
            {
                Directory.Delete(profile, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
