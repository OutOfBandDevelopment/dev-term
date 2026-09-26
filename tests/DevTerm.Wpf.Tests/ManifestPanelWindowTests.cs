using System.IO;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.DeviceManifests;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The WPF side of manifest panels and the chart controls: a device manifest's panel
/// (<see cref="ManifestPanel"/>) rendered by <see cref="ControlPanelWindow"/> with live bar graph,
/// strip chart and vector displays fed over a real <see cref="Session"/>; the Device Manifest picker
/// loading (or refusing) a choice without a modal; and sections reopening as they were left.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ManifestPanelWindowTests
{
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(5);

    public required TestContext TestContext { get; set; }

    private static DeviceManifest LoadDemo() =>
        DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));

    [TestMethod]
    public void ManifestPanel_ChartsFollowStreamedSamples_AndButtonsSendTheTemplates()
    {
        StaTestRunner.Run(async () =>
        {
            var manifest = LoadDemo();
            SectionExpansionState.Forget(manifest.Name);
            var transport = new FakeTransport();
            var session = new Session(transport, new Pipeline([]));
            await session.OpenAsync(TestContext.CancellationToken);
            using var panel = ManifestPanel.Attach(session, manifest);
            var window = new ControlPanelWindow(panel.Definition, panel.Surface, panel.Presenter) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            Assert.HasCount(5, window.Displays);
            Assert.AreEqual(string.Empty, window.StatusText.Text, "The manifest presenter is structured, so no 'Not decoding' warning.");
            Assert.AreEqual("Sends: Samples: 40\\n", window.PreviewFor("samples.send"));

            ((System.Windows.Controls.Button)window.ControlViews["measure"]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.IsTrue(StaTestRunner.PumpUntil(() => transport.WrittenPayloads.Count == 1, _pumpTimeout));
            Assert.AreEqual("MEAS?\n", Encoding.ASCII.GetString(transport.WrittenPayloads[0]));

            var lines = string.Concat(Enumerable.Range(0, 6).Select(i => DevTerm.Transports.Loopback.LoopbackGenerators.SensorSample(i) + "\n"));
            await transport.PushIncomingAsync(Encoding.ASCII.GetBytes(lines));

            var strip = (StripChartState)window.Displays["history"].State;
            Assert.IsTrue(StaTestRunner.PumpUntil(() => strip.SamplesOf("chA").Count == 6, _pumpTimeout), "Every sample line reaches the strip chart.");
            Assert.AreEqual(DevTerm.Transports.Loopback.LoopbackGenerators.SensorSample(0), window.IndicatorLabels["measure.reply"].Text, "The first line is Measure's reply.");
            Assert.IsNotNull(((BarGraphState)window.Displays["levels"].State).ValueOf("chA"), "The bars show a live value.");
            Assert.IsTrue(((VectorState)window.Displays["polar"].State).HasPoint);

            window.Close();
            await session.CloseAsync(TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public void CollapsedSection_StaysCollapsedWhenThePanelReopens()
    {
        StaTestRunner.Run(async () =>
        {
            var definition = new UiDefinition
            {
                Name = "WPF Remembered Sections",
                Sections = [new UiSection { Label = "One", Controls = [new ButtonControl { Id = "a", Label = "A" }] }],
            };
            SectionExpansionState.Forget(definition.Name);
            try
            {
                var first = new ControlPanelWindow(definition, new NullSurface(), null);
                StaTestRunner.DoEvents();
                Assert.IsTrue(first.SectionExpanders["One"].IsExpanded);
                first.SectionExpanders["One"].IsExpanded = false;
                first.Close();

                var second = new ControlPanelWindow(definition, new NullSurface(), null);
                StaTestRunner.DoEvents();
                Assert.IsFalse(second.SectionExpanders["One"].IsExpanded, "Reopened the way it was left.");
                second.Close();
            }
            finally
            {
                SectionExpansionState.Forget(definition.Name);
            }

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Picker_LoadsTheSelectedManifest_OrReportsWhyNotInline()
    {
        StaTestRunner.Run(async () =>
        {
            var entries = InstalledManifests.Discover();
            var picker = new ManifestPickerWindow(entries);
            StaTestRunner.DoEvents();

            picker.ManifestList.SelectedItem = entries.First(e => e.Name == "Loopback Sensor Demo");
            Assert.IsTrue(picker.TryLoadChoice());
            Assert.AreEqual("Loopback Sensor Demo", picker.Chosen!.Name);

            picker.PathBox.Text = Path.Combine(Path.GetTempPath(), "devterm-no-such-manifest", "device.json");
            Assert.IsFalse(picker.TryLoadChoice(), "A typed path wins over the list selection — and this one doesn't exist.");
            Assert.StartsWith("Couldn't load", picker.ErrorText.Text);

            picker.Close();
            await Task.CompletedTask;
        });
    }

    private sealed class NullSurface : DevTerm.Core.Control.IControlSurface
    {
        public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
