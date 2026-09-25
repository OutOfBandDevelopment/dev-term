using System.IO;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Devices.Busylight;
using DevTerm.Devices.K8055;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Generates the real screenshots for docs/user-guide/device-control-panels.md and closes out
/// docs/specs/device-control-panel.md's "No automated screenshot coverage" open item — a real,
/// laid-out <see cref="ControlPanelWindow"/> (K8055, Busylight, and a SCPI profile), built exactly
/// the way <c>MainWindow</c>'s own "K8055 Control Panel...", "Busylight Control Panel...", and
/// "SCPI Instrument..." menu items build it (same <c>UiDefinition</c>, same <c>IControlSurface</c>,
/// same structured-presenter wiring), rendered via <see cref="WpfScreenshot"/> the same way
/// <see cref="ScreenshotTests"/> already does for <c>MainWindow</c>/<c>DeviceProfilesWindow</c>.
/// The object graph (FakeTransport → Session → presenter → surface → ControlPanelWindow) mirrors
/// <see cref="ScpiControlPanelEndToEndTests"/>, which already proves this wiring works end to end.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ControlPanelScreenshotTests
{
    private static readonly string _imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(5);

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
    public void ControlPanelWindow_K8055_WithLiveInputData_IsCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var transport = new FakeTransport();
            var decoder = new K8055Decoder();
            var session = new Session(transport, new Pipeline([decoder]));
            var surface = new K8055ControlSurface(session);
            var definition = K8055UiDefinition.Build();
            var window = new ControlPanelWindow(definition, surface, decoder) { ShowInTaskbar = false };
            await session.OpenAsync(TestContext.CancellationToken);
            WpfScreenshot.ShowOffScreen(window);

            // A real 9-byte K8055 input report — see K8055Decoder's doc comment for the layout:
            // [reportId, digitalInRaw, 0x03, analogIn1, analogIn2, counter1Lo, counter1Hi, counter2Lo, counter2Hi].
            await transport.PushIncomingAsync([0x00, 0x05, 0x03, 42, 80, 5, 0, 10, 0]);

            var updated = StaTestRunner.PumpUntil(() => window.IndicatorLabels["analogIn1"].Text == "42", _pumpTimeout);
            Assert.IsTrue(updated, "Expected the Analog In 1 indicator to reflect the decoded frame.");

            var path = Path.Combine(_imagesDirectory, "wpf-control-panel-k8055.png");
            WpfScreenshot.Save(window, path);
            AssertRealImage(path);

            await session.CloseAsync(TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public void ControlPanelWindow_Busylight_IsCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var transport = new FakeTransport();
            var session = new Session(transport, new Pipeline([]));
            var surface = new BusylightControlSurface(session);
            var definition = BusylightUiDefinition.Build();
            var window = new ControlPanelWindow(definition, surface, structuredSource: null) { ShowInTaskbar = false };
            WpfScreenshot.ShowOffScreen(window);

            var path = Path.Combine(_imagesDirectory, "wpf-control-panel-busylight.png");
            WpfScreenshot.Save(window, path);
            AssertRealImage(path);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ControlPanelWindow_ScpiKorad_WithQueryReply_IsCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var profile = ScpiProfileCatalog.All.Single(p => p.Name.Contains("KA6003P", StringComparison.OrdinalIgnoreCase));
            var transport = new FakeTransport();
            var presenter = new ScpiReplyPresenter();
            presenter.ConfigureTerminator(profile.Terminator);
            var session = new Session(transport, new Pipeline([presenter]));
            var surface = new ScpiControlSurface(session, profile, presenter);
            var definition = ScpiUiDefinitionBuilder.Build(profile);
            var window = new ControlPanelWindow(definition, surface, presenter) { ShowInTaskbar = false };
            await session.OpenAsync(TestContext.CancellationToken);
            WpfScreenshot.ShowOffScreen(window);

            // Same command a "Query Set Voltage" button click invokes — exercises both the
            // Korad's terminator-less reply path (ScpiReplyPresenter.ConfigureTerminator) and the
            // just-fixed multi-parameter "vset.Voltage" field rendering without crashing (see
            // ScpiControlSurfaceTests.InvokeAsync_ParameterFieldId_IsANoOpRatherThanThrowing).
            await surface.InvokeAsync("vsetQuery", null, TestContext.CancellationToken);
            Assert.HasCount(1, transport.WrittenPayloads);
            Assert.AreEqual("VSET1?", Encoding.ASCII.GetString(transport.WrittenPayloads[0]));

            await transport.PushIncomingAsync("05.00"u8.ToArray());

            var updated = StaTestRunner.PumpUntil(() => window.IndicatorLabels["vsetQuery.reply"].Text == "05.00", _pumpTimeout);
            Assert.IsTrue(updated, "Expected the Query Set Voltage reply indicator to show the Korad's terminator-less reply.");

            var path = Path.Combine(_imagesDirectory, "wpf-control-panel-scpi.png");
            WpfScreenshot.Save(window, path);
            AssertRealImage(path);

            await session.CloseAsync(TestContext.CancellationToken);
        });
    }

    public TestContext TestContext { get; set; }
}
