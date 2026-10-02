using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// <see cref="ThemeBuilderState"/>: tracks per-role overrides against a seed theme, builds a live
/// preview theme, and saves a diff-only user theme file - a built-in is never edited in place, and
/// seeding from another user theme still produces a file valid against <see cref="ThemeFile.Parse"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ThemeBuilderStateTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "devterm-theme-builder-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void Indexer_ReadsTheSeedUntilOverridden()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");

        Assert.AreEqual(BuiltInThemes.Light[ThemeRole.Background], state[ThemeRole.Background]);
        Assert.IsFalse(state.IsOverridden(ThemeRole.Background));

        state.Set(ThemeRole.Background, ThemeColor.Parse("#112233"));
        Assert.AreEqual(ThemeColor.Parse("#112233"), state[ThemeRole.Background]);
        Assert.IsTrue(state.IsOverridden(ThemeRole.Background));
        Assert.AreEqual(1, state.Overrides.Count);
    }

    [TestMethod]
    public void Set_BackToTheSeedsOwnColor_ClearsTheOverride()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
        state.Set(ThemeRole.Background, ThemeColor.Parse("#112233"));

        state.Set(ThemeRole.Background, BuiltInThemes.Light[ThemeRole.Background]);

        Assert.IsFalse(state.IsOverridden(ThemeRole.Background));
        Assert.IsEmpty(state.Overrides);
    }

    [TestMethod]
    public void ResetToSeed_RemovesAnOverride()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme");
        state.Set(ThemeRole.Accent, ThemeColor.Parse("#112233"));

        state.ResetToSeed(ThemeRole.Accent);

        Assert.IsFalse(state.IsOverridden(ThemeRole.Accent));
        Assert.AreEqual(BuiltInThemes.Light[ThemeRole.Accent], state[ThemeRole.Accent]);
    }

    [TestMethod]
    public void Build_AppliesOverridesOnTopOfTheSeed_WithTheEditedName()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Dark, "Midnight") { ChartPalette = ChartPaletteVariant.Light };
        state.Set(ThemeRole.Background, ThemeColor.Parse("#000011"));

        var built = state.Build();

        Assert.AreEqual("Midnight", built.Name);
        Assert.AreEqual(ChartPaletteVariant.Light, built.ChartPalette);
        Assert.AreEqual(ThemeColor.Parse("#000011"), built[ThemeRole.Background]);
        Assert.AreEqual(BuiltInThemes.Dark[ThemeRole.Foreground], built[ThemeRole.Foreground], "Untouched roles still come from the seed.");
    }

    [TestMethod]
    public void ContrastWarnings_ReflectTheEditsSoFar()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "Murky");
        Assert.IsEmpty(state.ContrastWarnings());

        state.Set(ThemeRole.Foreground, ThemeColor.Parse("#EEEEEE"));

        Assert.IsNotEmpty(state.ContrastWarnings());
    }

    [TestMethod]
    public void Save_FromABuiltInSeed_WritesOnlyTheOverriddenRoles()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "My Theme") { ChartPalette = ChartPaletteVariant.Dark };
        state.Set(ThemeRole.Background, ThemeColor.Parse("#F4ECD8"));
        state.Set(ThemeRole.Accent, ThemeColor.Parse("#2E6DA4")); // happens to already equal the seed's own value

        var path = Path.Combine(_directory, "my-theme.json");
        Assert.IsNull(state.Save(path));

        var loaded = ThemeFile.Load(path);
        Assert.IsEmpty(loaded.Errors);
        Assert.AreEqual("My Theme", loaded.Theme!.Name);
        Assert.AreEqual(ChartPaletteVariant.Dark, loaded.Theme.ChartPalette);
        Assert.AreEqual(ThemeColor.Parse("#F4ECD8"), loaded.Theme[ThemeRole.Background]);
        Assert.AreEqual(BuiltInThemes.Light[ThemeRole.Foreground], loaded.Theme[ThemeRole.Foreground], "Never-touched roles still resolve from basedOn.");
    }

    [TestMethod]
    public void Save_FromAUserThemeSeed_StillWritesEveryRoleThatDiffersFromTheCanonicalBase()
    {
        // A user theme already overriding "foreground" off Light, with no further edits made here.
        var previousUserTheme = BuiltInThemes.Light.With("Earlier", new Dictionary<ThemeRole, ThemeColor> { [ThemeRole.Foreground] = ThemeColor.Parse("#123456") });
        var state = new ThemeBuilderState(previousUserTheme, "Later");

        var path = Path.Combine(_directory, "later.json");
        Assert.IsNull(state.Save(path));

        var loaded = ThemeFile.Load(path);
        Assert.IsEmpty(loaded.Errors);
        Assert.AreEqual(ThemeColor.Parse("#123456"), loaded.Theme![ThemeRole.Foreground], "The seed's own override is preserved even though this session never touched it.");
    }

    [TestMethod]
    public void Save_PicksDarkAsTheBaseName_WhenTheResultIsDark()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "Inverted");
        foreach (var role in Enum.GetValues<ThemeRole>())
        {
            state.Set(role, BuiltInThemes.Dark[role]);
        }

        var path = Path.Combine(_directory, "inverted.json");
        Assert.IsNull(state.Save(path));

        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "\"basedOn\": \"dark\"");
    }

    [TestMethod]
    public void Save_ToAnUncreatedThemesDirectory_CreatesIt()
    {
        var state = new ThemeBuilderState(BuiltInThemes.Light, "Fresh");
        var path = Path.Combine(_directory, "not-yet-created", "fresh.json");

        Assert.IsNull(state.Save(path));
        Assert.IsTrue(File.Exists(path));
    }
}
