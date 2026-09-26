using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions.Forms;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// <see cref="FormBinding"/> (the one set of conversion/visibility rules both form renderers share)
/// and <see cref="UiCondition"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class FormBindingTests
{
    public enum Speed
    {
        Low,
        High,
    }

    public sealed class Plain
    {
        public string Name { get; set; } = string.Empty;

        public string TextNumber { get; set; } = "1";

        public int Count { get; set; }

        public int? Optional { get; set; } = 4;

        public Speed Speed { get; set; }

        public bool Enabled { get; set; }

        public string Csv { get; set; } = string.Empty;

        public string[] Items { get; set; } = [];

        public List<string> ListItems { get; set; } = [];

        public ICommand? Run { get; set; }
    }

    public sealed class Notifying : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnChanged();
                OnChanged(nameof(Upper));
            }
        }

        public string Upper => _name.ToUpperInvariant();

        private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class Command : ICommand
    {
        public int Runs { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Runs++;
    }

    private static TextFieldControl Field(string id, ValueKind kind = ValueKind.Text, double? min = null, double? max = null) => new()
    {
        Id = id,
        Label = id,
        Constraint = kind == ValueKind.Text && min is null ? null : new ValueConstraint { Kind = kind, Minimum = min, Maximum = max },
    };

    [TestMethod]
    public void BlankOptionalNumber_IsValid_AndClearsIt_ButABlankRequiredNumberIsNot()
    {
        // A form field generated for an int? gets an Integer constraint, which on its own rejects a
        // blank - so an optional number (the manifest editor's text-field Max length) showed
        // "! '' is not a whole number." as soon as it was selected, and couldn't be cleared.
        var model = new Plain();
        using var binding = new FormBinding(model);
        var optional = Field(nameof(Plain.Optional), ValueKind.Integer, min: 0);

        Assert.IsTrue(binding.Validate(optional, "  ").IsValid);
        Assert.IsTrue(binding.SetText(optional, string.Empty).IsValid);
        Assert.IsNull(model.Optional);

        Assert.IsFalse(binding.Validate(optional, "x").IsValid, "Anything else still has to be a whole number.");
        Assert.IsFalse(binding.Validate(Field(nameof(Plain.Count), ValueKind.Integer), string.Empty).IsValid, "A plain int can't be blank.");
    }

    [TestMethod]
    public void SetText_ConvertsToThePropertyType_AndOnlyWritesAValueThatConverts()
    {
        var model = new Plain();
        using var binding = new FormBinding(model);

        Assert.IsTrue(binding.SetText(Field(nameof(Plain.Count), ValueKind.Integer), " 12 ").IsValid);
        Assert.AreEqual(12, model.Count);

        var rejected = binding.SetText(Field(nameof(Plain.Count), ValueKind.Integer), "abc");
        Assert.IsFalse(rejected.IsValid);
        Assert.AreEqual(12, model.Count, "An int property keeps its last good value.");

        binding.SetText(Field(nameof(Plain.Optional)), string.Empty);
        Assert.IsNull(model.Optional, "Blank clears a nullable.");

        binding.SetText(new ChoiceControl { Id = nameof(Plain.Speed), Label = "Speed" }, "high");
        Assert.AreEqual(Speed.High, model.Speed);
        Assert.AreEqual("High", binding.GetText(nameof(Plain.Speed)));

        binding.SetText(Field(nameof(Plain.Items)), "a, b ,,c");
        Assert.AreSequenceEqual(["a", "b", "c"], model.Items);
        Assert.AreEqual("a,b,c", binding.GetText(nameof(Plain.Items)));
    }

    [TestMethod]
    public void SetText_OnAStringProperty_KeepsWhatWasTyped_EvenWhenItFailsItsConstraint()
    {
        var model = new Plain();
        using var binding = new FormBinding(model);

        var result = binding.SetText(Field(nameof(Plain.TextNumber), ValueKind.Integer, 1, 10), "12x");

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("12x", model.TextNumber, "A string-typed view model validates later; the keystroke isn't lost.");
        Assert.AreEqual("11", ((Func<string>)(() => { binding.SetText(Field(nameof(Plain.TextNumber)), "11"); return model.TextNumber; }))());
    }

    [TestMethod]
    public void Selection_ReadsAndWritesACommaSeparatedStringOrACollection()
    {
        var model = new Plain();
        using var binding = new FormBinding(model);

        binding.SetSelection(nameof(Plain.Csv), ["ascii", "hex"]);
        binding.SetSelection(nameof(Plain.Items), ["x"]);
        binding.SetSelection(nameof(Plain.ListItems), ["y", "z"]);

        Assert.AreEqual("ascii,hex", model.Csv);
        Assert.AreSequenceEqual(["ascii", "hex"], binding.GetSelection(nameof(Plain.Csv)));
        Assert.AreSequenceEqual(["x"], model.Items);
        Assert.AreSequenceEqual(["y", "z"], binding.GetSelection(nameof(Plain.ListItems)));
    }

    [TestMethod]
    public void Changed_IsRaisedByTheBindingForAPlainModel_AndRelayedFromANotifyingOne()
    {
        var plain = new Plain();
        using var plainBinding = new FormBinding(plain);
        var raised = new List<string?>();
        plainBinding.Changed += (_, name) => raised.Add(name);

        plainBinding.SetBool(nameof(Plain.Enabled), true);
        plainBinding.SetBool(nameof(Plain.Enabled), true);

        Assert.AreSequenceEqual([nameof(Plain.Enabled)], raised, "Once per actual change, for a model that can't say so itself.");

        var notifying = new Notifying();
        using var notifyingBinding = new FormBinding(notifying);
        var relayed = new List<string?>();
        notifyingBinding.Changed += (_, name) => relayed.Add(name);

        notifyingBinding.SetText(Field(nameof(Notifying.Name)), "abc");

        Assert.AreSequenceEqual([nameof(Notifying.Name), nameof(Notifying.Upper)], relayed, "The model's own notifications, not a duplicate from the binding.");
        Assert.AreEqual("ABC", notifyingBinding.GetText(nameof(Notifying.Upper)));
        Assert.IsTrue(notifyingBinding.IsReadOnly(nameof(Notifying.Upper)));
    }

    [TestMethod]
    public void Invoke_RunsACommandProperty()
    {
        var command = new Command();
        using var binding = new FormBinding(new Plain { Run = command });

        Assert.IsTrue(binding.Invoke(nameof(Plain.Run)));
        Assert.IsFalse(binding.Invoke(nameof(Plain.Name)), "Not a command.");
        Assert.AreEqual(1, command.Runs);
    }

    [TestMethod]
    public void UiCondition_MatchesValuesCaseInsensitively_TruthWithNoValues_AndAnyItemOfACollection()
    {
        var transport = new UiCondition { Id = "Transport", Values = ["hid", "usbtmc"] };
        Assert.IsTrue(transport.IsMetBy("HID"));
        Assert.IsFalse(transport.IsMetBy("serial"));
        Assert.IsFalse(transport.IsMetBy(null));

        var flag = new UiCondition { Id = "IsSerial" };
        Assert.IsTrue(flag.IsMetBy(true));
        Assert.IsFalse(flag.IsMetBy(false));
        Assert.IsTrue(flag.IsMetBy("true"));

        var presenters = new UiCondition { Id = "Presenters", Values = ["scpi"] };
        Assert.IsTrue(presenters.IsMetBy(new[] { "ascii", "scpi" }));
        Assert.IsFalse(presenters.IsMetBy(new List<string> { "ascii" }));

        Assert.IsTrue(new UiCondition { Id = "Speed", Values = ["High"] }.IsMetBy(Speed.High), "An enum compares by name.");
    }

    [TestMethod]
    public void IsVisible_EvaluatesAConditionAgainstTheModel()
    {
        using var binding = new FormBinding(new Plain { Enabled = true, Speed = Speed.Low });

        Assert.IsTrue(binding.IsVisible(null));
        Assert.IsTrue(binding.IsVisible(new UiCondition { Id = nameof(Plain.Enabled) }));
        Assert.IsFalse(binding.IsVisible(new UiCondition { Id = nameof(Plain.Speed), Values = ["High"] }));
    }
}
