using DevTerm.Configuration;
using DevTerm.Core.StreamContent;
using DevTerm.DeviceManifests.Editing;
using DevTerm.Logging.Playback;
using DevTerm.Test.Utilities;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace DevTerm.Console.Tests;

/// <summary>
/// Layout regression tests (see <see cref="TuiLayoutAssert"/>) for the TUI's tool windows: the
/// device manifest editor (<see cref="ManifestEditorMode"/>, one outline node of every kind, and the
/// live preview), Playback (<see cref="PlaybackMode"/>) and the Stream Monitor
/// (<see cref="StreamMonitorMode"/>), at each review size, the editor in both themes. Each also saves a
/// review PNG under <c>artifacts/ui-review/tui/</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiToolWindowLayoutTests
{
    private static readonly string _bundled = Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo");
    private static string _directory = string.Empty;
    private static ManifestEditorParts? _editor;

    [ClassInitialize]
    public static void CreateDirectory(TestContext context)
    {
        _directory = Path.Combine(Path.GetTempPath(), "devterm-layout-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [ClassCleanup]
    public static void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestCleanup]
    public void Cleanup() => TuiReview.ResetTheme();

    public static IEnumerable<object[]> SizesAndThemes =>
        from theme in new[] { "light", "dark" }
        from size in TuiReview.Sizes
        select new object[] { size.Width, size.Height, theme };

    public static IEnumerable<object[]> Sizes => TuiReview.Sizes.Select(s => new object[] { s.Width, s.Height });

    private static Func<IApplication, View> Editor() => app =>
    {
        var editor = new ManifestEditorViewModel(Path.Combine(_directory, "user-manifests"), Path.Combine(AppContext.BaseDirectory, "manifests"));
        Assert.IsTrue(editor.Open(_bundled), editor.StatusMessage);
        _editor = ManifestEditorMode.BuildWindow(app, editor);
        return _editor.Window;
    };

    /// <summary>One outline entry of every kind (for controls, of every control type): the first of each.</summary>
    private static IEnumerable<(string Slug, int Index)> RepresentativeNodes(ManifestEditorViewModel editor)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < editor.Nodes.Count; i++)
        {
            var node = editor.Nodes[i];
            var kind = node.Kind == ManifestNodeKind.Control ? $"control-{node.Display.Trim().Split(':')[0]}" : node.Kind.ToString().ToLowerInvariant();
            if (seen.Add(kind))
            {
                yield return (kind.ToLowerInvariant(), i);
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void ManifestEditor_EveryKindOfOutlineNode(int width, int height, string theme)
    {
        var probe = new ManifestEditorViewModel(Path.Combine(_directory, "user-manifests"), Path.Combine(AppContext.BaseDirectory, "manifests"));
        Assert.IsTrue(probe.Open(_bundled), probe.StatusMessage);
        var nodes = RepresentativeNodes(probe).ToList();
        Assert.IsGreaterThanOrEqualTo(9, nodes.Count, "Identity, both headings, a command, a parameter, a pattern, the panel, a section and a control at least.");

        foreach (var (slug, index) in nodes)
        {
            TuiReview.Screen($"manifest-editor-{slug}", width, height, theme, Editor(), (app, _) =>
            {
                _editor!.Outline.SelectedItem = index;
            });
        }
    }

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void ManifestEditor_Preview(int width, int height, string theme) =>
        TuiReview.Screen("manifest-editor-preview", width, height, theme, Editor(), (app, _) => _editor!.ShowPreview());

    [TestMethod]
    [DynamicData(nameof(Sizes))]
    public void Playback_PartWayThroughWithANote(int width, int height)
    {
        var controller = new PlaybackPresenters().Open(PlaybackModeTests.WriteSampleLog(_directory, $"layout-{width}x{height}.jsonl"), new ManualTimeProvider());
        PlaybackWindowParts? parts = null;
        TuiReview.Screen("playback", width, height, "light", app =>
        {
            parts = PlaybackMode.BuildWindow(app, controller);
            return parts.Window;
        }, (app, _) =>
        {
            parts!.Do(() => controller.SetPresenters(["ascii", "hex"]));
            parts.Do(() => controller.SeekTo(5));
            parts.Do(() => controller.AddNote("IDN reply is correct"));
            parts.Do(controller.Step);
            controller.MarkIn();
        });
    }

    [TestMethod]
    [DynamicData(nameof(Sizes))]
    public async Task StreamMonitor_WithCaptures(int width, int height)
    {
        await using var bench = await StreamMonitorBench.StartAsync("Rigol DG1062Z", Path.Combine(_directory, "exports"));
        await bench.CaptureAsync(StreamContentSamples.Png());
        bench.Time.Advance(TimeSpan.FromSeconds(41));
        await bench.CaptureAsync(StreamContentSamples.Hpgl());

        TuiReview.Screen("stream-monitor", width, height, "light", app => StreamMonitorMode.BuildWindow(app, bench.Monitor).Window);
    }
}
