using System.IO;
using DevTerm.DeviceManifests;
using DevTerm.DeviceManifests.Editing;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The WPF expression picker (<see cref="ExpressionPickerWindow"/>) over <see cref="ExpressionPickerViewModel"/>, and the
/// <c>Pick...</c> button the manifest editor puts beside an indicator's Expression field. The window is constructed,
/// never shown (see CLAUDE.md on <c>Show()</c> under test); a modal result is not exercised.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ExpressionPickerWindowTests
{
    private static ExpressionPickerViewModel CreateViewModel(string? text = null) =>
        new(
            [
                new ValuePath("volts", ValuePathType.Number, ValuePathSource.Control, "Voltage", "V", 0, 30),
                new ValuePath("amps", ValuePathType.Number, ValuePathSource.Control, "Current", "A", 0, 5),
            ],
            text,
            seed: 7);

    [TestMethod]
    public void MoreFunctionsDropDown_InsertsTheChosenFunction()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ExpressionPickerWindow(CreateViewModel());
            StaTestRunner.DoEvents();
            var more = window.FunctionPanel.Children.OfType<System.Windows.Controls.ComboBox>().Single();
            var split = more.Items.OfType<System.Windows.Controls.ComboBoxItem>().Single(i => (string)i.Content == "split");

            more.SelectedItem = split;
            StaTestRunner.DoEvents();

            Assert.AreEqual("split(, ',')", window.ExpressionBox.Text);
            Assert.AreEqual(0, more.SelectedIndex);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ShowsTheViewModelsExpressionDiagnosticsAndResult()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ExpressionPickerWindow(CreateViewModel("{volts} * {amps}"));
            StaTestRunner.DoEvents();

            Assert.AreEqual("{volts} * {amps}", window.ExpressionBox.Text);
            Assert.AreEqual("OK", window.DiagnosticsText.Text);
            Assert.AreEqual($"Sample result: {window.ViewModel.ResultText}", window.ResultText.Text);
            Assert.HasCount(2, window.PathList.Items);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void TypingInTheBox_ReparsesAndReportsASyntaxError()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ExpressionPickerWindow(CreateViewModel());
            window.ExpressionBox.Text = "{volts} *";
            StaTestRunner.DoEvents();

            Assert.IsFalse(window.ViewModel.IsValid);
            StringAssert.StartsWith(window.DiagnosticsText.Text, "Error: ");
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void TheFilterBox_NarrowsThePathList()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ExpressionPickerWindow(CreateViewModel());
            window.FilterBox.Text = "amp";
            StaTestRunner.DoEvents();

            Assert.HasCount(1, window.PathList.Items);
            Assert.AreEqual("amps", ((PickerPath)window.PathList.Items[0]!).Path.Path);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void InsertingAPath_UpdatesTheBox_AndTheResult()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ExpressionPickerWindow(CreateViewModel());
            window.ViewModel.InsertPath(window.ViewModel.AllPaths.Single(p => p.Path.Path == "volts"));
            StaTestRunner.DoEvents();

            Assert.AreEqual("{volts}", window.ExpressionBox.Text);
            Assert.AreEqual("OK", window.DiagnosticsText.Text);
            Assert.AreNotEqual("-", window.ViewModel.ResultText);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void TheManifestEditor_OffersPick_OnAnIndicatorsExpression_ButNotOnOtherForms()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "manifests");
        var userDirectory = Path.Combine(Path.GetTempPath(), "devterm-expression-picker-wpf", Path.GetRandomFileName());
        Directory.CreateDirectory(userDirectory);
        try
        {
            StaTestRunner.Run(async () =>
            {
                var editor = new ManifestEditorViewModel(userDirectory, installed);
                Assert.IsTrue(editor.Open(Path.Combine(installed, "loopback-sensor-demo")), editor.StatusMessage);
                var window = new ManifestEditorWindow(editor) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.OutlineList.SelectedItem = editor.Nodes.First(n => n.Display.Contains("Last Sample", StringComparison.Ordinal));
                StaTestRunner.DoEvents();
                Assert.IsTrue(window.Form!.PickButtons.ContainsKey(nameof(ControlForm.IndicatorExpression)));
                var pick = window.Form!.PickButtons[nameof(ControlForm.IndicatorExpression)];
                window.UpdateLayout();
                Assert.IsTrue(pick.IsVisible || pick.Visibility == System.Windows.Visibility.Visible, $"Visibility={pick.Visibility} Actual={pick.ActualWidth}x{pick.ActualHeight}");

                window.OutlineList.SelectedItem = editor.Nodes.First(n => n.Display.Contains("barGraph", StringComparison.Ordinal));
                StaTestRunner.DoEvents();
                Assert.IsTrue(window.Form!.PickButtons.ContainsKey(nameof(ControlForm.Channels)));

                Assert.IsTrue(window.Form!.PickButtons.ContainsKey(nameof(ControlForm.VisibleWhenId)));
                window.OutlineList.SelectedItem = editor.Nodes.First(n => n.Kind == ManifestNodeKind.Control && n.Display.Trim().StartsWith("button", StringComparison.OrdinalIgnoreCase));
                StaTestRunner.DoEvents();
                Assert.IsTrue(window.Form!.PickButtons.ContainsKey(nameof(ControlForm.ParameterExpressions)));

                window.OutlineList.SelectedItem = editor.Nodes.First(n => n.Kind == ManifestNodeKind.Identity);
                StaTestRunner.DoEvents();
                Assert.IsFalse(window.Form!.PickButtons.ContainsKey(nameof(ControlForm.IndicatorExpression)));

                window.Editor.ConfirmDiscardChanges = () => true;
                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(userDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Screen_IsCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ExpressionPickerWindow(CreateViewModel("round({volts} * {amps}, 1)"));
            WpfScreenshot.ShowOffScreen(window, 560, 460);
            StaTestRunner.DoEvents();
            window.UpdateLayout();

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
            {
                directory = directory.Parent;
            }

            WpfScreenshot.Save(window, Path.Combine(directory!.FullName, "docs", "user-guide", "images", "wpf-expression-picker.png"));
            window.Close();
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Screens_AreCapturedForTheUserGuide()
    {
        var paths = new[]
        {
            new ValuePath("volts", ValuePathType.Number, ValuePathSource.Control, "Voltage", "V", 0, 30),
            new ValuePath("amps", ValuePathType.Number, ValuePathSource.Control, "Current", "A", 0, 5),
            new ValuePath("status", ValuePathType.Text, ValuePathSource.ResponsePattern, "Status"),
        };
        var cases = new (string Name, string Text, PickerMode Mode, string Filter)[]
        {
            ("wpf-expression-picker-error", "round({volts} * ", PickerMode.Expression, ""),
            ("wpf-expression-picker-warning", "{volts} * {watts}", PickerMode.Expression, ""),
            ("wpf-expression-picker-text", "matches({status}, 'READY') ? 100 : 0", PickerMode.Expression, ""),
            ("wpf-expression-picker-parameters", "round({volts} * 100, 0); {amps}", PickerMode.ExpressionList, ""),
            ("wpf-expression-picker-find", "{volts}", PickerMode.Expression, "amp"),
            ("wpf-expression-picker-channels", "volts:Volts:#FF6600; amps", PickerMode.Channels, ""),
        };

        StaTestRunner.Run(async () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
            {
                directory = directory.Parent;
            }

            foreach (var (name, text, mode, filter) in cases)
            {
                var window = new ExpressionPickerWindow(new ExpressionPickerViewModel(paths, text, seed: 7, mode: mode) { Filter = filter });
                WpfScreenshot.ShowOffScreen(window, 560, 460);
                StaTestRunner.DoEvents();
                window.UpdateLayout();
                WpfScreenshot.Save(window, Path.Combine(directory!.FullName, "docs", "user-guide", "images", name + ".png"));
                window.Close();
            }

            await Task.CompletedTask;
        });
    }
}
