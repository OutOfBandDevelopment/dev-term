using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.DeviceManifests;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Loopback;
using DevTerm.UiDefinitions;
using Microsoft.Extensions.Options;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI control panel fitting its window and remembering itself: rows wider than the window
/// scroll sideways (and a focused control scrolls into view), the Notes re-wrap when the terminal is
/// resized, sections reopen collapsed/expanded as last left — plus a device manifest's panel with the
/// chart controls, end to end over the real loopback transport.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ControlPanelModeFitTests
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    public required TestContext TestContext { get; set; }

    private static void RunHeadless(UiDefinition definition, IControlSurface surface, Action<IApplication, ControlPanelWindowParts> body) =>
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ControlPanelMode.BuildWindow(app, definition, surface, null, definition.Name);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                body(app, parts);
            }
            finally
            {
                app.End(token);
            }
        });

    private static UiDefinition Scpi34401a() =>
        ScpiUiDefinitionBuilder.Build(ScpiProfileCatalog.All.Single(p => p.Name.Contains("34401A", StringComparison.OrdinalIgnoreCase)));

    private static ScpiControlSurface Scpi34401aSurface() =>
        new(new Session(new FakeTransport(), new Pipeline([])), ScpiProfileCatalog.All.Single(p => p.Name.Contains("34401A", StringComparison.OrdinalIgnoreCase)), tracker: null);

    [TestMethod]
    public void WideRow_MakesTheFormScrollSideways_AndFocusBringsItsMarkerIntoView()
    {
        var definition = Scpi34401a();
        SectionExpansionState.Forget(definition.Name);
        RunHeadless(definition, Scpi34401aSurface(), (app, parts) =>
        {
            var form = parts.FormContent;
            Assert.IsGreaterThan(form.Viewport.Width, form.GetContentSize().Width, "The 4-wire row is wider than the window, so the content is too.");
            Assert.AreEqual(0, form.Viewport.X);

            var button = parts.ControlViews["confFres.send"];
            var marker = parts.InfoMarkers["confFres.send"];
            button.SetFocus();
            app.LayoutAndDraw(true);

            var body = button.SuperView!;
            var markerRight = body.Frame.X + marker.Frame.Right;
            Assert.IsGreaterThan(0, form.Viewport.X, "Focusing the wide row scrolled the form right...");
            Assert.IsLessThanOrEqualTo(form.Viewport.X + form.Viewport.Width, markerRight, "...far enough that its (i) marker is visible.");
            Assert.Contains("(i)", TuiTestRunner.DumpBuffer().Split('\n').Single(l => l.Contains("4-Wire Resistance Range:", StringComparison.Ordinal) && l.Contains('⟦')));

            parts.SectionHeaders["Common"].SetFocus();
            app.LayoutAndDraw(true);
            Assert.AreEqual(0, form.Viewport.X, "A section header scrolls back to the left edge.");
        });
    }

    [TestMethod]
    public void NarrowDefinition_HasNoSidewaysScrolling()
    {
        var definition = new UiDefinition { Name = "Narrow", Sections = [new UiSection { Label = "S", Controls = [new ButtonControl { Id = "b", Label = "Go" }] }] };
        RunHeadless(definition, new NullSurface(), (_, parts) =>
            Assert.AreEqual(parts.FormContent.Viewport.Width, parts.FormContent.GetContentSize().Width));
    }

    [TestMethod]
    public void Notes_ReWrapWhenTheTerminalIsResized()
    {
        var definition = new UiDefinition
        {
            Name = "Resizable Notes",
            Description = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor sit amet", 12)),
            Sections = [new UiSection { Label = "S", Controls = [new ButtonControl { Id = "b", Label = "Go" }] }],
        };
        RunHeadless(definition, new NullSurface(), (app, parts) =>
        {
            var notes = parts.SectionBodies[ControlPanelMode.NotesSectionLabel];
            var wideRows = notes.Frame.Height;

            app.Driver!.SetScreenSize(50, 30);
            app.LayoutAndDraw(true);

            var narrowRows = notes.Frame.Height;
            Assert.IsGreaterThan(wideRows, narrowRows, "Narrower window, more wrapped rows.");
            var text = ((Label)notes.SubViews.Single()).Text;
            Assert.IsTrue(text.Split('\n').All(l => l.Length <= ControlPanelMode.NotesWrapWidth(parts.FormContent.Viewport.Width)), "Every line fits the new width.");
            Assert.AreEqual(parts.FormContent.Viewport.Width, parts.FormContent.GetContentSize().Width, "The re-wrapped notes don't force sideways scrolling.");

            app.Driver.SetScreenSize(120, 30);
            app.LayoutAndDraw(true);
            Assert.IsLessThan(narrowRows, notes.Frame.Height, "Wider again, fewer rows.");
        });
    }

    [TestMethod]
    public void CollapsedSection_StaysCollapsedWhenThePanelReopens()
    {
        var definition = new UiDefinition
        {
            Name = "Remembered Sections",
            Sections =
            [
                new UiSection { Label = "One", Controls = [new ButtonControl { Id = "a", Label = "A" }] },
                new UiSection { Label = "Two", Controls = [new ButtonControl { Id = "b", Label = "B" }] },
            ],
        };
        SectionExpansionState.Forget(definition.Name);
        try
        {
            RunHeadless(definition, new NullSurface(), (_, parts) =>
            {
                Assert.AreEqual("[-] One", parts.SectionHeaders["One"].Text);
                parts.SectionHeaders["One"].InvokeCommand(Command.Accept);
            });

            RunHeadless(definition, new NullSurface(), (_, parts) =>
            {
                Assert.AreEqual("[+] One", parts.SectionHeaders["One"].Text, "Reopened the way it was left.");
                Assert.IsFalse(parts.SectionBodies["One"].Visible);
                Assert.AreEqual("[-] Two", parts.SectionHeaders["Two"].Text);
                Assert.AreEqual(parts.SectionHeaders["One"].Frame.Y + 2, parts.SectionHeaders["Two"].Frame.Y, "...and laid out collapsed, not just labeled so.");
            });
        }
        finally
        {
            SectionExpansionState.Forget(definition.Name);
        }
    }

    [TestMethod]
    public async Task ManifestPanel_LoopbackDemo_ChartsUpdateLive()
    {
        var manifest = DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));
        SectionExpansionState.Forget(manifest.Name);
        var transport = new DevTerm.Transports.Loopback.LoopbackTransport(Options.Create(new LoopbackTransportOptions()));
        await using var session = new Session(transport, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        using var panel = ManifestPanel.Attach(session, manifest);

        TuiTestRunner.RunWithLoop(
            app => ManifestPanelMode.BuildWindow(app, panel),
            parts =>
            {
                Assert.IsInstanceOfType<BarGraphState>(parts.DisplayViews["levels"].State);
                Assert.IsInstanceOfType<StripChartState>(parts.DisplayViews["history"].State);
                Assert.IsInstanceOfType<VectorState>(parts.DisplayViews["polar"].State);

                panel.Surface.InvokeAsync("samples", "20", TestContext.CancellationToken).GetAwaiter().GetResult();
                var filled = TuiTestRunner.WaitUntilOnLoop(
                    () => ((StripChartState)parts.DisplayViews["history"].State).SamplesOf("chA").Count == 20,
                    _waitTimeout);
                Assert.IsTrue(filled, "Every streamed sample line lands in the strip chart.");

                var lines = TuiTestRunner.InvokeOnLoop(() => parts.DisplayViews["levels"].Render().Lines());
                Assert.Contains(DevTerm.Transports.Loopback.LoopbackGenerators.SensorSample(19).Split(' ')[0].Split('=')[1], lines[0], "The bar shows channel A's latest value.");
                Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => ((VectorState)parts.DisplayViews["xy"].State).HasPoint));

                var dump = TuiTestRunner.InvokeOnLoop(TuiTestRunner.DumpBuffer);
                Assert.Contains("▕", dump, "The bar graph is on screen.");
            });
    }

    [TestMethod]
    public void ManifestPanel_ShowsWhatEachButtonSends()
    {
        var manifest = DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));
        var session = new Session(new FakeTransport(), new Pipeline([]));
        using var panel = ManifestPanel.Attach(session, manifest);

        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ManifestPanelMode.BuildWindow(app, panel);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                parts.ControlViews["measure"].SetFocus();
                Assert.AreEqual("Sends: MEAS?\\n", parts.PreviewLabel.Text);

                var count = (TextField)parts.ControlViews["samples.count"];
                count.SetFocus();
                count.Text = "12";
                Assert.AreEqual("Sends: Samples: 12\\n", parts.PreviewLabel.Text);
                Assert.IsFalse(parts.InfoMarkers.ContainsKey("levels"), "A chart sends nothing.");
            }
            finally
            {
                app.End(token);
            }
        });
    }

    private sealed class NullSurface : IControlSurface
    {
        public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
