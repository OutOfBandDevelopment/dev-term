using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace DevTerm.Console.Tests;

/// <summary>
/// Layout regression tests (see <see cref="TuiLayoutAssert"/>) for the TUI Connection Editor
/// (<see cref="ConfigureMode"/>): every transport, scrolled to the top and to the bottom, with a
/// validation error and with the "(not found)" hints showing, at each review size and in both themes.
/// Each also saves a review PNG under <c>artifacts/ui-review/tui/</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiConfigureLayoutTests
{
    private static string _profiles = string.Empty;
    private static ConfigureWindowParts? _parts;

    [ClassInitialize]
    public static void CreateProfiles(TestContext context)
    {
        _profiles = Path.Combine(Path.GetTempPath(), "devterm-layout-profiles-" + Guid.NewGuid().ToString("N"));
        var store = new ConnectionProfileStore(_profiles);
        store.Save("bench-scope", new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23" });
        store.Save("korad-psu", new CliOptions { Transport = "serial", Port = "COM5", Baud = 9600 });
        store.Save("k8055", new CliOptions { Transport = "hid", VendorId = 4303, ProductId = 21760 });
    }

    [ClassCleanup]
    public static void DeleteProfiles()
    {
        if (Directory.Exists(_profiles))
        {
            Directory.Delete(_profiles, recursive: true);
        }
    }

    [TestCleanup]
    public void Cleanup() => TuiReview.ResetTheme();

    private static CliOptions For(string transport) => transport switch
    {
        "serial" => new CliOptions { Transport = "serial", Port = "COM3", Baud = 9600, Presenter = ["ascii"], Description = "Tektronix 2230 bench scope" },
        "tcp" => new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii", "hex"], Description = "Tektronix 2230 bench scope" },
        "hid" => new CliOptions { Transport = "hid", VendorId = 4216, ProductId = 63560, Presenter = ["hex"] },
        "usbtmc" => new CliOptions { Transport = "usbtmc", VendorId = 6833, ProductId = 1603, Presenter = ["scpi"], ScpiProfile = "Rigol DS1102E" },
        _ => new CliOptions { Transport = "loopback", Presenter = ["ascii"] },
    };

    private static Func<IApplication, View> Editor(CliOptions initial, string? validationError = null) => app =>
    {
        _parts = ConfigureMode.BuildWindow(app, initial, validationError, new ConnectionProfileStore(_profiles));
        return _parts.Window;
    };

    private static void ScrollToBottom(IApplication app, View window)
    {
        var form = _parts!.FormContent;
        form.Viewport = form.Viewport with { Y = Math.Max(0, form.GetContentSize().Height - form.Viewport.Height) };
    }

    public static IEnumerable<object[]> Transports =>
        from transport in new[] { "serial", "tcp", "hid", "usbtmc", "loopback" }
        from size in TuiReview.Sizes
        select new object[] { transport, size.Width, size.Height };

    public static IEnumerable<object[]> SizesAndThemes =>
        from theme in new[] { "light", "dark" }
        from size in TuiReview.Sizes
        select new object[] { size.Width, size.Height, theme };

    [TestMethod]
    [DynamicData(nameof(Transports))]
    public void EachTransport_ScrolledToTheTop(string transport, int width, int height) =>
        TuiReview.Screen($"configure-{transport}-top", width, height, "light", Editor(For(transport)));

    [TestMethod]
    [DynamicData(nameof(Transports))]
    public void EachTransport_ScrolledToTheBottom(string transport, int width, int height) =>
        TuiReview.Screen($"configure-{transport}-bottom", width, height, "light", Editor(For(transport)), ScrollToBottom);

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void WithAValidationError(int width, int height, string theme) =>
        TuiReview.Screen("configure-validation-error", width, height, theme, Editor(new CliOptions { Transport = "serial" }, "Missing required '--port' for the serial transport."));

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void WithTheNotFoundHints(int width, int height, string theme)
    {
        // Neither a COM99 nor a 0x1234:0x5678 HID device exists on any machine this runs on, so both
        // "(not found)" hints show - the serial one on the serial form, the USB one on the HID form.
        TuiReview.Screen("configure-serial-not-found", width, height, theme, Editor(new CliOptions { Transport = "serial", Port = "COM99", Presenter = ["ascii"] }));
        TuiReview.Screen("configure-hid-not-found", width, height, theme, Editor(new CliOptions { Transport = "hid", VendorId = 0x1234, ProductId = 0x5678, Presenter = ["hex"] }));
    }
}
