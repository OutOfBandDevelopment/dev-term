using System.Globalization;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.DeviceManifests;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Captures the bundled loopback sensor manifest's control panel (bar graph, strip chart, vectors) filling as a paced
/// <c>Samples: N</c> stream arrives, for the user guide's loopback walkthrough; <c>scripts/make_loopback_gif.py</c>
/// assembles the stills into <c>tui-loopback-charts.gif</c>. The WPF counterpart is
/// <c>DevTerm.Wpf.Tests.LoopbackChartsScreenshotTests</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class LoopbackChartsScreenshotTests
{
    private const int _intervalMs = 150;
    private const int _samples = 24;
    private const int _stillEvery = 4;
    private const string _prefix = "tui-loopback-charts-";
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(15);

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task PacedSamples_FillTheCharts_StillsAreCaptured()
    {
        var imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
        Directory.CreateDirectory(imagesDirectory);
        var manifest = DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));
        DevTerm.Configuration.SectionExpansionState.Forget(manifest.Name);
        DevTerm.Configuration.SectionExpansionState.Set(manifest.Name, ControlPanelMode.NotesSectionLabel, expanded: false);
        var transport = new DevTerm.Transports.Loopback.LoopbackTransport(Options.Create(new DevTerm.Transports.Loopback.LoopbackTransportOptions { SampleIntervalMs = _intervalMs }));
        await using var session = new Session(transport, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        using var panel = ManifestPanel.Attach(session, manifest);

        var last = string.Empty;
        try
        {
            TuiTestRunner.RunWithLoop(
                app =>
                {
                    app.Driver!.SetScreenSize(90, 66);
                    return ManifestPanelMode.BuildWindow(app, panel);
                },
                parts =>
                {
                    // The transport holds the send until the last paced line is pushed, so run it unawaited.
                    var send = Task.Run(() => panel.Surface.InvokeAsync("samples", _samples.ToString(CultureInfo.InvariantCulture), TestContext.CancellationToken), TestContext.CancellationToken);
                    var still = 0;
                    for (var count = _stillEvery; count <= _samples; count += _stillEvery)
                    {
                        var wanted = count;
                        var seen = TuiTestRunner.WaitUntilOnLoop(
                            () => ((StripChartState)parts.DisplayViews["history"].State).SamplesOf("chA").Count >= wanted,
                            _waitTimeout);
                        Assert.IsTrue(seen, $"Expected {wanted} samples in the strip chart.");
                        var number = ++still;
                        last = TuiTestRunner.InvokeOnLoop(() =>
                        {
                            TuiTestRunner.CurrentApp.LayoutAndDraw(true);
                            TuiScreenshot.Save(Path.Combine(imagesDirectory, $"{_prefix}{number}.png"));
                            return TuiTestRunner.DumpBuffer();
                        });
                    }

                    send.GetAwaiter().GetResult();
                });
        }
        finally
        {
            DevTerm.Configuration.SectionExpansionState.Forget(manifest.Name);
        }

        Assert.Contains("[-] Levels", last);
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
