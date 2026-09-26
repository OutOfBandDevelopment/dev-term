using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Devices.Busylight;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Theming in WPF (see <see cref="WpfTheme"/>, docs/design/theming.md): every window gets the theme's
/// role brushes as resources, View &gt; Theme switches all of them live, and a dark theme swaps in the
/// dark-capable control templates. Checked on the brushes named elements actually resolve, not just
/// on the resource dictionary. The active theme is process-wide, so every test resets it.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class WpfThemeTests
{
    [TestCleanup]
    public void ResetTheme() => ActiveTheme.Reset();

    private static Color ColorOf(DevTermTheme theme, ThemeRole role) => WpfTheme.ToColor(theme[role]);

    private static Color? ColorOf(Brush? brush) => (brush as SolidColorBrush)?.Color;

    private static MainWindow CreateMainWindow()
    {
        var ascii = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
        return new MainWindow(new Session(new FakeTransport(), new Pipeline([ascii])), new PresenterCatalog([ascii]), new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] }, IsolatedProfiles.Empty())
        {
            ShowInTaskbar = false,
        };
    }

    [TestMethod]
    public void EveryRole_IsAResource_UnderItsKey()
    {
        StaTestRunner.Run(async () =>
        {
            ActiveTheme.Select("dark", persist: false);
            var window = CreateMainWindow();

            foreach (var role in Enum.GetValues<ThemeRole>())
            {
                Assert.AreEqual(ColorOf(BuiltInThemes.Dark, role), ColorOf(window.FindResource(WpfTheme.Key(role)) as Brush), role.ToString());
            }

            Assert.IsTrue((bool)window.FindResource(WpfTheme.ChartPaletteDarkKey));
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ViewThemeMenu_SwitchesTheMainWindowLive()
    {
        StaTestRunner.Run(async () =>
        {
            var window = CreateMainWindow();
            StaTestRunner.DoEvents();
            var sendButton = FindButton(window, "Send");
            var stockListBackground = ColorOf(window.OutputList.Background);
            var stockButtonBackground = ColorOf(sendButton.Background);
            Assert.IsTrue(window.ThemeMenuItems["light"].IsChecked);

            window.SelectTheme("dark");
            StaTestRunner.DoEvents();

            var dark = BuiltInThemes.Dark;
            Assert.AreSame(dark, WpfTheme.AppliedTo(window));
            Assert.AreEqual(ColorOf(dark, ThemeRole.Background), ColorOf(window.Background));
            Assert.AreEqual(ColorOf(dark, ThemeRole.Foreground), ColorOf(window.Foreground));
            Assert.AreEqual(ColorOf(dark, ThemeRole.StatusDisconnected), ColorOf(window.ConnectionStatusDot.Fill));
            Assert.AreEqual(ColorOf(dark, ThemeRole.Recording), ColorOf(window.LoggingStatusText.Foreground));
            Assert.AreEqual(ColorOf(dark, ThemeRole.ControlBackground), ColorOf(window.OutputList.Background), "The output list is dark (a stock ListBox is fixed white).");
            Assert.AreEqual(ColorOf(dark, ThemeRole.ControlBackground), ColorOf(sendButton.Background), "Buttons use the dark template.");
            Assert.AreEqual(ColorOf(dark, ThemeRole.ControlForeground), ColorOf(sendButton.Foreground));
            Assert.IsTrue(window.ThemeMenuItems["dark"].IsChecked);
            Assert.IsFalse(window.ThemeMenuItems["light"].IsChecked);

            window.SelectTheme("light");
            StaTestRunner.DoEvents();

            Assert.AreEqual(ColorOf(BuiltInThemes.Light, ThemeRole.StatusDisconnected), ColorOf(window.ConnectionStatusDot.Fill));
            Assert.AreEqual(stockListBackground, ColorOf(window.OutputList.Background), "Light is the stock look again.");
            Assert.AreEqual(stockButtonBackground, ColorOf(sendButton.Background));
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void OutputLines_UseTheOutputRoles_InEitherTheme()
    {
        StaTestRunner.Run(async () =>
        {
            var window = CreateMainWindow();
            WpfScreenshot.ShowOffScreen(window);
            window.OutputList.Items.Add(new OutputLine("[dev-term] status", OutputKind.Status));
            window.OutputList.Items.Add(new OutputLine("[error] boom", OutputKind.Error));

            foreach (var theme in new[] { BuiltInThemes.Dark, BuiltInThemes.Light })
            {
                window.SelectTheme(theme.Name);
                window.UpdateLayout();
                StaTestRunner.DoEvents();

                var lines = FindVisualChildren<TextBlock>(window.OutputList).Where(t => t.Name == "LineText").ToList();
                Assert.AreEqual(ColorOf(theme, ThemeRole.OutputStatus), ColorOf(lines.Single(t => t.Text.StartsWith("[dev-term]", StringComparison.Ordinal)).Foreground), theme.Name);
                Assert.AreEqual(ColorOf(theme, ThemeRole.OutputError), ColorOf(lines.Single(t => t.Text.StartsWith("[error]", StringComparison.Ordinal)).Foreground), theme.Name);
            }

            window.Close();
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void AnotherOpenWindow_FollowsTheSwitchToo()
    {
        StaTestRunner.Run(async () =>
        {
            var session = new Session(new FakeTransport(), new Pipeline([]));
            var panel = new ControlPanelWindow(BusylightUiDefinition.Build(), new BusylightControlSurface(session), structuredSource: null);

            ActiveTheme.Select("dark", persist: false);
            StaTestRunner.DoEvents();

            Assert.AreEqual(ColorOf(BuiltInThemes.Dark, ThemeRole.Background), ColorOf(panel.Background));
            Assert.AreEqual(ColorOf(BuiltInThemes.Dark, ThemeRole.Error), ColorOf(panel.StatusText.Foreground));
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void AUserTheme_OnALightBase_RecolorsSurfacesButKeepsStockTemplates()
    {
        StaTestRunner.Run(async () =>
        {
            var sepia = BuiltInThemes.Light.With("sepia", new Dictionary<ThemeRole, ThemeColor>
            {
                [ThemeRole.Background] = ThemeColor.Parse("#F4ECD8"),
                [ThemeRole.ControlBackground] = ThemeColor.Parse("#FBF6EA"),
            });
            var window = CreateMainWindow();
            WpfTheme.Apply(window, sepia);
            StaTestRunner.DoEvents();

            Assert.AreEqual(Color.FromRgb(0xF4, 0xEC, 0xD8), ColorOf(window.Background));
            Assert.AreEqual(Color.FromRgb(0xFB, 0xF6, 0xEA), ColorOf(window.FindResource(SystemColors.WindowBrushKey) as Brush), "Stock control styles read their backgrounds from SystemColors.");
            Assert.IsFalse(window.Resources.MergedDictionaries.OfType<ResourceDictionary>().SelectMany(d => d.MergedDictionaries).OfType<DarkControls>().Any(), "Only a dark theme needs the replacement templates.");
            await Task.CompletedTask;
        });
    }

    private static Button FindButton(DependencyObject root, string content) =>
        FindLogicalChildren<Button>(root).First(button => Equals(button.Content, content));

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindLogicalChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }
}
