using System.Buffers;
using System.Windows.Controls;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives a real <see cref="ControlPanelWindow"/> — real WPF controls, built by the actual
/// production code-behind — from a synthetic <see cref="UiDefinition"/> (not the K8055's own,
/// since this renderer is meant to be generic — see <c>DevTerm.Devices.K8055.K8055UiDefinition</c>
/// for the real one) plus a fake <see cref="IControlSurface"/>/<see cref="IStructuredPresenter"/>.
/// Same conventions as <see cref="DeviceProfilesWindowTests"/>: <see cref="StaTestRunner.Run"/>,
/// never <c>Show()</c>, and a <see cref="StaTestRunner.DoEvents"/> pump right after construction
/// (bindings/initial state don't populate synchronously before a pump).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ControlPanelWindowTests
{
    private sealed class FakeControlSurface : IControlSurface
    {
        public List<(string CommandId, string? Value)> Invocations { get; } = [];

        public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
        {
            Invocations.Add((commandId, value));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStructuredPresenter : IPresenter, IStructuredPresenter
    {
        public string Name => "fake";

        public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) => [];

        public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

        public void Fire(IReadOnlyDictionary<string, string> values) => ValuesChanged?.Invoke(this, values);
    }

    private static UiDefinition BuildSampleDefinition() => new()
    {
        Name = "Sample Device",
        Sections =
        [
            new UiSection
            {
                Label = "Outputs",
                Controls =
                [
                    new ButtonControl { Id = "reset", Label = "Reset" },
                    new ButtonControl { Id = "paramButton", Label = "Param Button", ParameterFieldIds = ["text1", "choiceDropdown"] },
                    new ButtonControl { Id = "paramButton2", Label = "Param Button 2", CommandId = "paramCommand", ParameterFieldIds = ["text1"] },
                    new ToggleControl { Id = "toggle1", Label = "Toggle 1", DefaultValue = false },
                    new SliderControl { Id = "slider1", Label = "Slider 1", Minimum = 0, Maximum = 255, DefaultValue = 10 },
                    new NumericControl { Id = "numeric1", Label = "Numeric 1", Minimum = 0, Maximum = 100, DefaultValue = 5 },
                    new ChoiceControl { Id = "choiceRadio", Label = "Choice Radio", Options = ["a", "b"], DefaultValue = "a", Style = ChoiceStyle.RadioGroup },
                    new ChoiceControl { Id = "choiceDropdown", Label = "Choice Dropdown", Options = ["x", "y"], DefaultValue = "x", Style = ChoiceStyle.Dropdown },
                    new TextFieldControl { Id = "text1", Label = "Text 1", DefaultValue = "hi" },
                ],
            },
            new UiSection
            {
                Label = "Inputs",
                Controls = [new IndicatorControl { Id = "indicator1", Label = "Indicator 1", DefaultValue = "0" }],
            },
        ],
    };

    [TestMethod]
    public void Constructor_RendersEachControlKindAsTheExpectedWidgetType()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ControlPanelWindow(BuildSampleDefinition(), new FakeControlSurface(), null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            Assert.IsInstanceOfType<Button>(window.ControlViews["reset"]);
            Assert.IsInstanceOfType<CheckBox>(window.ControlViews["toggle1"]);
            Assert.IsInstanceOfType<Slider>(window.ControlViews["slider1"]);
            Assert.IsInstanceOfType<TextBox>(window.ControlViews["numeric1"]);
            Assert.IsInstanceOfType<StackPanel>(window.ControlViews["choiceRadio"]);
            Assert.IsInstanceOfType<ComboBox>(window.ControlViews["choiceDropdown"]);
            Assert.IsInstanceOfType<TextBox>(window.ControlViews["text1"]);
            Assert.IsInstanceOfType<TextBlock>(window.ControlViews["indicator1"]);
            Assert.IsTrue(window.IndicatorLabels.ContainsKey("indicator1"));

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void NoStructuredPresenter_ShowsTheNotDecodingStatusLine()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ControlPanelWindow(BuildSampleDefinition(), new FakeControlSurface(), null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            Assert.Contains("Not decoding", window.StatusText.Text);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Button_WhenClicked_InvokesItsIdWithNoValue()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            var button = (Button)window.ControlViews["reset"];
            button.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("reset", (string?)null), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ButtonWithParameterFieldIds_WhenClicked_InvokesWithJoinedSiblingValues()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            var button = (Button)window.ControlViews["paramButton"];
            button.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("paramButton", "hi,x"), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ButtonWithParameterFieldIds_ReadsCurrentValueAndUsesCommandIdOverride()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            ((TextBox)window.ControlViews["text1"]).Text = "updated";
            var button = (Button)window.ControlViews["paramButton2"];
            button.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("paramCommand", "updated"), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Toggle_WhenChecked_InvokesItsIdWithOne()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            ((CheckBox)window.ControlViews["toggle1"]).IsChecked = true;

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("toggle1", "1"), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Slider_WhenChanged_InvokesItsIdWithTheValue()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            ((Slider)window.ControlViews["slider1"]).Value = 200;

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("slider1", "200"), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Numeric_WhenCommitted_InvokesItsIdWithTheClampedValue()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            var field = (TextBox)window.ControlViews["numeric1"];
            field.Text = "999"; // above Maximum=100, should clamp
            field.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.UIElement.LostFocusEvent));

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("numeric1", "100"), surface.Invocations[0]);
            Assert.AreEqual("100", field.Text);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void TextField_WhenCommitted_InvokesItsIdWithTheTypedValue()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            var field = (TextBox)window.ControlViews["text1"];
            field.Text = "hello";
            field.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.UIElement.LostFocusEvent));

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("text1", "hello"), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void RadioChoice_WhenSelected_InvokesItsIdWithTheOption()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            var panel = (StackPanel)window.ControlViews["choiceRadio"];
            var second = (RadioButton)panel.Children[1];
            second.IsChecked = true;

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("choiceRadio", "b"), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DropdownChoice_WhenSelectionChanges_InvokesItsIdWithTheOption()
    {
        StaTestRunner.Run(async () =>
        {
            var surface = new FakeControlSurface();
            var window = new ControlPanelWindow(BuildSampleDefinition(), surface, null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            ((ComboBox)window.ControlViews["choiceDropdown"]).SelectedItem = "y";

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("choiceDropdown", "y"), surface.Invocations[0]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void RowLabel_StaysOnOneLine_AndNeverOverlapsItsRowContent()
    {
        // History: the row label was first a fixed-Width, unwrapped TextBlock, so a long label
        // ("Configure DC Voltage Range", the 34401A profile) painted over its row's control; the
        // fix wrapped it, which then broke short-but-not-short-enough labels like Busylight's
        // "On (unit unconfirmed):" onto two lines. Now labels never wrap and sit in an auto-sized
        // label column per section, so the control column starts past the longest label instead.
        StaTestRunner.Run(async () =>
        {
            var definition = new UiDefinition
            {
                Name = "Sample Device",
                Sections =
                [
                    new UiSection
                    {
                        Label = "Configure",
                        Controls =
                        [
                            new ButtonControl { Id = "longLabel", Label = "Configure DC Voltage Range" },
                            new ButtonControl { Id = "short", Label = "Go" },
                        ],
                    },
                ],
            };
            var window = new ControlPanelWindow(definition, new FakeControlSurface(), null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();
            var content = (System.Windows.FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(800, 600));
            content.Arrange(new System.Windows.Rect(0, 0, 800, 600));
            content.UpdateLayout();

            var label = window.ControlLabels["longLabel"];
            Assert.AreEqual(System.Windows.TextWrapping.NoWrap, label.TextWrapping);

            var grid = (Grid)label.Parent;
            var labelRight = label.TranslatePoint(new System.Windows.Point(label.ActualWidth, 0), grid).X;
            var longX = window.ControlViews["longLabel"].TranslatePoint(default, grid).X;
            var shortX = window.ControlViews["short"].TranslatePoint(default, grid).X;
            Assert.IsGreaterThanOrEqualTo(labelRight, longX, "The control starts past its label, not over it.");
            Assert.AreEqual(longX, shortX, 0.5, "Every control in a section starts in the same column.");

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ValuesChanged_UpdatesTheMatchingIndicatorLabel()
    {
        StaTestRunner.Run(async () =>
        {
            var presenter = new FakeStructuredPresenter();
            var window = new ControlPanelWindow(BuildSampleDefinition(), new FakeControlSurface(), presenter) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            presenter.Fire(new Dictionary<string, string> { ["indicator1"] = "42" });
            StaTestRunner.DoEvents();

            Assert.AreEqual("42", window.IndicatorLabels["indicator1"].Text);

            await Task.CompletedTask;
        });
    }

    private static UiDefinition ColorButtonDefinition(string buttonId) => new()
    {
        Name = "Color Device",
        Sections =
        [
            new UiSection
            {
                Label = "Color",
                Controls = [new ButtonControl { Id = buttonId, Label = "Custom...", ColorPickerTargetCommandId = "color" }],
            },
        ],
    };

    [TestMethod]
    public void CustomColorSwatch_HiddenUntilAColorIsSet()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ControlPanelWindow(ColorButtonDefinition($"custom-{Guid.NewGuid():N}"), new FakeControlSurface(), null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            Assert.AreEqual(System.Windows.Visibility.Collapsed, window.ColorSwatches.Values.Single().Visibility);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void CustomColorSwatch_ShowsTheHexOnThatColor_WhenOneWasSetBefore()
    {
        StaTestRunner.Run(async () =>
        {
            // Set in an earlier opening of the panel - the swatch shows it straight away.
            var id = $"custom-{Guid.NewGuid():N}";
            DevTerm.Configuration.LastPickedColors.Set(id, (0xFF, 0x80, 0x00));

            var window = new ControlPanelWindow(ColorButtonDefinition(id), new FakeControlSurface(), null) { ShowInTaskbar = false };
            StaTestRunner.DoEvents();

            var swatch = window.ColorSwatches[id];
            Assert.AreEqual(System.Windows.Visibility.Visible, swatch.Visibility);
            Assert.AreEqual("#FF8000", ((TextBlock)swatch.Child).Text);
            Assert.AreEqual(System.Windows.Media.Color.FromRgb(0xFF, 0x80, 0x00), ((System.Windows.Media.SolidColorBrush)swatch.Background).Color);
            await Task.CompletedTask;
        });
    }
}
