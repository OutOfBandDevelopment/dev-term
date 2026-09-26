using System.Windows.Controls;
using DevTerm.Core.Control;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The WPF control panel's layout and feedback features, driven the same way
/// <see cref="ControlPanelWindowTests"/> is (<see cref="StaTestRunner.Run"/>, never <c>Show()</c>, a
/// <see cref="StaTestRunner.DoEvents"/> pump after construction): one <see cref="Expander"/> per
/// labeled section, the description as a bottom "Notes" section, the "ⓘ" command preview (via
/// <see cref="ICommandPreview"/>), and <see cref="ValueValidator"/>-backed rejection of invalid input
/// in the status line.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ControlPanelWindowLayoutTests
{
    private sealed class PreviewingSurface : IControlSurface, ICommandPreview
    {
        public List<(string CommandId, string? Value)> Invocations { get; } = [];

        public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
        {
            Invocations.Add((commandId, value));
            return Task.CompletedTask;
        }

        public string? PreviewCommand(string commandId, string? value) =>
            commandId == "silent" ? null : $"{commandId}={value ?? "<none>"}\\n";
    }

    private static UiDefinition BuildDefinition() => new()
    {
        Name = "Layout Device",
        Description = "Operational notes for this device.",
        Sections =
        [
            new UiSection
            {
                Label = "Outputs",
                Controls =
                [
                    new ButtonControl { Id = "reset", Label = "Reset" },
                    new ToggleControl { Id = "toggle1", Label = "Toggle" },
                    new SliderControl { Id = "slider1", Label = "Slider", Minimum = 0, Maximum = 255, DefaultValue = 10 },
                    new NumericControl { Id = "numeric1", Label = "Numeric", Minimum = 0, Maximum = 100, DefaultValue = 5 },
                    new ChoiceControl { Id = "silent", Label = "Silent", Options = ["a", "b"], DefaultValue = "a" },
                    new TextFieldControl
                    {
                        Id = "channel",
                        Label = "Channel",
                        DefaultValue = "1",
                        Constraint = new ValueConstraint { Kind = ValueKind.Integer, Minimum = 1, Maximum = 4 },
                    },
                    new ButtonControl { Id = "send", Label = "Send", CommandId = "sendCommand", ParameterFieldIds = ["channel"] },
                ],
            },
            new UiSection
            {
                Label = "Inputs",
                Controls = [new IndicatorControl { Id = "indicator1", Label = "Indicator", DefaultValue = "0" }],
            },
            new UiSection { Controls = [new ButtonControl { Id = "apply", Label = "Apply" }] },
        ],
    };

    private static ControlPanelWindow Create(IControlSurface surface)
    {
        // Expand/collapse state is remembered per definition for the process (a test collapses a
        // section below) — every test here starts from all-expanded.
        DevTerm.Configuration.SectionExpansionState.Forget(BuildDefinition().Name);
        var window = new ControlPanelWindow(BuildDefinition(), surface, null) { ShowInTaskbar = false };
        StaTestRunner.DoEvents();
        return window;
    }

    private static void LoseFocus(TextBox box) => box.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.UIElement.LostFocusEvent));

    [TestMethod]
    public void EachLabeledSection_IsAnExpander_ExpandedByDefault_WithNotesLast()
    {
        StaTestRunner.Run(async () =>
        {
            var window = Create(new PreviewingSurface());

            Assert.IsTrue(window.SectionExpanders["Outputs"].IsExpanded);
            Assert.IsTrue(window.SectionExpanders["Inputs"].IsExpanded);
            Assert.IsFalse(window.SectionExpanders.ContainsKey(string.Empty), "An unlabeled section has no header to collapse by.");

            var notes = window.SectionExpanders[ControlPanelWindow.NotesSectionLabel];
            Assert.AreSame(notes, window.SectionsPanel.Children[^1], "Notes is the last section.");
            var text = (TextBlock)((Border)notes.Content).Child;
            Assert.AreEqual("Operational notes for this device.", text.Text);
            Assert.AreEqual(System.Windows.TextWrapping.Wrap, text.TextWrapping);

            window.SectionExpanders["Outputs"].IsExpanded = false;
            Assert.IsFalse(window.SectionExpanders["Outputs"].IsExpanded);

            await Task.CompletedTask;
        });
    }

    private static readonly string[] _asyncAction = ["reset", "toggle1", "slider1", "numeric1", "send", "apply"];

    [TestMethod]
    public void InfoIcons_OnlyOnControlsThatSendSomething()
    {
        StaTestRunner.Run(async () =>
        {
            var window = Create(new PreviewingSurface());

            foreach (var id in _asyncAction)
            {
                Assert.IsTrue(window.InfoIcons.ContainsKey(id), $"Expected an info icon on '{id}'.");
            }

            Assert.AreEqual(ControlPanelWindow.InfoGlyph, window.InfoIcons["reset"].Text);
            Assert.IsFalse(window.InfoIcons.ContainsKey("silent"), "A state-only command previews as null — no icon.");
            Assert.IsFalse(window.InfoIcons.ContainsKey("channel"), "A parameter field's icon is on its button.");
            Assert.IsFalse(window.InfoIcons.ContainsKey("indicator1"));

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void InfoIcons_NoneWhenTheSurfaceHasNoPreview()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ControlPanelWindow(BuildDefinition(), new NoPreviewSurface(), null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            Assert.IsEmpty(window.InfoIcons);

            await Task.CompletedTask;
        });
    }

    private sealed class NoPreviewSurface : IControlSurface
    {
        public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [TestMethod]
    public void Preview_UsesEachControlsCurrentValue()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new PreviewingSurface();
            var window = Create(surface);

            Assert.AreEqual("Sends: reset=<none>\\n", window.PreviewFor("reset"));
            Assert.AreEqual("Sends: reset=<none>\\n", window.InfoIcons["reset"].ToolTip);

            ((Slider)window.ControlViews["slider1"]).Value = 200;
            Assert.AreEqual("Sends: slider1=200\\n", window.PreviewFor("slider1"));

            ((TextBox)window.ControlViews["numeric1"]).Text = "999";
            Assert.AreEqual("Sends: numeric1=100\\n", window.PreviewFor("numeric1"), "The preview is of the value that would actually be sent (clamped).");

            Assert.AreEqual("Sends: toggle1=1\\n", window.PreviewFor("toggle1"), "A toggle previews the state toggling it would send.");

            ((TextBox)window.ControlViews["channel"]).Text = "3";
            Assert.AreEqual("Sends: sendCommand=3\\n", window.PreviewFor("send"));

            ((TextBox)window.ControlViews["channel"]).Text = "9";
            Assert.AreEqual("Won't send: Channel: 9 is out of range (1 to 4).", window.PreviewFor("send"));

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ConstrainedTextField_InvalidValue_IsReportedInTheStatusLineAndNotSent()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new PreviewingSurface();
            var window = Create(surface);
            var channel = (TextBox)window.ControlViews["channel"];

            channel.Text = "two";
            LoseFocus(channel);

            Assert.IsEmpty(surface.Invocations);
            Assert.AreEqual("Channel: 'two' is not a whole number. Not sent.", window.StatusText.Text);

            channel.Text = " 3.0 ";
            LoseFocus(channel);

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("channel", "3"), surface.Invocations[0]);
            Assert.AreEqual("3", channel.Text);
            Assert.Contains("Not decoding", window.StatusText.Text, "A valid commit restores the panel's own status line.");

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Numeric_UnparsableValue_IsRejectedRatherThanReplacedByTheDefault()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new PreviewingSurface();
            var window = Create(surface);
            var numeric = (TextBox)window.ControlViews["numeric1"];

            numeric.Text = "lots";
            LoseFocus(numeric);

            Assert.IsEmpty(surface.Invocations);
            Assert.AreEqual("Numeric: 'lots' is not a number. Not sent.", window.StatusText.Text);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ParameterButton_WithAnInvalidField_IsNotSent()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new PreviewingSurface();
            var window = Create(surface);
            var send = (Button)window.ControlViews["send"];

            ((TextBox)window.ControlViews["channel"]).Text = "0";
            send.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

            Assert.IsEmpty(surface.Invocations);
            Assert.AreEqual("Channel: 0 is out of range (1 to 4). Not sent.", window.StatusText.Text);

            ((TextBox)window.ControlViews["channel"]).Text = "4";
            send.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("sendCommand", "4"), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ScpiPanel_QueryButtonPreview_IsTheExactCommandWithItsTerminator()
    {
        StaTestRunner.Run(async () =>
        {
            var session = new DevTerm.Core.Sessions.Session(new FakeTransport(), new DevTerm.Core.Presenters.Pipeline([]));
            var profile = DevTerm.Devices.Scpi.ScpiProfileCatalog.Generic;
            var surface = new DevTerm.Devices.Scpi.ScpiControlSurface(session, profile, tracker: null);
            var window = new ControlPanelWindow(DevTerm.Devices.Scpi.ScpiUiDefinitionBuilder.Build(profile), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            Assert.AreEqual("Sends: *IDN?\\n", window.PreviewFor("idn"));

            ((TextBox)window.ControlViews[DevTerm.Devices.Scpi.ScpiUiDefinitionBuilder.CustomCommandFieldId]).Text = "MEAS:VOLT:DC? DEF";
            Assert.AreEqual("Sends: MEAS:VOLT:DC? DEF\\n", window.PreviewFor(DevTerm.Devices.Scpi.ScpiControlSurface.SendCustomCommandId));
            Assert.IsFalse(window.InfoIcons.ContainsKey(DevTerm.Devices.Scpi.ScpiUiDefinitionBuilder.CustomCommandFieldId));

            await Task.CompletedTask;
        });
    }
}
