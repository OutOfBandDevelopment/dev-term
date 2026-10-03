using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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
}
