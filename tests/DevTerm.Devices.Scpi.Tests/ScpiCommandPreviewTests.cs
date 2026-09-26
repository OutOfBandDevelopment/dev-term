using System.Text;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using Moq;

namespace DevTerm.Devices.Scpi.Tests;

/// <summary>
/// Verifies <see cref="ScpiControlSurface.PreviewCommand"/> shows exactly what
/// <see cref="ScpiControlSurface.InvokeAsync"/> puts on the wire (same template/formatting/terminator
/// path, terminator escaped visibly), without sending anything; plus <see cref="ScpiUiDefinitionBuilder"/>'s
/// handling of <see cref="ScpiParameterDefinition.Control"/> — a widget chosen independently of the
/// parameter's kind. No real instrument involved, so UNIT.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Scpi)]
[TestClass]
public sealed class ScpiCommandPreviewTests
{
    public TestContext TestContext { get; set; } = null!;

    private static ScpiInstrumentProfile BuildProfile(string terminator) => new()
    {
        Name = "Test Instrument",
        Terminator = terminator,
        Commands =
        [
            new ScpiCommandDefinition { Id = "meas", Label = "Measure DC Voltage", Template = "MEAS:VOLT:DC? DEF", IsQuery = true },
            new ScpiCommandDefinition
            {
                Id = "vset",
                Label = "Set Voltage",
                Template = "VSET1:{Voltage}",
                Parameters = [new ScpiParameterDefinition { Name = "Voltage", Kind = ScpiParameterKind.Numeric, Minimum = 0, Maximum = 60, DecimalPlaces = 2, IntegerDigits = 2, DefaultValue = "0" }],
            },
        ],
    };

    private static (ScpiControlSurface Surface, Mock<ITransport> Transport) CreateSurface(string terminator)
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        return (new ScpiControlSurface(session, BuildProfile(terminator), tracker: null), transport);
    }

    private static string SentText(Mock<ITransport> transport) =>
        Encoding.ASCII.GetString(((ReadOnlyMemory<byte>)transport.Invocations.Single(i => i.Method.Name == nameof(ITransport.WriteAsync)).Arguments[0]!).ToArray());

    [TestMethod]
    public void PreviewCommand_Query_ShowsTemplateWithTheTerminatorEscaped()
    {
        var (surface, _) = CreateSurface("\n");

        Assert.AreEqual("MEAS:VOLT:DC? DEF\\n", surface.PreviewCommand("meas", null));
    }

    [TestMethod]
    public async Task PreviewCommand_MatchesExactlyWhatInvokeAsyncSends_AndSendsNothingItself()
    {
        var (surface, transport) = CreateSurface("\r\n");

        var preview = surface.PreviewCommand("vset", "5");
        Assert.IsFalse(transport.Invocations.Any(i => i.Method.Name == nameof(ITransport.WriteAsync)), "A preview must never send.");

        await surface.InvokeAsync("vset", "5", TestContext.CancellationToken);

        Assert.AreEqual("VSET1:05.00\\r\\n", preview);
        Assert.AreEqual(CommandPreviewFormat.EscapeControlCharacters(SentText(transport)), preview);
    }

    [TestMethod]
    public void PreviewCommand_CustomCommand_IsTheTypedTextPlusTerminator()
    {
        var (surface, _) = CreateSurface("\n");

        Assert.AreEqual("*RST\\n", surface.PreviewCommand(ScpiControlSurface.SendCustomCommandId, "*RST"));
    }

    [TestMethod]
    public void PreviewCommand_ParameterFieldIdOrUnknownCommand_IsNull()
    {
        var (surface, _) = CreateSurface("\n");

        Assert.IsNull(surface.PreviewCommand("vset.Voltage", "5"));
        Assert.IsNull(surface.PreviewCommand(ScpiUiDefinitionBuilder.CustomCommandFieldId, "*IDN?"));
        Assert.IsNull(surface.PreviewCommand("nope", null));
    }

    [TestMethod]
    public void PreviewCommand_NoTerminator_ShowsJustTheCommand()
    {
        var (surface, _) = CreateSurface(string.Empty);

        Assert.AreEqual("VSET1:12.50", surface.PreviewCommand("vset", "12.5"));
    }

    private static ScpiInstrumentProfile ProfileWithParameter(ScpiParameterDefinition parameter) => new()
    {
        Name = "Hint Test",
        Commands = [new ScpiCommandDefinition { Id = "cmd", Label = "Command", Template = "CMD {P}", Parameters = [parameter] }],
    };

    private static UiControl ParameterControl(ScpiParameterDefinition parameter) =>
        ScpiUiDefinitionBuilder.Build(ProfileWithParameter(parameter)).Sections[0].Controls.Single(c => c.Id == "cmd.P");

    [TestMethod]
    public void Build_NumericParameterWithTextHint_IsATextFieldWithANumberConstraint()
    {
        var control = ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Numeric, Minimum = 1, Maximum = 5, Unit = "V", DecimalPlaces = 2, Control = ScpiParameterControl.Text });

        var textField = Assert.IsInstanceOfType<TextFieldControl>(control);
        Assert.IsNotNull(textField.Constraint);
        Assert.AreEqual(ValueKind.Number, textField.Constraint.Kind);
        Assert.AreEqual(1d, textField.Constraint.Minimum);
        Assert.AreEqual(5d, textField.Constraint.Maximum);
        Assert.IsFalse(textField.Constraint.ClampToRange, "A typed out-of-range value is rejected, not clamped.");
        Assert.AreEqual("P (V)", textField.Label);
    }

    [TestMethod]
    public void Build_NumericParameterWithZeroDecimalPlacesAndTextHint_IsAnIntegerConstraint()
    {
        var control = ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Numeric, Minimum = 1, Maximum = 5, DecimalPlaces = 0, Control = ScpiParameterControl.Text });

        Assert.AreEqual(ValueKind.Integer, ((TextFieldControl)control).Constraint!.Kind);
    }

    [TestMethod]
    public void Build_NumericParameterWithSliderHint_IsASlider()
    {
        var control = ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Numeric, Minimum = 0, Maximum = 3, DecimalPlaces = 1, DefaultValue = "1.5", Control = ScpiParameterControl.Slider });

        var slider = Assert.IsInstanceOfType<SliderControl>(control);
        Assert.AreEqual(0d, slider.Minimum);
        Assert.AreEqual(3d, slider.Maximum);
        Assert.AreEqual(0.1, slider.Step, 1e-9);
        Assert.AreEqual(1.5, slider.DefaultValue);
    }

    [TestMethod]
    public void Build_NoHint_KeepsTheKindsOwnWidget()
    {
        Assert.IsInstanceOfType<NumericControl>(ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Numeric, Maximum = 5 }));
        Assert.IsInstanceOfType<ChoiceControl>(ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Choice, Options = ["A"] }));
        var text = Assert.IsInstanceOfType<TextFieldControl>(ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Text }));
        Assert.IsNull(text.Constraint);
    }

    [TestMethod]
    public void Build_HintThatDoesNotFitTheKind_FallsBackToTheKindsWidget()
    {
        // A slider needs numeric bounds; a choice needs options.
        Assert.IsInstanceOfType<TextFieldControl>(ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Text, Control = ScpiParameterControl.Slider }));
        Assert.IsInstanceOfType<NumericControl>(ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Numeric, Maximum = 5, Control = ScpiParameterControl.Choice }));
    }

    [TestMethod]
    public void Build_TextParameterWithChoiceHint_IsAChoice()
    {
        Assert.IsInstanceOfType<ChoiceControl>(ParameterControl(new ScpiParameterDefinition { Name = "P", Kind = ScpiParameterKind.Text, Options = ["A", "B"], Control = ScpiParameterControl.Choice }));
    }

    [TestMethod]
    public void Load_ProfileJsonWithAControlHint_ParsesIt()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "DevTermScpiTests_" + Guid.NewGuid().ToString("N"));
        var profilesDirectory = Path.Combine(baseDirectory, "Profiles");
        Directory.CreateDirectory(profilesDirectory);
        try
        {
            File.WriteAllText(Path.Combine(profilesDirectory, "hint.json"), """
                {
                    "Name": "Hint Instrument",
                    "Commands": [
                        {
                            "Id": "freq", "Label": "Set Frequency", "Template": "FREQ {Frequency}",
                            "Parameters": [ { "Name": "Frequency", "Kind": "Numeric", "Control": "text", "Minimum": 1, "Maximum": 100 } ]
                        }
                    ]
                }
                """);

            var profile = ScpiProfileCatalog.Load(baseDirectory).Single(p => p.Name == "Hint Instrument");

            Assert.AreEqual(ScpiParameterControl.Text, profile.Commands[0].Parameters[0].Control);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }
}
