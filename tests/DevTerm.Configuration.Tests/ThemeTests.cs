using DevTerm.Test.Utilities;
using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// The shared theme model (docs/design/theming.md): built-in themes are complete and readable, theme
/// files validate with clear errors and never throw, the catalog falls back rather than failing,
/// <c>system</c> follows the OS hint, and the selection is an app preference - never part of a
/// connection profile. <see cref="ActiveTheme"/> is process-wide, so the tests touching it run
/// serially and reset it.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ThemeTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "devterm-theme-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        ActiveTheme.Reset();
        Directory.Delete(_directory, recursive: true);
    }

    private string WriteTheme(string fileName, string json)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    [TestMethod]
    public void BuiltIns_DefineEveryRole_AndPassEveryContrastCheck()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            foreach (var role in Enum.GetValues<ThemeRole>())
            {
                _ = theme[role];
            }

            Assert.IsEmpty(theme.ContrastWarnings(), $"{theme.Name}: {string.Join(" ", theme.ContrastWarnings())}");
        }

        Assert.IsFalse(BuiltInThemes.Light.IsDark);
        Assert.IsTrue(BuiltInThemes.Dark.IsDark);
        Assert.AreEqual(ChartPaletteVariant.Light, BuiltInThemes.Light.ChartPalette);
        Assert.AreEqual(ChartPaletteVariant.Dark, BuiltInThemes.Dark.ChartPalette);
    }

    [TestMethod]
    public void Light_KeepsThePreThemingColors()
    {
        // What the front ends hard-coded before theming: WPF's DarkRed/DimGray output styles and
        // SteelBlue info icon, the TUI's green/amber/red status line.
        Assert.AreEqual("#8B0000", BuiltInThemes.Light[ThemeRole.OutputError].ToHex());
        Assert.AreEqual("#696969", BuiltInThemes.Light[ThemeRole.OutputStatus].ToHex());
        Assert.AreEqual("#4682B4", BuiltInThemes.Light[ThemeRole.Accent].ToHex());
        Assert.AreEqual("#78C878", BuiltInThemes.Light[ThemeRole.StatusConnected].ToHex());
        Assert.AreEqual("#E6C85A", BuiltInThemes.Light[ThemeRole.StatusConnecting].ToHex());
        Assert.AreEqual("#AA2828", BuiltInThemes.Light[ThemeRole.StatusDisconnected].ToHex());
    }

    [TestMethod]
    [DataRow("#1E1E1E", true)]
    [DataRow("#ffffff", true)]
    [DataRow("1E1E1E", false)]
    [DataRow("#1E1E1", false)]
    [DataRow("#GG0000", false)]
    [DataRow("red", false)]
    [DataRow("", false)]
    public void ThemeColor_ParsesOnlyHashRrGgBb(string text, bool valid)
    {
        Assert.AreEqual(valid, ThemeColor.TryParse(text, out _));
    }

    [TestMethod]
    public void ContrastRatio_MatchesWcag()
    {
        Assert.AreEqual(21.0, ThemeColor.ContrastRatio(ThemeColor.Parse("#000000"), ThemeColor.Parse("#FFFFFF")), 0.01);
        Assert.AreEqual(1.0, ThemeColor.ContrastRatio(ThemeColor.Parse("#777777"), ThemeColor.Parse("#777777")), 0.01);
    }

    [TestMethod]
    public void ThemeFile_OverridesOnlyTheRolesItNames_OnItsBase()
    {
        var result = ThemeFile.Load(WriteTheme("solarized.json", """
            {
              // comments and trailing commas are fine
              "name": "Solarized Dark",
              "basedOn": "dark",
              "colors": { "background": "#002B36", "outputError": "#DC322F", },
            }
            """));

        Assert.IsEmpty(result.Errors);
        var theme = result.Theme!;
        Assert.AreEqual("Solarized Dark", theme.Name);
        Assert.AreEqual("#002B36", theme[ThemeRole.Background].ToHex());
        Assert.AreEqual("#DC322F", theme[ThemeRole.OutputError].ToHex());
        Assert.AreEqual(BuiltInThemes.Dark[ThemeRole.Foreground], theme[ThemeRole.Foreground], "Unnamed roles keep the base theme's colors.");
        Assert.AreEqual(ChartPaletteVariant.Dark, theme.ChartPalette, "The chart palette variant defaults to the base's.");
        Assert.IsTrue(theme.IsDark);
    }

    [TestMethod]
    public void ThemeFile_WithoutAName_IsNamedAfterTheFile_AndDefaultsToLight()
    {
        var result = ThemeFile.Load(WriteTheme("sepia.json", """{ "colors": { "background": "#F4ECD8" }, "chartPalette": "light" }"""));

        Assert.AreEqual("sepia", result.Theme!.Name);
        Assert.AreEqual(BuiltInThemes.Light[ThemeRole.Foreground], result.Theme[ThemeRole.Foreground]);
    }

    [TestMethod]
    public void ThemeFile_ReportsEveryProblem_AndIsRejected()
    {
        var result = ThemeFile.Load(WriteTheme("broken.json", """
            {
              "name": "dark",
              "basedOn": "purple",
              "chartPalette": "rainbow",
              "colours": {},
              "colors": { "backgroud": "#000000", "foreground": "white", "accent": 12 }
            }
            """));

        Assert.IsNull(result.Theme);
        var errors = string.Join("\n", result.Errors);
        StringAssert.Contains(errors, "reserved");
        StringAssert.Contains(errors, "\"basedOn\" must be \"light\" or \"dark\"");
        StringAssert.Contains(errors, "\"chartPalette\"");
        StringAssert.Contains(errors, "unknown setting \"colours\"");
        StringAssert.Contains(errors, "unknown color role \"backgroud\"");
        StringAssert.Contains(errors, "\"foreground\" must be a \"#RRGGBB\" color");
        StringAssert.Contains(errors, "\"accent\" must be a \"#RRGGBB\" color");
        Assert.IsTrue(result.Errors.All(e => e.StartsWith("broken.json:", StringComparison.Ordinal)), "Every message names the file.");
    }

    [TestMethod]
    [DataRow("{ not json", "not valid JSON")]
    [DataRow("[1, 2]", "must be a JSON object")]
    [DataRow("", "not valid JSON")]
    public void ThemeFile_ThatIsntAnObject_IsAClearError_NotAnException(string json, string expected)
    {
        var result = ThemeFile.Load(WriteTheme("bad.json", json));

        Assert.IsNull(result.Theme);
        StringAssert.Contains(result.Errors.Single(), expected);
    }

    [TestMethod]
    public void ThemeFile_WithUnreadableContrast_LoadsWithAWarning()
    {
        var result = ThemeFile.Load(WriteTheme("murky.json", """{ "colors": { "foreground": "#EEEEEE" } }"""));

        Assert.IsNotNull(result.Theme, "Low contrast is reported, not rejected - it's the user's theme.");
        StringAssert.Contains(result.Warnings.Single(w => w.Contains("foreground on background", StringComparison.Ordinal)), "may be hard to read");
    }

    [TestMethod]
    public void Catalog_LoadsGoodFiles_ReportsBadOnes_AndSkipsDuplicates()
    {
        WriteTheme("a-good.json", """{ "name": "Good", "colors": { "accent": "#123456" } }""");
        WriteTheme("b-bad.json", """{ "colors": { "nope": "#000000" } }""");
        WriteTheme("c-dupe.json", """{ "name": "good" }""");
        WriteTheme("ignored.txt", "not a theme");

        var catalog = ThemeCatalog.Load(_directory);

        Assert.AreEqual("Good", catalog.UserThemes.Single().Name);
        CollectionAssert.AreEqual(new[] { "light", "dark", "system", "Good" }, catalog.SelectionNames.ToArray());
        Assert.IsTrue(catalog.Problems.Any(p => p.StartsWith("b-bad.json:", StringComparison.Ordinal)));
        Assert.IsTrue(catalog.Problems.Any(p => p.StartsWith("c-dupe.json:", StringComparison.Ordinal) && p.Contains("already named", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Catalog_ForAMissingDirectory_IsJustTheBuiltIns()
    {
        var catalog = ThemeCatalog.Load(Path.Combine(_directory, "does-not-exist"));

        Assert.IsEmpty(catalog.UserThemes);
        Assert.IsEmpty(catalog.Problems);
    }

    [TestMethod]
    public void Resolve_SystemFollowsTheOsHint_AndUnknownNamesFallBackToSystemWithAWarning()
    {
        var catalog = ThemeCatalog.BuiltInOnly;

        Assert.AreSame(BuiltInThemes.Dark, catalog.Resolve("system", out var none, () => true));
        Assert.IsNull(none);
        Assert.AreSame(BuiltInThemes.Light, catalog.Resolve("System", out _, () => false));
        Assert.AreSame(BuiltInThemes.Light, catalog.Resolve(null, out _, () => false), "Nothing chosen means system.");
        Assert.AreSame(BuiltInThemes.Dark, catalog.Resolve("DARK", out _, () => false), "Names are case-insensitive.");

        Assert.AreSame(BuiltInThemes.Dark, catalog.Resolve("no-such-theme", out var warning, () => true));
        StringAssert.Contains(warning, "Unknown theme \"no-such-theme\"");
    }

    [TestMethod]
    [DataRow("15;0", true)]
    [DataRow("0;15", false)]
    [DataRow("15;default;8", true)]
    [DataRow("7;7", false)]
    [DataRow(null, false)]
    [DataRow("garbage", false)]
    public void ColorFgBg_DarkBackgroundsMeanDark(string? value, bool dark)
    {
        Assert.AreEqual(dark, SystemThemeDetector.PrefersDarkFromColorFgBg(value));
    }

    [TestMethod]
    public void Preferences_RoundTrip_AndACorruptFileFallsBackWithAProblem()
    {
        var store = new AppPreferencesStore(Path.Combine(_directory, "nested", "preferences.json"));
        Assert.IsNull(store.Load().Theme, "Missing file = defaults.");
        Assert.IsNull(store.LoadProblem);

        Assert.IsNull(store.Save(new AppPreferences { Theme = "dark" }));
        Assert.AreEqual("dark", store.Load().Theme);

        File.WriteAllText(store.Path, "{ broken");
        Assert.IsNull(store.Load().Theme);
        StringAssert.Contains(store.LoadProblem, "Could not read preferences");
    }

    [TestMethod]
    public void Initialize_CommandLineBeatsTheSavedPreference_WhichBeatsSystem_AndIsNotPersisted()
    {
        var store = new AppPreferencesStore(Path.Combine(_directory, "preferences.json"));
        store.Save(new AppPreferences { Theme = "dark" });
        ActiveTheme.PrefersDark = () => false;

        ActiveTheme.Initialize(new ConfigurationBuilder().Build(), ThemeCatalog.BuiltInOnly, store);
        Assert.AreSame(BuiltInThemes.Dark, ActiveTheme.Current, "The saved preference applies.");

        var commandLine = new ConfigurationBuilder().AddCommandLine(["--theme", "light"]).Build();
        ActiveTheme.Initialize(commandLine, ThemeCatalog.BuiltInOnly, store);
        Assert.AreSame(BuiltInThemes.Light, ActiveTheme.Current, "--theme wins for this run...");
        Assert.AreEqual("dark", store.Load().Theme, "...without overwriting the saved preference.");

        File.Delete(store.Path);
        ActiveTheme.Initialize(null, ThemeCatalog.BuiltInOnly, store);
        Assert.AreEqual("system", ActiveTheme.Selection);
        Assert.AreSame(BuiltInThemes.Light, ActiveTheme.Current);
    }

    [TestMethod]
    public void Initialize_ReportsProblems_WithoutFailing()
    {
        WriteTheme("bad.json", "{");
        var store = new AppPreferencesStore(Path.Combine(_directory, "preferences.json"));
        var commandLine = new ConfigurationBuilder().AddCommandLine(["--theme", "neon"]).Build();

        var problems = ActiveTheme.Initialize(commandLine, ThemeCatalog.Load(_directory), store);

        Assert.IsTrue(problems.Any(p => p.StartsWith("bad.json:", StringComparison.Ordinal)));
        Assert.IsTrue(problems.Any(p => p.Contains("Unknown theme \"neon\"", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(problems.ToArray(), ActiveTheme.StartupProblems.ToArray());
        Assert.AreEqual("system", ActiveTheme.Selection);
    }

    [TestMethod]
    public void Select_PersistsTheChoice_RaisesChanged_AndCanonicalizesTheName()
    {
        WriteTheme("amber.json", """{ "name": "Amber", "basedOn": "dark" }""");
        var store = new AppPreferencesStore(Path.Combine(_directory, "preferences.json"));
        ActiveTheme.Initialize(null, ThemeCatalog.Load(_directory), store);
        var raised = 0;
        void OnChanged(object? sender, EventArgs e) => raised++;
        ActiveTheme.Changed += OnChanged;
        try
        {
            Assert.IsNull(ActiveTheme.Select("AMBER"));

            Assert.AreEqual(1, raised);
            Assert.AreEqual("Amber", ActiveTheme.Selection);
            Assert.AreEqual("Amber", ActiveTheme.Current.Name);
            Assert.AreEqual("Amber", store.Load().Theme);
        }
        finally
        {
            ActiveTheme.Changed -= OnChanged;
        }
    }

    [TestMethod]
    public void TheTheme_IsNeverPartOfAConnectionProfile()
    {
        var configuration = new ConfigurationBuilder().AddCommandLine(["--transport", "tcp", "--host", "h", "--port", "1", "--theme", "dark"]).Build();
        var options = new CliOptions();
        DevTermConfiguration.Bind(configuration, options);

        Assert.DoesNotContain("Theme", DevTermConfiguration.ToProfileJson(options));
        Assert.IsNull(typeof(CliOptions).GetProperty(ActiveTheme.ConfigurationKey), "Read straight from configuration, not a connection option.");
    }
}
