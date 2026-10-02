using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives the real <see cref="ThemeSeedPickerWindow"/> directly - the first step of the WPF
/// "Build/Edit Theme..." flow (<see cref="ThemeBuilderWindow.Run"/>). Mirrors
/// <c>DevTerm.Console.Tests.ThemeBuilderModeTests.PickSeed_ChoosingTheDefaultSelection_ReturnsLightAsTheSeed</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ThemeSeedPickerWindowTests
{
    [TestCleanup]
    public void Cleanup() => ActiveTheme.Reset();

    private static ThemeSeedPickerWindow BuildWindow()
    {
        var window = new ThemeSeedPickerWindow { ShowInTaskbar = false };
        StaTestRunner.DoEvents();
        return window;
    }

    [TestMethod]
    public void Constructor_DefaultsToTheFirstSeedWithAGeneratedName()
    {
        StaTestRunner.Run(async () =>
        {
            var window = BuildWindow();

            Assert.AreEqual(0, window.SeedList.SelectedIndex);
            Assert.AreEqual("Light copy", window.NameBox.Text);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Commit_WithTheDefaultSelection_ReturnsLightAsTheSeed()
    {
        StaTestRunner.Run(async () =>
        {
            var window = BuildWindow();

            window.Commit();

            Assert.AreEqual(BuiltInThemes.Light, window.ChosenTheme);
            Assert.AreEqual("Light copy", window.ChosenName);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Commit_WithACustomName_UsesItTrimmed()
    {
        StaTestRunner.Run(async () =>
        {
            var window = BuildWindow();

            window.NameBox.Text = "  My New Theme  ";
            window.Commit();

            Assert.AreEqual("My New Theme", window.ChosenName);

            await Task.CompletedTask;
        });
    }
}
