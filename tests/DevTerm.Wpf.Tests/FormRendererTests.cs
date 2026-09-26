using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using DevTerm.UiDefinitions.Forms;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The WPF generic form renderer (<see cref="FormRenderer"/>) against a small plain model: each widget
/// kind writes its property, a model change reaches the widgets, validation shows inline, and a
/// visibility condition collapses a section. The Connection Editor's own use is covered by
/// <c>DeviceProfilesWindowTests</c>.
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

        [Category("Main")]
        [FormField(Order = 0)]
        public string Name { get; set; } = "first";

        [Category("Main")]
        [FormField(Order = 1, Minimum = 1, Maximum = 9)]
        public int Count { get; set; } = 3;

        [Category("Main")]
        [FormField(Order = 2, OptionsFrom = nameof(Sizes))]
        public string Size { get; set; } = "medium";

        [Category("Main")]
        [FormField(Order = 3, OptionsFrom = nameof(Sizes), ChoiceStyle = ChoiceStyle.RadioGroup)]
        public string Radio { get; set; } = "small";

        [Category("Main")]
        [FormField(Order = 4, OptionsFrom = nameof(Sizes), ChoiceStyle = ChoiceStyle.CheckList)]
        public string Picked { get; set; } = string.Empty;

        [Category("Main")]
        [FormField(Order = 5)]
        public bool ShowExtra { get; set; }

        [Category("Main")]
        [FormField(Order = 6)]
        public int? Limit { get; set; }

        [Category("Extra")]
        [FormField]
        public string Secret { get; set; } = "hidden";
    }

    [TestMethod]
    public void Widgets_WriteTheModel_ValidateInline_AndConditionsCollapseASection()
    {
        StaTestRunner.Run(async () =>
        {
            var model = new Model();
            using var binding = new FormBinding(model);
            var parts = FormRenderer.Build(FormDefinitionGenerator.Generate(model), binding);

            Assert.AreEqual("first", parts.TextBoxes[nameof(Model.Name)].Text);
            parts.TextBoxes[nameof(Model.Name)].Text = "second";
            Assert.AreEqual("second", model.Name);

            parts.TextBoxes[nameof(Model.Count)].Text = "42";
            Assert.AreEqual(3, model.Count);
            Assert.AreEqual(Visibility.Visible, parts.ErrorTexts[nameof(Model.Count)].Visibility);
            parts.TextBoxes[nameof(Model.Count)].Text = "4";
            Assert.AreEqual(4, model.Count);
            Assert.AreEqual(Visibility.Collapsed, parts.ErrorTexts[nameof(Model.Count)].Visibility);

            var size = (ComboBox)parts.ControlViews[nameof(Model.Size)];
            Assert.AreEqual("medium", size.SelectedItem);
            size.SelectedItem = "large";
            Assert.AreEqual("large", model.Size);

            parts.RadioGroups[nameof(Model.Radio)][2].IsChecked = true;
            Assert.AreEqual("large", model.Radio);

            parts.CheckLists[nameof(Model.Picked)][0].IsChecked = true;
            parts.CheckLists[nameof(Model.Picked)][2].IsChecked = true;
            Assert.AreEqual("small,large", model.Picked);

            Assert.AreEqual(Visibility.Collapsed, parts.SectionPanels["Extra"].Visibility);
            ((CheckBox)parts.ControlViews[nameof(Model.ShowExtra)]).IsChecked = true;
            Assert.IsTrue(model.ShowExtra);
            Assert.AreEqual(Visibility.Visible, parts.SectionPanels["Extra"].Visibility, "A plain model: the binding's own change notice re-evaluates the condition.");

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void BlankOptionalNumber_IsValid_AndClearsTheValue()
    {
        // Mirrors the TUI renderer's fix: "! '' is not a whole number." used to show for a blank
        // optional number, which then couldn't be cleared.
        StaTestRunner.Run(async () =>
        {
            var model = new Model();
            using var binding = new FormBinding(model);
            var parts = FormRenderer.Build(FormDefinitionGenerator.Generate(model), binding);

            parts.TextBoxes[nameof(Model.Limit)].Text = "5";
            Assert.AreEqual(5, model.Limit);
            parts.TextBoxes[nameof(Model.Limit)].Text = string.Empty;

            Assert.IsNull(model.Limit);
            Assert.AreEqual(Visibility.Collapsed, parts.ErrorTexts[nameof(Model.Limit)].Visibility, parts.ErrorTexts[nameof(Model.Limit)].Text);

            await Task.CompletedTask;
        });
    }
}
