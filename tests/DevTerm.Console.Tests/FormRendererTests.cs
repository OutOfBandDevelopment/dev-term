using System.ComponentModel;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using DevTerm.UiDefinitions.Forms;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI's generic form renderer (<see cref="FormRenderer"/>) against a small plain model — each
/// widget kind writes its property, a model change reaches the widget, and a visibility condition
/// hides a row without leaving a gap. The Connection Editor's own use is covered by
/// <c>ConfigureModeTests</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class FormRendererTests
{
    [FormSection("Main", Order = 0)]
    [FormSection("Extra", Order = 1, VisibleWhen = nameof(ShowExtra))]
    public sealed class Model
    {
        public IReadOnlyList<string> Sizes { get; } = ["small", "medium", "large"];

        public IReadOnlyList<string> ManyNames { get; } = [.. Enumerable.Range(1, 30).Select(i => $"option number {i}")];

        public IReadOnlyList<string> Wide { get; } = ["alpha-option", "beta-option", "gamma-option", "delta-option", "epsilon-option", "zeta-option"];

        [Category("Main")]
        [DisplayName("Name")]
        [FormField(Order = 0)]
        public string Name { get; set; } = "first";

        [Category("Main")]
        [FormField(Order = 1, Minimum = 1, Maximum = 9)]
        public int Count { get; set; } = 3;

        [Category("Main")]
        [FormField(Order = 2, OptionsFrom = nameof(Sizes))]
        public string Size { get; set; } = "medium";

        [Category("Main")]
        [DisplayName("Show extra")]
        [FormField(Order = 3)]
        public bool ShowExtra { get; set; }

        [Category("Main")]
        [FormField(Order = 4, OptionsFrom = nameof(Sizes), ChoiceStyle = ChoiceStyle.CheckList)]
        public string Picked { get; set; } = "small";

        [Category("Main")]
        [FormField(Order = 5, OptionsFrom = nameof(Wide), ChoiceStyle = ChoiceStyle.RadioGroup)]
        public string Letter { get; set; } = "beta-option";

        [Category("Main")]
        [FormField(Order = 6, OptionsFrom = nameof(ManyNames))]
        public string Long { get; set; } = string.Empty;

        [Category("Extra")]
        [FormField]
        public string Secret { get; set; } = "hidden";

        [Category("Tail")]
        [FormField]
        public string After { get; set; } = "last";
    }

    private static void Render(Model model, Action<TuiFormParts, IApplication> body) =>
        TuiTestRunner.RunHeadlessApp(app =>
        {
            using var binding = new FormBinding(model);
            var parts = FormRenderer.Build(app, FormDefinitionGenerator.Generate(model), binding, new TuiFormOptions { AvailableWidth = 76 });
            var window = new Window { Width = Dim.Fill(), Height = Dim.Fill() };
            window.Add(parts.Root);
            var token = app.Begin(window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                body(parts, app);
            }
            finally
            {
                app.End(token);
                window.Dispose();
            }
        });

    [TestMethod]
    public void Widgets_WriteTheModel_AndModelChangesReachTheWidgets()
    {
        var model = new Model();
        Render(model, (parts, _) =>
        {
            var name = (TextField)parts.ControlViews[nameof(Model.Name)];
            Assert.AreEqual("first", name.Text);

            name.Text = "second";
            Assert.AreEqual("second", model.Name, "Typing writes the property straight away.");

            var count = (TextField)parts.ControlViews[nameof(Model.Count)];
            count.Text = "12";
            Assert.AreEqual(3, model.Count, "Out of range: an int property isn't written...");
            Assert.Contains("out of range", parts.ErrorLabels[nameof(Model.Count)].Text, "...and the field says why.");
            count.Text = "7";
            Assert.AreEqual(7, model.Count);
            Assert.AreEqual(string.Empty, parts.ErrorLabels[nameof(Model.Count)].Text);

            Assert.IsInstanceOfType<OptionSelector>(parts.Choices[nameof(Model.Size)].Widget, "Three short options fit on one line.");
            parts.Choices[nameof(Model.Size)].Value = "large";
            Assert.AreEqual("large", model.Size);

            var picked = parts.CheckLists[nameof(Model.Picked)];
            picked[2].Value = CheckState.Checked;
            Assert.AreEqual("small,large", model.Picked);

            // A plain model has no change notification: the binding raises one for its own writes,
            // and the form re-reads from it.
            parts.Binding.SetText(new TextFieldControl { Id = nameof(Model.Name), Label = "Name" }, "third");
            Assert.AreEqual("third", name.Text);
        });
    }

    [TestMethod]
    public void AChoiceTooWideForOneLine_Wraps_AndOneTooLongToWrap_BecomesTypeOrPick()
    {
        var model = new Model();
        Render(model, (parts, _) =>
        {
            var radios = parts.CheckLists[nameof(Model.Letter)];
            Assert.IsTrue(radios.All(r => r.RadioStyle));
            Assert.IsGreaterThan(1, radios.Select(r => r.Frame.Y).Distinct().Count(), "Six wide options wrap onto more than one line.");
            Assert.AreEqual(CheckState.Checked, radios[1].Value);

            radios[4].Value = CheckState.Checked;
            Assert.AreEqual("epsilon-option", model.Letter);
            Assert.AreEqual(CheckState.UnChecked, radios[1].Value, "Still a single choice.");

            Assert.IsInstanceOfType<TextField>(parts.Choices[nameof(Model.Long)].Widget);
            Assert.IsTrue(parts.PickButtons.ContainsKey(nameof(Model.Long)));
            parts.Choices[nameof(Model.Long)].Value = "option number 5";
            Assert.AreEqual("option number 5", model.Long);
        });
    }

    [TestMethod]
    public void AHiddenSection_TakesNoRows_AndShowingItPushesLaterSectionsDown()
    {
        var model = new Model();
        Render(model, (parts, app) =>
        {
            var secret = parts.ControlViews[nameof(Model.Secret)];
            var after = parts.ControlViews[nameof(Model.After)];
            Assert.IsFalse(secret.Visible);
            Assert.IsFalse(parts.SectionHeaderLabels["Extra"].Visible);
            app.LayoutAndDraw(true);
            var hiddenTop = after.Frame.Y;
            var hiddenRows = parts.Rows;

            ((CheckBox)parts.ControlViews[nameof(Model.ShowExtra)]).Value = CheckState.Checked;
            app.LayoutAndDraw(true);

            Assert.IsTrue(model.ShowExtra);
            Assert.IsTrue(secret.Visible);
            Assert.AreEqual(hiddenTop + 3, after.Frame.Y, "The Extra section's header, row and blank line now sit above it.");
            Assert.AreEqual(hiddenRows + 3, parts.Rows);
            Assert.IsTrue(parts.IsRowVisible(nameof(Model.Secret)));
        });
    }
}
