using System.ComponentModel;
using System.Windows.Input;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions.Forms;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// <see cref="FormDefinitionGenerator"/>: an annotated model becomes the same <see cref="UiDefinition"/>
/// vocabulary a device panel uses — sections from <c>[Category]</c>, labels from <c>[DisplayName]</c>,
/// widgets inferred from property types or declared by <see cref="FormFieldAttribute"/>, and
/// visibility conditions from <see cref="FormSectionAttribute"/>/<see cref="FormFieldAttribute.VisibleWhen"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class FormDefinitionGeneratorTests
{
    public enum Mode
    {
        Off,
        Slow,
        Fast,
    }

    /// <summary>No <see cref="FormFieldAttribute"/> anywhere: every browsable, editable property becomes a field (how <c>CliOptions</c> generates).</summary>
    [DisplayName("Plain settings")]
    public sealed class PlainSettings
    {
        [Category("Link")]
        [DisplayName("Host name")]
        [System.ComponentModel.Description("Where to connect.")]
        public string? Host { get; set; } = "localhost";

        [Category("Link")]
        public int RetryCount { get; set; } = 3;

        [Category("Link")]
        public double Gain { get; set; } = 1.5;

        [Category("Options")]
        public bool Verbose { get; set; } = true;

        [Category("Options")]
        public Mode Mode { get; set; } = Mode.Slow;

        [Browsable(false)]
        public string Hidden { get; set; } = "x";

        public string ReadOnlyThing => "not editable";

        public string[] Tags { get; set; } = ["a", "b"];

        public object? Unsupported { get; set; }
    }

    [FormSection("Advanced", Order = 0, Label = "Advanced settings", VisibleWhen = nameof(ShowAdvanced))]
    [FormSection("Basics", Order = -1, Label = "")]
    public sealed class AnnotatedModel
    {
        public IReadOnlyList<string> Colors { get; } = ["red", "green"];

        [Category("Basics")]
        [FormField(Order = 1)]
        public bool ShowAdvanced { get; set; }

        [Category("Basics")]
        [FormField(Order = 0, OptionsFrom = nameof(Colors), ChoiceStyle = ChoiceStyle.RadioGroup)]
        public string Color { get; set; } = "green";

        [Category("Advanced")]
        [FormField(ValueKind = ValueKind.Integer, Minimum = 1, Maximum = 10, VisibleWhen = nameof(Color), VisibleWhenValues = ["red"])]
        public string Level { get; set; } = "5";

        [Category("Advanced")]
        [DisplayName("")]
        [FormField(Kind = FormFieldKind.Indicator, Warning = true)]
        public string Note => "careful";

        [Category("Advanced")]
        [FormField]
        public ICommand? Apply { get; set; }

        [Category("Advanced")]
        [FormField(Kind = FormFieldKind.Slider, Minimum = 0, Maximum = 255, Unit = "%")]
        public double Brightness { get; set; } = 128;

        public string NotAField { get; set; } = "ignored";
    }

    [TestMethod]
    public void UnannotatedModel_GetsAFieldPerBrowsableEditableProperty_GroupedByCategory()
    {
        var definition = FormDefinitionGenerator.Generate<PlainSettings>();

        Assert.AreEqual("Plain settings", definition.Name);
        Assert.AreSequenceEqual(["Link", "Options", null], [.. definition.Sections.Select(s => s.Label)]);
        var ids = definition.Sections.SelectMany(s => s.Controls).Select(c => c.Id).ToList();
        Assert.AreSequenceEqual(["Host", "RetryCount", "Gain", "Verbose", "Mode", "Tags"], ids, "Browsable(false), read-only and unsupported-type properties are left out.");

        var host = (TextFieldControl)definition.Sections[0].Controls[0];
        Assert.AreEqual("Host name", host.Label);
        Assert.AreEqual("Where to connect.", host.Description);
        Assert.AreEqual("localhost", host.DefaultValue, "Defaults come from a new instance.");
        Assert.IsNull(host.Constraint);

        var retries = (TextFieldControl)definition.Sections[0].Controls[1];
        Assert.AreEqual("Retry count", retries.Label, "A property without [DisplayName] is humanized.");
        Assert.AreEqual(ValueKind.Integer, retries.Constraint!.Kind);
        Assert.AreEqual(ValueKind.Number, ((TextFieldControl)definition.Sections[0].Controls[2]).Constraint!.Kind);

        var verbose = (ToggleControl)definition.Sections[1].Controls[0];
        Assert.IsTrue(verbose.DefaultValue);
        var mode = (ChoiceControl)definition.Sections[1].Controls[1];
        Assert.AreSequenceEqual(["Off", "Slow", "Fast"], mode.Options);
        Assert.AreEqual("Slow", mode.DefaultValue);
        Assert.AreEqual("a,b", ((TextFieldControl)definition.Sections[2].Controls[0]).DefaultValue, "A string collection is edited as comma-separated text.");
    }

    [TestMethod]
    public void AnnotatedModel_IsOptIn_AndCarriesOrderOptionsKindsAndConditions()
    {
        var model = new AnnotatedModel();
        var definition = FormDefinitionGenerator.Generate(model, "Annotated");

        Assert.AreEqual("Annotated", definition.Name);
        Assert.AreSequenceEqual(["", "Advanced settings"], [.. definition.Sections.Select(s => s.Label)], "Sections follow FormSection.Order and take its label (empty: no header).");
        Assert.IsNull(definition.Sections[0].VisibleWhen);
        Assert.AreEqual(nameof(AnnotatedModel.ShowAdvanced), definition.Sections[1].VisibleWhen!.Id);
        Assert.IsEmpty(definition.Sections[1].VisibleWhen!.Values, "No values means 'while it's true'.");

        Assert.AreSequenceEqual(["Color", "ShowAdvanced"], [.. definition.Sections[0].Controls.Select(c => c.Id)], "Order beats declaration order; NotAField isn't annotated, so it's out.");
        var color = (ChoiceControl)definition.Sections[0].Controls[0];
        Assert.AreSequenceEqual(["red", "green"], color.Options, "OptionsFrom reads the instance's list.");
        Assert.AreEqual(ChoiceStyle.RadioGroup, color.Style);

        var advanced = definition.Sections[1].Controls;
        var level = (TextFieldControl)advanced[0];
        Assert.AreEqual(ValueKind.Integer, level.Constraint!.Kind);
        Assert.AreEqual(1, level.Constraint.Minimum);
        Assert.AreEqual(10, level.Constraint.Maximum);
        Assert.AreSequenceEqual(["red"], level.VisibleWhen!.Values);

        var note = (IndicatorControl)advanced[1];
        Assert.AreEqual(string.Empty, note.Label);
        Assert.AreEqual("careful", note.DefaultValue);
        Assert.AreEqual(IndicatorStyle.Warning, note.Style);

        Assert.IsInstanceOfType<ButtonControl>(advanced[2], "An ICommand property is a button.");
        var slider = (SliderControl)advanced[3];
        Assert.AreEqual(255, slider.Maximum);
        Assert.AreEqual(128, slider.DefaultValue);
        Assert.AreEqual("%", slider.Unit);
    }

    [TestMethod]
    public void GeneratedDefinition_RoundTripsThroughJsonAndXml()
    {
        var definition = FormDefinitionGenerator.Generate(new AnnotatedModel(), "Annotated");

        var fromJson = UiDefinitionSerializer.FromJson(UiDefinitionSerializer.ToJson(definition));
        var fromXml = UiDefinitionSerializer.FromXml(UiDefinitionSerializer.ToXml(definition));

        // Exact for JSON; XML is checked field by field (XmlSerializer reads an absent list, e.g. a
        // button's null ParameterFieldIds, back as an empty one - long-standing, not this change).
        Assert.AreEqual(UiDefinitionSerializer.ToJson(definition), UiDefinitionSerializer.ToJson(fromJson));
        foreach (var copy in new[] { fromJson, fromXml })
        {
            Assert.AreEqual(nameof(AnnotatedModel.ShowAdvanced), copy.Sections[1].VisibleWhen!.Id);
            Assert.AreSequenceEqual(["red"], copy.Sections[1].Controls[0].VisibleWhen!.Values);
            Assert.AreEqual(IndicatorStyle.Warning, ((IndicatorControl)copy.Sections[1].Controls[1]).Style);
        }
    }

    [TestMethod]
    public void ChoiceStyle_IsWrittenByName_AndStillReadsAsANumber()
    {
        var json = UiDefinitionSerializer.ToJson(new UiDefinition
        {
            Name = "x",
            Sections = [new UiSection { Controls = [new ChoiceControl { Id = "c", Label = "C", Style = ChoiceStyle.CheckList, Options = ["a"] }] }],
        });

        Assert.Contains("\"CheckList\"", json);
        var legacy = UiDefinitionSerializer.FromJson("""{ "name": "x", "sections": [ { "controls": [ { "kind": "choice", "id": "c", "label": "C", "style": 1 } ] } ] }""");
        Assert.AreEqual(ChoiceStyle.RadioGroup, ((ChoiceControl)legacy.Sections[0].Controls[0]).Style);
    }

    [TestMethod]
    [DataRow("DataBits", "Data bits")]
    [DataRow("ListPorts", "List ports")]
    [DataRow("USBDevice", "USB device")]
    [DataRow("Host", "Host")]
    public void Humanize_SplitsPascalCase(string name, string expected) =>
        Assert.AreEqual(expected, FormDefinitionGenerator.Humanize(name));
}
