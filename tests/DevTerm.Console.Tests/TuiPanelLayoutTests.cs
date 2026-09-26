using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.DeviceManifests;
using DevTerm.Devices.Busylight;
using DevTerm.Devices.K8055;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace DevTerm.Console.Tests;

/// <summary>
/// Layout regression tests (see <see cref="TuiLayoutAssert"/>) for every TUI control panel
/// (<see cref="ControlPanelMode"/>): the K8055, the Busylight (with and without a custom color), every
/// bundled SCPI profile plus Generic, and the bundled Loopback Sensor Demo manifest - each scrolled to
/// the top and to the bottom, at each review size, a few in the dark theme too. Each also saves a review
/// PNG under <c>artifacts/ui-review/tui/</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiPanelLayoutTests
{
    private static ControlPanelWindowParts? _parts;

    [TestCleanup]
    public void Cleanup()
    {
        TuiReview.ResetTheme();
        LastPickedColors.Forget("customColor");
    }

    private static Func<IApplication, View> Panel(UiDefinition definition, IControlSurface surface, IPresenter? structured, string title) => app =>
    {
        SectionExpansionState.Forget(definition.Name);
        _parts = ControlPanelMode.BuildWindow(app, definition, surface, structured, title);
        return _parts.Window;
    };

    private static void ScrollToBottom(IApplication app, View window)
    {
        var form = _parts!.FormContent;
        form.Viewport = form.Viewport with { Y = Math.Max(0, form.GetContentSize().Height - form.Viewport.Height) };
    }

    private static Session NewSession() => new(new FakeTransport(), new Pipeline([]));

    public static IEnumerable<object[]> Sizes => TuiReview.Sizes.Select(s => new object[] { s.Width, s.Height });

    public static IEnumerable<object[]> SizesAndThemes =>
        from theme in new[] { "light", "dark" }
        from size in TuiReview.Sizes
        select new object[] { size.Width, size.Height, theme };

    public static IEnumerable<ScpiInstrumentProfile> Profiles => [ScpiProfileCatalog.Generic, .. ScpiProfileCatalog.All];

    public static IEnumerable<object[]> ProfilesAndSizes =>
        from profile in Profiles
        from size in TuiReview.Sizes
        select new object[] { profile.Name, size.Width, size.Height };

    private static string Slug(string name) => new([.. name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]);

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void K8055(int width, int height, string theme)
    {
        var session = NewSession();
        var build = Panel(K8055UiDefinition.Build(), new K8055ControlSurface(session), new K8055Decoder(), "dev-term — K8055 Control Panel");
        TuiReview.Screen("panel-k8055-top", width, height, theme, build);
        TuiReview.Screen("panel-k8055-bottom", width, height, theme, build, ScrollToBottom);
    }

    [TestMethod]
    [DynamicData(nameof(Sizes))]
    public void Busylight(int width, int height)
    {
        LastPickedColors.Forget("customColor");
        var build = Panel(BusylightUiDefinition.Build(), new BusylightControlSurface(NewSession()), null, "dev-term — Busylight Control Panel");
        TuiReview.Screen("panel-busylight", width, height, "light", build);
    }

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void Busylight_WithACustomColor(int width, int height, string theme)
    {
        LastPickedColors.Set("customColor", (0xFF, 0x66, 0x00));
        var build = Panel(BusylightUiDefinition.Build(), new BusylightControlSurface(NewSession()), null, "dev-term — Busylight Control Panel");
        TuiReview.Screen("panel-busylight-custom", width, height, theme, build);
    }

    [TestMethod]
    [DynamicData(nameof(ProfilesAndSizes))]
    public void EachScpiProfile(string profileName, int width, int height)
    {
        var profile = Profiles.Single(p => p.Name == profileName);
        var presenter = new ScpiReplyPresenter();
        var build = Panel(ScpiUiDefinitionBuilder.Build(profile), new ScpiControlSurface(NewSession(), profile, presenter), presenter, $"dev-term — {profile.Name}");
        TuiReview.Screen($"panel-scpi-{Slug(profile.Name)}-top", width, height, "light", build);
        TuiReview.Screen($"panel-scpi-{Slug(profile.Name)}-bottom", width, height, "light", build, ScrollToBottom);
    }

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void ScpiPanel_WithAFocusedControlsPreview(int width, int height, string theme)
    {
        // The 34401A: its footer shows what the focused button sends, and its widest row scrolls sideways.
        var profile = ScpiProfileCatalog.All.Single(p => p.Name.Contains("34401A", StringComparison.OrdinalIgnoreCase));
        var build = Panel(ScpiUiDefinitionBuilder.Build(profile), new ScpiControlSurface(NewSession(), profile, tracker: null), null, $"dev-term — {profile.Name}");
        TuiReview.Screen("panel-scpi-preview", width, height, theme, build, (app, _) => _parts!.ControlViews["confFres.send"].SetFocus());
    }

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void LoopbackSensorDemoManifest(int width, int height, string theme)
    {
        var manifest = DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));
        using var panel = ManifestPanel.Attach(NewSession(), manifest);
        Func<IApplication, View> build = app =>
        {
            SectionExpansionState.Forget(manifest.Name);
            _parts = ManifestPanelMode.BuildWindow(app, panel);
            return _parts.Window;
        };
        TuiReview.Screen("panel-manifest-top", width, height, theme, build);
        TuiReview.Screen("panel-manifest-bottom", width, height, theme, build, ScrollToBottom);
    }
}
