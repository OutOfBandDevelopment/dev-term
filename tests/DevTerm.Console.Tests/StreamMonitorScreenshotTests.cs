using DevTerm.Core.StreamContent;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>
/// Generates the real TUI screenshot for docs/user-guide/stream-monitor.md and
/// docs/specs/stream-monitor.md: the <see cref="StreamMonitorMode"/> window after a few real
/// captures (a sniffed PNG, an idle-ended HP-GL plot, and a declared, block-wrapped BMP from a real SCPI screen-capture query) came through
/// a live session — same capture pattern as <see cref="ScreenshotTests"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class StreamMonitorScreenshotTests
{
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not find the repo root (DevTerm.slnx) above '{AppContext.BaseDirectory}'.");
    }

    private static readonly string _imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");

    [TestMethod]
    public async Task StreamMonitor_WithCaptures_IsCaptured()
    {
        var exportDirectory = Path.Combine(Path.GetTempPath(), "dev-term", "exports");
        if (Directory.Exists(exportDirectory))
        {
            Directory.Delete(exportDirectory, recursive: true);
        }

        await using var bench = await StreamMonitorBench.StartAsync("Rigol DG1062Z", exportDirectory);
        await bench.CaptureAsync(StreamContentSamples.Png());
        bench.Time.Advance(TimeSpan.FromSeconds(41));
        await bench.CaptureAsync(StreamContentSamples.Hpgl());
        bench.Time.Advance(TimeSpan.FromSeconds(73));

        // The real declared-hint path: the bundled DG1062Z profile's screen-capture query, sent
        // through its real control surface, tells the monitor to expect an image; the reply is a
        // BMP in a definite-length block, the way the instrument returns it.
        var dg1062z = ScpiProfileCatalog.All.Single(p => p.Name.Contains("DG1062Z", StringComparison.OrdinalIgnoreCase));
        await new ScpiControlSurface(bench.Session, dg1062z, tracker: null).InvokeAsync("hcopyData", null);
        var declared = await bench.CaptureAsync([.. StreamContentSamples.ScpiBlock(StreamContentSamples.Bmp()), (byte)'\n']);

        var dump = "";
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = StreamMonitorMode.BuildWindow(app, bench.Monitor);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                dump = TuiTestRunner.DumpBuffer();
                Directory.CreateDirectory(_imagesDirectory);
                TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-stream-monitor.png"));
            }
            finally
            {
                app.End(token);
                parts.Window.Dispose();
            }
        });

        File.WriteAllText(Path.Combine(_imagesDirectory, "tui-stream-monitor.txt"), dump);

        Assert.AreEqual(StreamContentKind.Bmp, declared.Capture.Kind);
        Assert.IsTrue(declared.Capture.WasDeclared);
        Assert.Contains("Monitoring Rigol DG1062Z", dump);
        Assert.Contains("BMP", dump);
        Assert.Contains("HPGL", dump);
        Assert.Contains("Rigol_DG1062Z_20260925-", dump);
        Assert.Contains("Stop Monitoring", dump);
    }
}
