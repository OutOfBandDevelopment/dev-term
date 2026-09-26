using System.IO;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The real screenshots for docs/user-guide/themes.md: the main window, a live control panel (a
/// manifest panel with charts, so the chart palette and chart ink show too) and the Device Profiles
/// form, each in Light and Dark - captured the same way <see cref="ScreenshotTests"/> does, with the
/// theme selected through <see cref="ActiveTheme.Select"/> exactly as View &gt; Theme does. Always
/// reset to Light afterwards, since the active theme is process-wide.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ThemeScreenshotTests
{
    private static readonly string _imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(5);

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

    [TestCleanup]
    public void ResetTheme() => ActiveTheme.Reset();

    private static void AssertRealImage(string path)
    {
        Assert.IsTrue(File.Exists(path), $"Expected a screenshot at '{path}'.");
        Assert.IsGreaterThan(1000L, new FileInfo(path).Length, "Expected a real rendered image, not a blank/near-empty file.");
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void MainWindow_IsCapturedInTheme(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            ActiveTheme.Select(theme, persist: false);
            var transport = new FakeTransport();
            var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
            var session = new Session(transport, new Pipeline([presenter]));
            var window = new MainWindow(session, new PresenterCatalog([presenter]), new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Parser = "ascii" }, IsolatedProfiles.Empty());
            WpfScreenshot.ShowOffScreen(window);

            StaTestRunner.PumpUntil(() => window.SendBox.IsEnabled, _pumpTimeout);
            await transport.PushIncomingAsync("ID TEK/2230,V81.1,VERS:14\r"u8.ToArray());
            StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 0, _pumpTimeout);
            window.SendBox.Text = "*IDN?";
            window.OutputList.Items.Add(new OutputLine("[dev-term] Connected.", OutputKind.Status));
            window.OutputList.Items.Add(new OutputLine("[error] Could not send: the device did not answer.", OutputKind.Error));
            StaTestRunner.DoEvents();

            var path = Path.Combine(_imagesDirectory, $"wpf-theme-{theme}-main.png");
            WpfScreenshot.Save(window, path);
            AssertRealImage(path);
            window.Close();
        });
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void ManifestControlPanel_IsCapturedInTheme(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            ActiveTheme.Select(theme, persist: false);
            var manifest = DevTerm.DeviceManifests.DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));
            SectionExpansionState.Forget(manifest.Name);
            var transport = new FakeTransport();
            var session = new Session(transport, new Pipeline([]));
            await session.OpenAsync(TestContext.CancellationToken);
            using var panel = DevTerm.DeviceManifests.ManifestPanel.Attach(session, manifest);
            var window = new ControlPanelWindow(panel.Definition, panel.Surface, panel.Presenter) { ShowInTaskbar = false };
            WpfScreenshot.ShowOffScreen(window, width: 640, height: 1320);

            await panel.Surface.InvokeAsync("measure", null, TestContext.CancellationToken);
            var lines = string.Concat(Enumerable.Range(0, 41).Select(i => DevTerm.Transports.Loopback.LoopbackGenerators.SensorSample(i) + "\n"));
            await transport.PushIncomingAsync(Encoding.ASCII.GetBytes(lines));

            var strip = (DevTerm.UiDefinitions.StripChartState)window.Displays["history"].State;
            Assert.IsTrue(StaTestRunner.PumpUntil(() => strip.SamplesOf("chA").Count == 41, _pumpTimeout));
            window.UpdateLayout();
            StaTestRunner.DoEvents();

            var path = Path.Combine(_imagesDirectory, $"wpf-theme-{theme}-panel.png");
            WpfScreenshot.Save(window, path);
            AssertRealImage(path);

            await session.CloseAsync(TestContext.CancellationToken);
            window.Close();
        });
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void DeviceProfilesWindow_IsCapturedInTheme(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            ActiveTheme.Select(theme, persist: false);
            var directory = Path.Combine(Path.GetTempPath(), "devterm-theme-screenshots", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            var store = new ConnectionProfileStore(directory);
            store.Save("bench-psu", new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] });
            var window = new DeviceProfilesWindow(store, new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] });
            WpfScreenshot.ShowOffScreen(window);
            StaTestRunner.DoEvents();

            var path = Path.Combine(_imagesDirectory, $"wpf-theme-{theme}-profiles.png");
            WpfScreenshot.Save(window, path);
            AssertRealImage(path);
            window.Close();
            await Task.CompletedTask;
        });
    }
}
