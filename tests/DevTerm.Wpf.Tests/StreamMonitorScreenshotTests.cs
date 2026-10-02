using System.IO;
using DevTerm.Configuration;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Generates the real WPF screenshots for docs/user-guide/stream-monitor.md and
/// docs/specs/stream-monitor.md: <see cref="StreamMonitorWindow"/> after real captures came through
/// a live session — a live PNG preview (a real, WPF-encoded scope-style screen dump returned for the
/// bundled DG1062Z profile's declared screen-capture query), and an HP-GL capture showing the
/// "preview not available yet" state. Same off-screen-show-then-render pattern as
/// <see cref="ScreenshotTests"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class StreamMonitorScreenshotTests
{
    private static readonly string _imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");

    public required TestContext TestContext { get; set; }

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

    private static void AssertRealImage(string path)
    {
        Assert.IsTrue(File.Exists(path), $"Expected a screenshot at '{path}'.");
        Assert.IsGreaterThan(1000L, new FileInfo(path).Length, "Expected a real rendered image, not a blank/near-empty file.");
    }

    [TestMethod]
    public void StreamMonitorWindow_WithCaptures_IsCaptured()
    {
        var exportDirectory = Path.Combine(Path.GetTempPath(), "dev-term", "exports");
        if (Directory.Exists(exportDirectory))
        {
            Directory.Delete(exportDirectory, recursive: true);
        }

        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("Rigol DG1062Z", exportDirectory);
            await bench.CaptureAsync(StreamContentSamples.Hpgl());
            bench.Time.Advance(TimeSpan.FromSeconds(38));
            await bench.CaptureAsync(StreamContentSamples.PostScript());
            bench.Time.Advance(TimeSpan.FromSeconds(95));

            var dg1062z = ScpiProfileCatalog.All.Single(p => p.Name.Contains("DG1062Z", StringComparison.OrdinalIgnoreCase));
            await new ScpiControlSurface(bench.Session, dg1062z, tracker: null).InvokeAsync("hcopyData", null, TestContext.CancellationToken);
            await bench.CaptureAsync([.. StreamContentSamples.ScpiBlock(StreamMonitorWindowTests.ScopeScreenPng()), (byte)'\n']);

            var window = new StreamMonitorWindow(bench.Monitor);
            WpfScreenshot.ShowOffScreen(window, 860, 520);
            StaTestRunner.DoEvents();
            window.UpdateLayout();

            Assert.IsNotNull(window.PreviewImage.Source);
            var previewPath = Path.Combine(_imagesDirectory, "wpf-stream-monitor.png");
            WpfScreenshot.Save(window, previewPath);
            AssertRealImage(previewPath);

            window.CaptureList.SelectedIndex = 0;
            StaTestRunner.DoEvents();
            window.UpdateLayout();

            Assert.Contains("Preview not available yet", window.PreviewMessage.Text);
            var hpglPath = Path.Combine(_imagesDirectory, "wpf-stream-monitor-hpgl.png");
            WpfScreenshot.Save(window, hpglPath);
            AssertRealImage(hpglPath);

            // A real plot captured from the Tektronix 2230 (a toy square says nothing about how a trace looks).
            await bench.CaptureAsync(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", "tek2230-plot.hpgl")));
            window.CaptureList.SelectedIndex = window.Items.Count - 1;
            window.ConvertMode = StreamConversionMode.InternalHpglToSvg;
            await window.ConvertSelectedAsync();
            StaTestRunner.DoEvents();
            window.UpdateLayout();

            Assert.IsNotNull(window.PreviewImage.Source);
            Assert.IsTrue(window.Items[^1].Capture.Capture.Kind.IsSvg);
            var svgPath = Path.Combine(_imagesDirectory, "wpf-stream-monitor-svg.png");
            WpfScreenshot.Save(window, svgPath);
            AssertRealImage(svgPath);

            window.Close();
        });
    }
}
