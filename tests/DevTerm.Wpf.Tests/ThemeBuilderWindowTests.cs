using System.IO;
using System.Windows;
using System.Windows.Controls;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives the real <see cref="ThemeBuilderWindow"/> directly - field wiring (name, palette,
/// reset-to-seed) and every Save path, since <see cref="ThemeBuilderWindow.ReportValidationError"/>/
/// <see cref="ThemeBuilderWindow.ConfirmOverwrite"/> make even the validation/overwrite branches
/// testable without a real blocking <c>MessageBox</c> - unlike the TUI's equivalent
/// (<c>DevTerm.Console.Tests.ThemeBuilderModeTests</c>), which needs a nested <c>Application.Run</c>
/// for those same branches. Saves go to a temp folder, never the real
/// <see cref="DevTermUserDataPaths.ThemesDirectory"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ThemeBuilderWindowTests
{
    private string _themesDirectory = string.Empty;

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-theme-builder-window-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    [TestInitialize]
    public void CreateDirectory() => _themesDirectory = CreateTempDirectory();

    [TestCleanup]
    public void Cleanup()
    {
        ActiveTheme.Reset();
        Directory.Delete(_themesDirectory, recursive: true);
    }

    private ThemeBuilderWindow BuildWindow(ThemeBuilderState state)
    {
        var window = new ThemeBuilderWindow(state, _themesDirectory) { ShowInTaskbar = false };
        StaTestRunner.DoEvents();
        return window;
    }

    [TestMethod]
    public void Constructor_RendersTheSeedsNameAndPalette()
    {
        StaTestRunner.Run(async () =>
        {
            var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
            var window = BuildWindow(state);

            Assert.AreEqual("My Theme", window.NameBox.Text);
            Assert.AreEqual(0, window.PaletteBox.SelectedIndex, "Light's own chart palette is Light.");
            Assert.AreEqual(Enum.GetValues<ThemeRole>().Length, window.RolesList.Items.Count, "Every role gets a row.");

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void NameBox_TextChanged_RenamesTheStateAndPreviewsIt()
    {
        StaTestRunner.Run(async () =>
        {
            var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
            var window = BuildWindow(state);

            window.NameBox.Text = "Renamed";
            StaTestRunner.DoEvents();

            Assert.AreEqual("Renamed", state.Name);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void PaletteBox_SelectionChanged_SwitchesTheChartPaletteAndPreviewsIt()
    {
        StaTestRunner.Run(async () =>
        {
            var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
            var window = BuildWindow(state);

            window.PaletteBox.SelectedIndex = 1;
            StaTestRunner.DoEvents();

            Assert.AreEqual(ChartPaletteVariant.Dark, state.ChartPalette);
            Assert.AreEqual(ChartPaletteVariant.Dark, ActiveTheme.Current.ChartPalette);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ResetButton_RestoresTheSelectedRoleToTheSeed()
    {
        StaTestRunner.Run(async () =>
        {
            var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
            state.Set(ThemeRole.Background, ThemeColor.Parse("#112233"));
            var window = BuildWindow(state);

            window.RolesList.SelectedIndex = Array.IndexOf(Enum.GetValues<ThemeRole>(), ThemeRole.Background);
            window.ResetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.IsFalse(state.IsOverridden(ThemeRole.Background));
            Assert.AreEqual(BuiltInThemes.Light[ThemeRole.Background], state[ThemeRole.Background]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Save_WithAFreshValidName_WritesTheFileAndSelectsIt()
    {
        StaTestRunner.Run(async () =>
        {
            var state = new ThemeBuilderState(BuiltInThemes.Light, "Fresh Theme");
            state.Set(ThemeRole.Background, ThemeColor.Parse("#112233"));
            var window = BuildWindow(state);

            window.Save();

            var path = Path.Combine(_themesDirectory, "Fresh Theme.json");
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual("Fresh Theme", ActiveTheme.Selection, "Save selects the newly-written theme by name.");

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Save_WithAnEmptyName_ReportsAnErrorAndDoesNotSave()
    {
        StaTestRunner.Run(async () =>
        {
            var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
            var window = BuildWindow(state);

            var errors = new List<string>();
            window.ReportValidationError = errors.Add;
            window.NameBox.Text = "   ";

            window.Save();

            Assert.AreEqual(1, errors.Count);
            Assert.IsEmpty(Directory.GetFiles(_themesDirectory));

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Save_WithAReservedName_ReportsAnErrorAndDoesNotSave()
    {
        StaTestRunner.Run(async () =>
        {
            var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
            var window = BuildWindow(state);

            var errors = new List<string>();
            window.ReportValidationError = errors.Add;
            window.NameBox.Text = BuiltInThemes.LightName;

            window.Save();

            Assert.AreEqual(1, errors.Count);
            Assert.IsEmpty(Directory.GetFiles(_themesDirectory));

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Save_OverwritingAnExistingFile_AsksAndSavesOnConfirm()
    {
        StaTestRunner.Run(async () =>
        {
            File.WriteAllText(Path.Combine(_themesDirectory, "Existing.json"), "{}");
            var state = new ThemeBuilderState(BuiltInThemes.Light, "Existing");
            var window = BuildWindow(state);

            var asked = 0;
            window.ConfirmOverwrite = name => { asked++; Assert.AreEqual("Existing", name); return false; };

            window.Save();

            Assert.AreEqual(1, asked, "An existing file should ask before overwriting.");
            Assert.AreEqual("{}", File.ReadAllText(Path.Combine(_themesDirectory, "Existing.json")), "Declining should leave the file untouched.");

            window.ConfirmOverwrite = _ => true;
            window.Save();

            var text = File.ReadAllText(Path.Combine(_themesDirectory, "Existing.json"));
            Assert.AreNotEqual("{}", text, "Confirming should replace the placeholder file with real theme content.");
            Assert.Contains("\"name\": \"Existing\"", text);

            await Task.CompletedTask;
        });
    }
}
