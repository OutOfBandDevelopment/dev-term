using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Devices.Busylight;
using DevTerm.Devices.K8055;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>
/// Generates the real screenshots for docs/user-guide/device-control-panels.md and closes out
/// docs/specs/device-control-panel.md's "No automated screenshot coverage" open item — a real,
/// headlessly-rendered <see cref="ControlPanelMode"/> window (K8055, Busylight, and a SCPI profile),
/// built exactly the way <see cref="TuiMode"/>'s own "_K8055 Control Panel...",
/// "_Busylight Control Panel...", and "_SCPI Instrument..." menu items build it (same
/// <c>UiDefinition</c>, same <c>IControlSurface</c>, same structured-presenter wiring), captured via
/// <see cref="TuiScreenshot"/> the same way <see cref="ScreenshotTests"/> already does for
/// ConfigureMode/TuiMode.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ControlPanelScreenshotTests
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

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

    private static void SaveDump(string fileNameStem, string dump)
    {
        Directory.CreateDirectory(_imagesDirectory);
        File.WriteAllText(Path.Combine(_imagesDirectory, $"{fileNameStem}.txt"), dump);
    }

    [TestMethod]
    public async Task ControlPanelMode_K8055_WithLiveInputData_IsCaptured()
    {
        var transport = new FakeTransport();
        var decoder = new K8055Decoder();
        var session = new Session(transport, new Pipeline([decoder]));
        await session.OpenAsync(TestContext.CancellationToken);
        var surface = new K8055ControlSurface(session);
        var definition = K8055UiDefinition.Build();

        var dump = string.Empty;
        TuiTestRunner.RunWithLoop(
            app => ControlPanelMode.BuildWindow(app, definition, surface, decoder, "dev-term — K8055 Control Panel"),
            parts =>
            {
                // A real 9-byte K8055 input report — see K8055Decoder's doc comment for the layout:
                // [reportId, digitalInRaw, 0x03, analogIn1, analogIn2, counter1Lo, counter1Hi, counter2Lo, counter2Hi].
                transport.PushIncomingAsync([0x00, 0x05, 0x03, 42, 80, 5, 0, 10, 0]).GetAwaiter().GetResult();

                var updated = TuiTestRunner.WaitUntilOnLoop(() => parts.IndicatorLabels["analogIn1"].Text == "42", _waitTimeout);
                Assert.IsTrue(updated, "Expected the Analog In 1 indicator to reflect the decoded frame.");

                dump = TuiTestRunner.InvokeOnLoop(TuiTestRunner.DumpBuffer);
                TuiTestRunner.InvokeOnLoop(() =>
                {
                    Directory.CreateDirectory(_imagesDirectory);
                    TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-control-panel-k8055.png"));
                    return true;
                });
            });

        await session.CloseAsync(TestContext.CancellationToken);

        SaveDump("tui-control-panel-k8055", dump);
        Assert.Contains("Digital Out", dump);
        Assert.Contains("0x05", dump);

        // The panel's lower sections (Analog In) scroll past the visible viewport at the default
        // headless window size — WaitUntilOnLoop above already confirmed analogIn1 updated to "42"
        // via the real IndicatorLabels dictionary, not the rendered buffer.
    }

    [TestMethod]
    public void ControlPanelMode_Busylight_IsCaptured()
    {
        var transport = new FakeTransport();
        var session = new Session(transport, new Pipeline([]));
        var surface = new BusylightControlSurface(session);
        var definition = BusylightUiDefinition.Build();

        var dump = "";
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ControlPanelMode.BuildWindow(app, definition, surface, structuredSource: null, "dev-term — Busylight Control Panel");
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException(); ;
            app.LayoutAndDraw(true);

            try
            {
                dump = TuiTestRunner.DumpBuffer();
                Directory.CreateDirectory(_imagesDirectory);
                TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-control-panel-busylight.png"));
            }
            finally
            {
                app.End(token);
            }
        });

        SaveDump("tui-control-panel-busylight", dump);
        Assert.Contains("Color", dump);
        Assert.Contains("Apply", dump);
    }

    [TestMethod]
    public async Task ControlPanelMode_ScpiKorad_WithQueryReply_IsCaptured()
    {
        var profile = ScpiProfileCatalog.All.Single(p => p.Name.Contains("KA6003P", StringComparison.OrdinalIgnoreCase));
        var transport = new FakeTransport();
        var presenter = new ScpiReplyPresenter();
        presenter.ConfigureTerminator(profile.Terminator);
        var session = new Session(transport, new Pipeline([presenter]));
        await session.OpenAsync(TestContext.CancellationToken);
        var surface = new ScpiControlSurface(session, profile, presenter);
        var definition = ScpiUiDefinitionBuilder.Build(profile);

        var dump = string.Empty;
        TuiTestRunner.RunWithLoop(
            app => ControlPanelMode.BuildWindow(app, definition, surface, presenter, $"dev-term — {profile.Name}"),
            parts =>
            {
                // Same command a "Query Set Voltage" button click invokes — exercises both the
                // Korad's terminator-less reply path (ScpiReplyPresenter.ConfigureTerminator) and the
                // just-fixed multi-parameter "vset.Voltage" field rendering without crashing (see
                // ScpiControlSurfaceTests.InvokeAsync_ParameterFieldId_IsANoOpRatherThanThrowing).
                surface.InvokeAsync("vsetQuery", null, TestContext.CancellationToken).GetAwaiter().GetResult();
                transport.PushIncomingAsync("05.00"u8.ToArray()).GetAwaiter().GetResult();

                var updated = TuiTestRunner.WaitUntilOnLoop(() => parts.IndicatorLabels["vsetQuery.reply"].Text == "05.00", _waitTimeout);
                Assert.IsTrue(updated, "Expected the Query Set Voltage reply indicator to show the Korad's terminator-less reply.");

                dump = TuiTestRunner.InvokeOnLoop(TuiTestRunner.DumpBuffer);
                TuiTestRunner.InvokeOnLoop(() =>
                {
                    Directory.CreateDirectory(_imagesDirectory);
                    TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-control-panel-scpi.png"));
                    return true;
                });
            });

        await session.CloseAsync(TestContext.CancellationToken);

        SaveDump("tui-control-panel-scpi", dump);
        Assert.HasCount(1, transport.WrittenPayloads);
        Assert.AreEqual("VSET1?", Encoding.ASCII.GetString(transport.WrittenPayloads[0]));
        Assert.Contains("Set Voltage", dump);
        Assert.Contains("05.00", dump);

        // The always-present "Custom Command" section scrolls past the visible viewport at the
        // default headless window size, but ScpiUiDefinitionBuilder always adds it to the model.
        Assert.Contains(s => s.Label == "Custom Command", definition.Sections);
    }

    public TestContext TestContext { get; set; }
}
