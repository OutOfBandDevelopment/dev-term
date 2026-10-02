using System.Globalization;
using System.IO;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.DeviceManifests;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Loopback;
using DevTerm.UiDefinitions;
using Microsoft.Extensions.Options;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Captures the bundled loopback sensor manifest's control panel (bar graph, strip chart, vectors) filling as a paced
/// <c>Samples: N</c> stream arrives, for the user guide's loopback walkthrough; <c>scripts/make_loopback_gif.py</c>
/// assembles the stills into <c>wpf-loopback-charts.gif</c>. The TUI counterpart is
/// <c>DevTerm.Console.Tests.LoopbackChartsScreenshotTests</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class LoopbackChartsScreenshotTests
{
    private const int _intervalMs = 150;
    private const int _samples = 24;
    private const int _stillEvery = 4;
    private const string _prefix = "wpf-loopback-charts-";
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(15);

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public void PacedSamples_FillTheCharts_StillsAreCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
            Directory.CreateDirectory(imagesDirectory);
            var manifest = DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));
            DevTerm.Configuration.SectionExpansionState.Forget(manifest.Name);
            var transport = new LoopbackTransport(Options.Create(new LoopbackTransportOptions { SampleIntervalMs = _intervalMs }));
            var session = new Session(transport, new Pipeline([]));
            await session.OpenAsync(TestContext.CancellationToken);
            using var panel = ManifestPanel.Attach(session, manifest);
            var window = new ControlPanelWindow(panel.Definition, panel.Surface, panel.Presenter) { ShowInTaskbar = false };
            WpfScreenshot.ShowOffScreen(window, width: 640, height: 1320);

            // The transport holds the send until the last paced line is pushed, so run it unawaited.
            var send = panel.Surface.InvokeAsync("samples", _samples.ToString(CultureInfo.InvariantCulture), TestContext.CancellationToken);
            var strip = (StripChartState)window.Displays["history"].State;

            var still = 0;
            for (var count = _stillEvery; count <= _samples; count += _stillEvery)
            {
                var wanted = count;
                Assert.IsTrue(StaTestRunner.PumpUntil(() => strip.SamplesOf("chA").Count >= wanted, _pumpTimeout), $"Expected {wanted} samples in the strip chart.");
                window.UpdateLayout();
                StaTestRunner.DoEvents();
                var path = Path.Combine(imagesDirectory, $"{_prefix}{++still}.png");
                WpfScreenshot.Save(window, path);
                Assert.IsGreaterThan(1000L, new FileInfo(path).Length, "Expected a real rendered image.");
            }

            await send;
            await session.CloseAsync(TestContext.CancellationToken);
        });
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find DevTerm.slnx above the test output.");
    }
}
