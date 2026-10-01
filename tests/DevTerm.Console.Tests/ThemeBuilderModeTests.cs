using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives the real <see cref="ThemeBuilderMode"/> window headlessly: field wiring (name, palette,
/// reset-to-seed, cancel) and the Save success path run under plain <see cref="TuiTestRunner.RunHeadlessApp"/>,
/// since none of them pop a real modal. Save's validation/overwrite dialogs and <see cref="ThemeBuilderMode.EditColor"/>
/// pop a real <c>MessageBox</c>/<c>Dialog</c> via a nested <c>Application.Run</c>, which headless mode can't
/// drive (see <c>ConfigureModeTests</c>'s <c>Quit_SetsResultToNull</c> comment) - those use
/// <see cref="TuiTestRunner.RunWithLoopApp"/> plus the nested-Run-plus-<c>AddTimeout</c> technique from
/// <c>TuiModeTests</c>'s <c>CtrlQ_*</c> tests instead. Saves go to a temp folder, never the real
/// <see cref="DevTermUserDataPaths.ThemesDirectory"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ThemeBuilderModeTests
{
    private string _themesDirectory = string.Empty;

    [TestInitialize]
    public void CreateDirectory()
    {
        _themesDirectory = Path.Combine(Path.GetTempPath(), "devterm-theme-builder-tui", Path.GetRandomFileName());
        Directory.CreateDirectory(_themesDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        ActiveTheme.Reset();
        Directory.Delete(_themesDirectory, recursive: true);
    }

    private static void Click(Button button) => button.InvokeCommand(Command.Accept);

    private void RunHeadless(ThemeBuilderState state, Action<ThemeBuilderWindowParts> body)
    {
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ThemeBuilderMode.BuildWindow(app, state, _themesDirectory);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try { body(parts); }
            finally { app.End(token); parts.Window.Dispose(); }
        });
    }

    [TestMethod]
    public void BuildWindow_RendersTheSeedsNameAndPalette()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");

        RunHeadless(state, parts =>
        {
            Assert.AreEqual("My Theme", parts.NameField.Text);
            Assert.AreEqual(0, parts.PaletteSelector.Value, "Light's own chart palette is Light.");
            Assert.AreEqual(Enum.GetValues<ThemeRole>().Length, parts.RolesList.Source?.Count, "Every role gets a row.");
        });
    }

    [TestMethod]
    public void NameField_TextChanged_RenamesTheStateAndPreviewsIt()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");

        RunHeadless(state, parts =>
        {
            parts.NameField.Text = "Renamed";

            Assert.AreEqual("Renamed", state.Name);
        });
    }

    [TestMethod]
    public void PaletteSelector_ValueChanged_SwitchesTheChartPaletteAndPreviewsIt()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");

        RunHeadless(state, parts =>
        {
            parts.PaletteSelector.Value = 1;

            Assert.AreEqual(ChartPaletteVariant.Dark, state.ChartPalette);
            Assert.AreEqual(ChartPaletteVariant.Dark, ActiveTheme.Current.ChartPalette);
        });
    }

    [TestMethod]
    public void ResetButton_RestoresTheSelectedRoleToTheSeed()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
        state.Set(ThemeRole.Background, ThemeColor.Parse("#112233"));

        RunHeadless(state, parts =>
        {
            parts.RolesList.SelectedItem = Array.IndexOf(Enum.GetValues<ThemeRole>(), ThemeRole.Background);

            Click(parts.ResetButton);

            Assert.IsFalse(state.IsOverridden(ThemeRole.Background));
            Assert.AreEqual(BuiltInThemes.Light[ThemeRole.Background], state[ThemeRole.Background]);
        });
    }

    [TestMethod]
    public void CancelButton_StopsTheWindowWithoutSaving()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");

        RunHeadless(state, parts =>
        {
            Click(parts.CancelButton);

            Assert.IsTrue(((IRunnable)parts.Window).StopRequested);
        });

        Assert.IsEmpty(Directory.GetFiles(_themesDirectory), "Cancel must not write a theme file.");
    }

    [TestMethod]
    public void SaveButton_WithAFreshValidName_WritesTheFileAndStopsTheWindow()
    {
        // A brand-new, non-conflicting, non-reserved name never reaches a MessageBox (Save's
        // overwrite-confirm only fires when the target file already exists) - safe under plain
        // RunHeadlessApp.
        var state = new ThemeBuilderState(BuiltInThemes.Light, "Fresh Theme");
        state.Set(ThemeRole.Background, ThemeColor.Parse("#112233"));

        RunHeadless(state, parts =>
        {
            Click(parts.SaveButton);

            Assert.IsTrue(((IRunnable)parts.Window).StopRequested);
        });

        var path = Path.Combine(_themesDirectory, "Fresh Theme.json");
        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual("Fresh Theme", ActiveTheme.Selection, "Save selects the newly-written theme by name.");
    }

    [TestMethod]
    public void SaveButton_WithAnEmptyName_ShowsAnErrorDialogAndDoesNotSave()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
        ThemeBuilderWindowParts? captured = null;

        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app =>
            {
                captured = ThemeBuilderMode.BuildWindow(app, state, _themesDirectory);
                return captured.Window;
            },
            body: (app, _) =>
            {
                var parts = captured!;
                var stillRunning = TuiTestRunner.InvokeOnLoop(() =>
                {
                    parts.NameField.Text = "   ";
                    app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                    {
                        app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                        return false;
                    });

                    Click(parts.SaveButton);
                    return !((IRunnable)parts.Window).StopRequested;
                });

                Assert.IsTrue(stillRunning, "An invalid name should show an error dialog, not close the window.");
            });

        Assert.IsEmpty(Directory.GetFiles(_themesDirectory));
    }

    [TestMethod]
    public void SaveButton_WithAReservedName_ShowsAnErrorDialogAndDoesNotSave()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
        ThemeBuilderWindowParts? captured = null;

        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app =>
            {
                captured = ThemeBuilderMode.BuildWindow(app, state, _themesDirectory);
                return captured.Window;
            },
            body: (app, _) =>
            {
                var parts = captured!;
                var stillRunning = TuiTestRunner.InvokeOnLoop(() =>
                {
                    parts.NameField.Text = BuiltInThemes.LightName;
                    app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                    {
                        app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                        return false;
                    });

                    Click(parts.SaveButton);
                    return !((IRunnable)parts.Window).StopRequested;
                });

                Assert.IsTrue(stillRunning, "A reserved name should show an error dialog, not close the window.");
            });

        Assert.IsEmpty(Directory.GetFiles(_themesDirectory));
    }

    [TestMethod]
    public void SaveButton_OverwritingAnExistingFile_AsksAndSavesOnConfirm()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "Existing");
        File.WriteAllText(Path.Combine(_themesDirectory, "Existing.json"), "{}");
        ThemeBuilderWindowParts? captured = null;

        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app =>
            {
                captured = ThemeBuilderMode.BuildWindow(app, state, _themesDirectory);
                return captured.Window;
            },
            body: (app, _) =>
            {
                var parts = captured!;
                var stopped = TuiTestRunner.InvokeOnLoop(() =>
                {
                    app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                    {
                        // The overwrite confirm MessageBox.Query defaults focus to "No" - same quirk as
                        // TuiModeTests' CtrlQ_WhileConnected_AsksAndQuitsOnConfirm - so reaching "Yes" needs
                        // one CursorLeft before Enter.
                        app.Keyboard.RaiseKeyDownEvent(Key.CursorLeft);
                        app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                        return false;
                    });

                    Click(parts.SaveButton);
                    return ((IRunnable)parts.Window).StopRequested;
                });

                Assert.IsTrue(stopped, "Confirming the overwrite should close the window.");
            });

        var text = File.ReadAllText(Path.Combine(_themesDirectory, "Existing.json"));
        Assert.AreNotEqual("{}", text, "The real theme content should have replaced the placeholder file.");
        Assert.Contains("\"name\": \"Existing\"", text);
    }

    [TestMethod]
    public void EditColor_AcceptingThePrefilledValue_RoundTripsIt()
    {
        var current = ThemeColor.Parse("#445566");
        ThemeColor? result = null;

        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app => new Window { Width = Dim.Fill(), Height = Dim.Fill() },
            body: (app, _) =>
            {
                result = TuiTestRunner.InvokeOnLoop(() =>
                {
                    app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                    {
                        app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                        return false;
                    });

                    return ThemeBuilderMode.EditColor(app, ThemeRole.Background, current);
                });
            });

        Assert.AreEqual(current, result);
    }

    [TestMethod]
    public void PickSeed_ChoosingTheDefaultSelection_ReturnsLightAsTheSeed()
    {
        ActiveTheme.Reset();
        (DevTermTheme Theme, string Name)? picked = null;

        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app => new Window { Width = Dim.Fill(), Height = Dim.Fill() },
            body: (app, _) =>
            {
                picked = TuiTestRunner.InvokeOnLoop(() =>
                {
                    app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                    {
                        app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                        return false;
                    });

                    return ThemeBuilderMode.PickSeed(app);
                });
            });

        Assert.IsNotNull(picked);
        Assert.AreEqual(BuiltInThemes.Light, picked.Value.Theme);
        Assert.AreEqual("Light copy", picked.Value.Name);
    }
}
