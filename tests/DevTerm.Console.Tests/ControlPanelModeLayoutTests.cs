using DevTerm.Core.Control;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI control panel's layout and feedback features, driven headlessly the same way
/// <see cref="ControlPanelModeTests"/> is: collapsible sections that reflow, one aligned control
/// column per section, the definition's description as a bottom "Notes" section, the "Sends: ..."
/// footer preview (via <see cref="ICommandPreview"/>) with its <c>(i)</c> markers, and
/// <see cref="ValueValidator"/>-backed rejection of invalid input.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ControlPanelModeLayoutTests
{
    private sealed class PreviewingSurface : IControlSurface, ICommandPreview
    {
        public List<(string CommandId, string? Value)> Invocations { get; } = [];

        public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
        {
            Invocations.Add((commandId, value));
            return Task.CompletedTask;
        }

        // Every command sends something except "silent" — a state-only setter.
        public string? PreviewCommand(string commandId, string? value) =>
            commandId == "silent" ? null : $"{commandId}={value ?? "<none>"}\\n";
    }

    private sealed class PlainSurface : IControlSurface
    {
        public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static UiDefinition BuildDefinition() => new()
    {
        Name = "Layout Device",
        Description = "Some operational notes about this device that are long enough to need wrapping onto more than one line of the panel when rendered at the default headless width.",
        Sections =
        [
            new UiSection
            {
                Label = "Outputs",
                Controls =
                [
                    new ButtonControl { Id = "reset", Label = "Reset" },
                    new ToggleControl { Id = "toggle1", Label = "A much longer toggle label" },
                    new SliderControl { Id = "slider1", Label = "Slider", Minimum = 0, Maximum = 255, DefaultValue = 10 },
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
        ],
    };

    private static void RunHeadless(IControlSurface surface, Action<IApplication, ControlPanelWindowParts> body) =>
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ControlPanelMode.BuildWindow(app, BuildDefinition(), surface, null, "Layout Panel");
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

    private static void Accept(View view) => view.InvokeCommand(Command.Accept);

    [TestMethod]
    public void SectionHeader_TogglesItsSection_AndReflowsTheSectionsBelow()
    {
        RunHeadless(new PlainSurface(), (app, parts) =>
        {
            var outputs = parts.SectionHeaders["Outputs"];
            var inputs = parts.SectionHeaders["Inputs"];
            Assert.AreEqual("[-] Outputs", outputs.Text);
            var expandedInputsY = inputs.Frame.Y;

            Accept(outputs);
            app.LayoutAndDraw(true);

            Assert.AreEqual("[+] Outputs", outputs.Text);
            Assert.IsFalse(parts.SectionBodies["Outputs"].Visible);
            Assert.AreEqual(outputs.Frame.Y + 2, inputs.Frame.Y, "A collapsed section leaves only its header and one blank row — no gap.");
            Assert.IsLessThan(expandedInputsY, inputs.Frame.Y);
            Assert.DoesNotContain("Reset:", TuiTestRunner.DumpBuffer());

            Accept(outputs);
            app.LayoutAndDraw(true);

            Assert.AreEqual("[-] Outputs", outputs.Text);
            Assert.IsTrue(parts.SectionBodies["Outputs"].Visible);
            Assert.AreEqual(expandedInputsY, inputs.Frame.Y);
        });
    }

    [TestMethod]
    public void SectionHeader_IsFocusable_SoTheKeyboardCanReachIt()
    {
        RunHeadless(new PlainSurface(), (app, parts) =>
        {
            var header = parts.SectionHeaders["Inputs"];
            Assert.IsTrue(header.CanFocus);

            header.SetFocus();

            Assert.IsTrue(header.HasFocus);
        });
    }

    [TestMethod]
    public void Controls_InOneSection_StartInTheSameColumn_PastTheLongestLabel()
    {
        RunHeadless(new PlainSurface(), (app, parts) =>
        {
            var xs = new[] { "reset", "toggle1", "slider1", "silent", "channel", "send" }
                .Select(id => parts.ControlViews[id].Frame.X)
                .Distinct()
                .ToList();

            Assert.HasCount(1, xs, "Every control in the section should share one column.");
            Assert.AreEqual("A much longer toggle label:".Length + 1, xs[0]);
        });
    }

    [TestMethod]
    public void Description_RendersAsAWrappedNotesSection_AfterEveryOtherSection()
    {
        RunHeadless(new PlainSurface(), (app, parts) =>
        {
            var notes = parts.SectionHeaders[ControlPanelMode.NotesSectionLabel];
            Assert.AreEqual("[-] Notes", notes.Text);
            Assert.IsGreaterThan(parts.SectionHeaders["Inputs"].Frame.Y, notes.Frame.Y);
            Assert.IsGreaterThan(1, parts.SectionBodies[ControlPanelMode.NotesSectionLabel].Frame.Height, "Long notes wrap onto more than one row.");

            var dump = TuiTestRunner.DumpBuffer();
            Assert.Contains("Some operational notes", dump);
            Assert.IsGreaterThan(dump.IndexOf("Indicator:", StringComparison.Ordinal), dump.IndexOf("Some operational notes", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void WordWrap_BreaksAtSpacesAndKeepsExplicitLineBreaks()
    {
        Assert.AreSequenceEqual(new[] { "one two", "three", "four" }, ControlPanelMode.WordWrap("one two three\nfour", 8));
        Assert.AreSequenceEqual(new[] { "abcd", "ef" }, ControlPanelMode.WordWrap("abcdef", 4));
    }

    [TestMethod]
    public void InfoMarkers_OnlyOnControlsThatSendSomething()
    {
        RunHeadless(new PreviewingSurface(), (_, parts) =>
        {
            Assert.IsTrue(parts.InfoMarkers.ContainsKey("reset"));
            Assert.IsTrue(parts.InfoMarkers.ContainsKey("toggle1"));
            Assert.IsTrue(parts.InfoMarkers.ContainsKey("slider1"));
            Assert.IsTrue(parts.InfoMarkers.ContainsKey("send"));
            Assert.AreEqual("(i)", parts.InfoMarkers["reset"].Text);

            Assert.IsFalse(parts.InfoMarkers.ContainsKey("silent"), "A state-only command previews as null — no marker.");
            Assert.IsFalse(parts.InfoMarkers.ContainsKey("channel"), "A parameter field's marker is on its button, not the field.");
            Assert.IsFalse(parts.InfoMarkers.ContainsKey("indicator1"));
        });
    }

    [TestMethod]
    public void InfoMarkers_NoneWhenTheSurfaceHasNoPreview()
    {
        RunHeadless(new PlainSurface(), (_, parts) => Assert.IsEmpty(parts.InfoMarkers));
    }

    [TestMethod]
    public void Focus_ShowsWhatTheControlWouldSend_AndUpdatesAsItsValueChanges()
    {
        RunHeadless(new PreviewingSurface(), (_, parts) =>
        {
            Assert.AreEqual(string.Empty, parts.PreviewLabel.Text);

            var slider = (TextField)parts.ControlViews["slider1"];
            slider.SetFocus();
            Assert.AreEqual("Sends: slider1=10\\n", parts.PreviewLabel.Text);

            slider.Text = "999";
            Assert.AreEqual("Sends: slider1=255\\n", parts.PreviewLabel.Text, "The preview is of the value that would actually be sent (clamped).");

            slider.Text = "abc";
            Assert.StartsWith("Won't send:", parts.PreviewLabel.Text);

            parts.ControlViews["reset"].SetFocus();
            Assert.AreEqual("Sends: reset=<none>\\n", parts.PreviewLabel.Text);

            parts.ControlViews["toggle1"].SetFocus();
            Assert.AreEqual("Sends: toggle1=1\\n", parts.PreviewLabel.Text, "A toggle previews the state toggling it would send.");

            parts.ControlViews["silent"].SetFocus();
            Assert.AreEqual(string.Empty, parts.PreviewLabel.Text);
        });
    }

    [TestMethod]
    public void Focus_OnAParameterField_ShowsItsButtonsPreviewWithTheCurrentValue()
    {
        RunHeadless(new PreviewingSurface(), (_, parts) =>
        {
            var channel = (TextField)parts.ControlViews["channel"];
            channel.SetFocus();
            Assert.AreEqual("Sends: sendCommand=1\\n", parts.PreviewLabel.Text);

            channel.Text = "3";
            Assert.AreEqual("Sends: sendCommand=3\\n", parts.PreviewLabel.Text);

            channel.Text = "9";
            Assert.AreEqual("Won't send: Channel: 9 is out of range (1 to 4).", parts.PreviewLabel.Text);
        });
    }

    [TestMethod]
    public void ConstrainedTextField_InvalidValue_IsRejectedWithAMessageAndNotSent()
    {
        var surface = new PreviewingSurface();
        RunHeadless(surface, (_, parts) =>
        {
            var channel = (TextField)parts.ControlViews["channel"];
            channel.Text = "two";
            Accept(channel);

            Assert.IsEmpty(surface.Invocations);
            Assert.AreEqual("Channel: 'two' is not a whole number. Not sent.", parts.MessageLabel.Text);

            channel.Text = " 3.0 ";
            Accept(channel);

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("channel", "3"), surface.Invocations[0]);
            Assert.AreEqual("3", channel.Text, "A valid number is normalized in place.");
            Assert.AreEqual(string.Empty, parts.MessageLabel.Text);
        });
    }

    [TestMethod]
    public void ParameterButton_WithAnInvalidField_IsNotSent()
    {
        var surface = new PreviewingSurface();
        RunHeadless(surface, (_, parts) =>
        {
            ((TextField)parts.ControlViews["channel"]).Text = "0";
            Accept(parts.ControlViews["send"]);

            Assert.IsEmpty(surface.Invocations);
            Assert.AreEqual("Channel: 0 is out of range (1 to 4). Not sent.", parts.MessageLabel.Text);

            ((TextField)parts.ControlViews["channel"]).Text = "4";
            Accept(parts.ControlViews["send"]);

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("sendCommand", "4"), surface.Invocations[0]);
        });
    }

    [TestMethod]
    public void Slider_UnparsableValue_IsRejectedRatherThanReplacedByTheDefault()
    {
        var surface = new PreviewingSurface();
        RunHeadless(surface, (_, parts) =>
        {
            var slider = (TextField)parts.ControlViews["slider1"];
            slider.Text = "lots";
            Accept(slider);

            Assert.IsEmpty(surface.Invocations);
            Assert.AreEqual("Slider: 'lots' is not a number. Not sent.", parts.MessageLabel.Text);
        });
    }

    [TestMethod]
    public void ScpiPanel_FocusingAQueryButton_ShowsTheExactCommandWithItsTerminator()
    {
        var session = new DevTerm.Core.Sessions.Session(new FakeTransport(), new DevTerm.Core.Presenters.Pipeline([]));
        var profile = DevTerm.Devices.Scpi.ScpiProfileCatalog.Generic;
        var surface = new DevTerm.Devices.Scpi.ScpiControlSurface(session, profile, tracker: null);

        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ControlPanelMode.BuildWindow(app, DevTerm.Devices.Scpi.ScpiUiDefinitionBuilder.Build(profile), surface, null, "SCPI");
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                parts.ControlViews["idn"].SetFocus();
                Assert.AreEqual("Sends: *IDN?\\n", parts.PreviewLabel.Text);

                var custom = (TextField)parts.ControlViews[DevTerm.Devices.Scpi.ScpiUiDefinitionBuilder.CustomCommandFieldId];
                custom.SetFocus();
                custom.Text = "MEAS:VOLT:DC? DEF";
                Assert.AreEqual("Sends: MEAS:VOLT:DC? DEF\\n", parts.PreviewLabel.Text);
                Assert.IsTrue(parts.InfoMarkers.ContainsKey(DevTerm.Devices.Scpi.ScpiControlSurface.SendCustomCommandId));
            }
            finally
            {
                app.End(token);
            }
        });
    }
}
