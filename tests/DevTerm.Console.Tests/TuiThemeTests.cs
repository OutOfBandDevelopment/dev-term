using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;
using TgColor = Terminal.Gui.Drawing.Color;

namespace DevTerm.Console.Tests;

/// <summary>
/// Theming in the TUI (see <see cref="TuiTheme"/>, docs/design/theming.md): the role-to-scheme
/// mapping, a live switch through View &gt; Theme repainting the real rendered cells (read from
/// <c>Driver.GetOutputBuffer()</c> on the UI thread, not just the view's scheme), and the Light/Dark
/// screenshots for docs/user-guide/themes.md. The active theme and Terminal.Gui's schemes are
/// process-wide, so every test puts both back afterwards and the class runs serially.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiThemeTests
{
    private static readonly string _imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    public required TestContext TestContext { get; set; }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not find the repo root (DevTerm.slnx) above '{AppContext.BaseDirectory}'.");
    }

    [TestCleanup]
    public void ResetTheme()
    {
        ActiveTheme.Reset();
        TuiTheme.Restore();
    }

    private static TgColor Tg(DevTermTheme theme, ThemeRole role) => TuiTheme.ToColor(theme[role]);

    private static (Session Session, AsciiPresenter Presenter, CliOptions Options) Create()
    {
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(new FakeTransport(), new Pipeline([presenter]));
        return (session, presenter, new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] });
    }

    [TestMethod]
    public void Apply_MapsWindowMenuAndStatusRolesOntoSchemes()
    {
        var theme = BuiltInThemes.Dark;
        TuiTheme.Apply(theme);

        var baseScheme = Terminal.Gui.Configuration.SchemeManager.GetScheme("Base");
        Assert.AreEqual(Tg(theme, ThemeRole.Foreground), baseScheme.Normal.Foreground);
        Assert.AreEqual(Tg(theme, ThemeRole.Background), baseScheme.Normal.Background);
        Assert.AreEqual(Tg(theme, ThemeRole.FieldBackground), baseScheme.Editable.Background, "Text fields have no border in a terminal, so they get their own background.");
        Assert.AreEqual(Tg(theme, ThemeRole.SelectionBackground), baseScheme.Focus.Background);

        var menu = Terminal.Gui.Configuration.SchemeManager.GetScheme("Menu");
        Assert.AreEqual(Tg(theme, ThemeRole.MenuBackground), menu.Normal.Background);

        var connected = TuiTheme.StatusAttribute(theme, ConnectionState.Open);
        Assert.AreEqual(Tg(theme, ThemeRole.StatusConnected), connected.Background);
        Assert.AreEqual(Tg(theme, ThemeRole.StatusConnectedText), connected.Foreground);
        Assert.AreEqual(Tg(theme, ThemeRole.StatusDisconnected), TuiTheme.StatusAttribute(theme, ConnectionState.Closed).Background);
        Assert.AreEqual(Tg(theme, ThemeRole.StatusConnecting), TuiTheme.StatusAttribute(theme, ConnectionState.Opening).Background);
    }

    [TestMethod]
    public void Restore_PutsTerminalGuisOwnSchemesBack()
    {
        var before = Terminal.Gui.Configuration.SchemeManager.GetScheme("Base").Normal;
        TuiTheme.Apply(BuiltInThemes.Dark);
        Assert.AreNotEqual(before, Terminal.Gui.Configuration.SchemeManager.GetScheme("Base").Normal);

        TuiTheme.Restore();

        Assert.AreEqual(before, Terminal.Gui.Configuration.SchemeManager.GetScheme("Base").Normal);
        Assert.IsNull(TuiTheme.Applied);
    }

    [TestMethod]
    public void OutputHighlighting_IsGeneratedFromTheTheme()
    {
        var xshd = OutputHighlighting.Xshd(BuiltInThemes.Dark);

        StringAssert.Contains(xshd, $"foreground=\"{BuiltInThemes.Dark[ThemeRole.OutputError].ToHex()}\"");
        StringAssert.Contains(xshd, $"foreground=\"{BuiltInThemes.Dark[ThemeRole.OutputStatus].ToHex()}\"");
        Assert.AreNotSame(OutputHighlighting.For(BuiltInThemes.Light), OutputHighlighting.For(BuiltInThemes.Dark));
        Assert.AreSame(OutputHighlighting.For(BuiltInThemes.Dark), OutputHighlighting.For(BuiltInThemes.Dark), "Cached per theme.");
    }

    [TestMethod]
    public void ViewThemeMenu_SwitchesLive_RepaintingTheRenderedCells()
    {
        var (session, presenter, options) = Create();
        TuiTestRunner.RunHeadless(session, presenter, options, parts =>
        {
            parts.Output.Text = string.Join('\n', TuiMode.ErrorLine("boom"), TuiMode.StatusLine("Connected."), "[ascii] ID TEK/2230");
            var app = TuiTestRunner.CurrentApp;

            foreach (var (name, theme) in new[] { ("dark", BuiltInThemes.Dark), ("light", BuiltInThemes.Light), ("dark", BuiltInThemes.Dark) })
            {
                // Exactly what picking the item does (its Action), on the UI thread.
                parts.ThemeMenu.Items[name].Action!.Invoke();
                app.LayoutAndDraw(true);
                var buffer = app.Driver!.GetOutputBuffer();

                Assert.AreEqual(name, ActiveTheme.Selection);
                Assert.AreEqual(Tg(theme, ThemeRole.Background), buffer.Contents![0, 0].Attribute!.Value.Background, $"{name}: the window border repaints in the theme's background.");
                Assert.AreEqual(Tg(theme, ThemeRole.OutputError), buffer.Contents[2, 3].Attribute!.Value.Foreground, $"{name}: [error] lines use the theme's outputError.");
                Assert.AreEqual(Tg(theme, ThemeRole.OutputStatus), buffer.Contents[3, 3].Attribute!.Value.Foreground, $"{name}: [dev-term] lines use the theme's outputStatus.");

                var statusCell = FindCell(buffer, "●");
                Assert.AreEqual(Tg(theme, ThemeRole.StatusDisconnected), statusCell.Background, $"{name}: the (disconnected) status line uses the theme's statusDisconnected.");
                Assert.AreEqual(Tg(theme, ThemeRole.StatusDisconnectedText), statusCell.Foreground);

                Assert.StartsWith("●", parts.ThemeMenu.Items[name].Title, "The current theme is marked in the menu.");
                Assert.IsFalse(parts.ThemeMenu.Items[BuiltInThemes.SystemName].Title.StartsWith('●'));
            }
        }, TuiTestRunner.EmptyProfiles());
    }

    [TestMethod]
    public void ViewThemeMenu_ListsBuiltInsThenUserThemes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-tui-themes", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "amber.json"), """{ "basedOn": "dark", "colors": { "foreground": "#FFB000" } }""");
        ActiveTheme.UseCatalog(ThemeCatalog.Load(directory));

        var (session, presenter, options) = Create();
        TuiTestRunner.RunHeadless(session, presenter, options, parts =>
        {
            CollectionAssert.AreEqual(new[] { "light", "dark", "system", "amber" }, parts.ThemeMenu.Items.Keys.ToArray());

            parts.ThemeMenu.Items["amber"].Action!.Invoke();
            TuiTestRunner.CurrentApp.LayoutAndDraw(true);

            Assert.AreEqual(new TgColor(0xFF, 0xB0, 0x00, 255), TuiTestRunner.CurrentApp.Driver!.GetOutputBuffer().Contents![0, 0].Attribute!.Value.Foreground);
        }, TuiTestRunner.EmptyProfiles());
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public async Task MainWindow_IsCapturedInTheme(string theme)
    {
        ActiveTheme.Select(theme, persist: false);
        TuiTheme.Apply(ActiveTheme.Current);
        var (session, presenter, options) = Create();
        await session.OpenAsync(TestContext.CancellationToken);

        TuiTestRunner.RunHeadless(session, presenter, options, parts =>
        {
            parts.Output.Text = string.Join('\n', "[ascii] ID TEK/2230,V81.1,VERS:14", TuiMode.StatusLine("Connected."), TuiMode.ErrorLine("Could not send: the device did not answer."));
            parts.SendField.Text = "*IDN?";
            TuiTestRunner.CurrentApp.LayoutAndDraw(true);
            TuiScreenshot.Save(Path.Combine(_imagesDirectory, $"tui-theme-{theme}-main.png"));
        }, TuiTestRunner.EmptyProfiles());

        await session.CloseAsync(TestContext.CancellationToken);
        Assert.IsTrue(File.Exists(Path.Combine(_imagesDirectory, $"tui-theme-{theme}-main.png")));
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public async Task ManifestControlPanel_IsCapturedInTheme(string theme)
    {
        ActiveTheme.Select(theme, persist: false);
        TuiTheme.Apply(ActiveTheme.Current);
        var manifest = DevTerm.DeviceManifests.DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));
        SectionExpansionState.Forget(manifest.Name);
        SectionExpansionState.Set(manifest.Name, ControlPanelMode.NotesSectionLabel, expanded: false);
        var transport = new DevTerm.Transports.Loopback.LoopbackTransport(Options.Create(new DevTerm.Transports.Loopback.LoopbackTransportOptions()));
        await using var session = new Session(transport, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        using var panel = DevTerm.DeviceManifests.ManifestPanel.Attach(session, manifest);

        try
        {
            TuiTestRunner.RunWithLoop(
                app =>
                {
                    app.Driver!.SetScreenSize(90, 66);
                    return ManifestPanelMode.BuildWindow(app, panel);
                },
                parts =>
                {
                    panel.Surface.InvokeAsync("measure", null, TestContext.CancellationToken).GetAwaiter().GetResult();
                    panel.Surface.InvokeAsync("samples", "40", TestContext.CancellationToken).GetAwaiter().GetResult();
                    Assert.IsTrue(TuiTestRunner.WaitUntilOnLoop(
                        () => ((DevTerm.UiDefinitions.StripChartState)parts.DisplayViews["history"].State).SamplesOf("chA").Count == 41,
                        _waitTimeout));
                    TuiTestRunner.InvokeOnLoop(() =>
                    {
                        TuiTestRunner.CurrentApp.LayoutAndDraw(true);
                        TuiScreenshot.Save(Path.Combine(_imagesDirectory, $"tui-theme-{theme}-panel.png"));
                        return true;
                    });
                });
        }
        finally
        {
            SectionExpansionState.Forget(manifest.Name);
        }

        Assert.IsTrue(File.Exists(Path.Combine(_imagesDirectory, $"tui-theme-{theme}-panel.png")));
    }

    private static Terminal.Gui.Drawing.Attribute FindCell(Terminal.Gui.Drivers.IOutputBuffer buffer, string grapheme)
    {
        for (var row = buffer.Rows - 1; row >= 0; row--)
        {
            for (var col = 0; col < buffer.Cols; col++)
            {
                if (buffer.Contents![row, col].Grapheme == grapheme)
                {
                    return buffer.Contents[row, col].Attribute!.Value;
                }
            }
        }

        throw new AssertFailedException($"No '{grapheme}' cell on screen:\n{TuiTestRunner.DumpBuffer()}");
    }
}
