using DevTerm.DeviceManifests;
using DevTerm.Test.Utilities;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Layout regression tests (see <see cref="TuiLayoutAssert"/>) for the TUI's modal dialogs, each run
/// for real (a nested <c>Application.Run</c> under a real run loop - see <see cref="TuiReview.Modal"/>)
/// over a plain window: the SCPI instrument picker, the detected-device list picker (and its "nothing
/// detected" message), the manifest picker, the custom color picker, the one-line prompt (log path,
/// note), and the error/confirmation message boxes, at each review size and in both themes. Each also
/// saves a review PNG under <c>artifacts/ui-review/tui/</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiDialogLayoutTests
{
    [TestCleanup]
    public void Cleanup() => TuiReview.ResetTheme();

    public static IEnumerable<object[]> SizesAndThemes =>
        from theme in new[] { "light", "dark" }
        from size in TuiReview.Sizes
        select new object[] { size.Width, size.Height, theme };

    private static View Background(IApplication app) =>
        new Window { Title = "dev-term — tcp://192.168.0.107:23 (ascii; send as ascii)", Width = Dim.Fill(), Height = Dim.Fill() };

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void ScpiInstrumentPicker(int width, int height, string theme) =>
        TuiReview.Modal("dialog-scpi-picker", width, height, theme, Background, app => TuiMode.PickScpiProfileChoice(app));

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void DetectedDevicePicker(int width, int height, string theme) =>
        TuiReview.Modal("dialog-device-picker", width, height, theme, Background, app => FormRenderer.PickFromList(
            app,
            "Detected HID devices",
            [
                "Velleman K8055 (VID 0x10CF PID 0x5500) - board address 0",
                "Velleman K8055 (VID 0x10CF PID 0x5501) - board address 1",
                "Kuando Busylight Omega (VID 0x27BB PID 0x3BCD) serial 00001234",
            ]));

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void NothingDetectedMessage(int width, int height, string theme) =>
        TuiReview.Modal("dialog-nothing-detected", width, height, theme, Background, app => FormRenderer.PickFromList(
            app,
            "Detected USBTMC devices",
            [],
            "No detected USBTMC device matches the Vendor/Product ID entered (0 means any)."));

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void ManifestPicker(int width, int height, string theme) =>
        TuiReview.Modal("dialog-manifest-picker", width, height, theme, Background, app => ManifestPanelMode.Pick(
            app,
            [
                new ManifestEntry("Loopback Sensor Demo", Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"), "installed"),
                new ManifestEntry("Bench power supply (my copy)", @"C:\Users\someone\.dev-term\manifests\bench-psu", "yours"),
            ]));

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void CustomColorPicker(int width, int height, string theme) =>
        TuiReview.Modal("dialog-color-picker", width, height, theme, Background, app => ControlPanelMode.PickColor(app, 0xFF, 0x66, 0x00));

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void LogPathPrompt(int width, int height, string theme) =>
        TuiReview.Modal("dialog-prompt", width, height, theme, Background, app => PlaybackMode.PromptForText(
            app,
            "Start Logging",
            "Log file:",
            @"C:\Users\someone\.dev-term\logs\20260925-120000_tcp_192.168.0.107_23.jsonl"));

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void UnexpectedErrorMessage(int width, int height, string theme) =>
        TuiReview.Modal("dialog-error", width, height, theme, Background, app => MessageBox.ErrorQuery(
            app,
            "dev-term — unexpected error",
            "An unexpected error occurred and has been ignored so dev-term can keep running:\n\nSystem.InvalidOperationException: Sequence contains no matching element\n   at System.Linq.ThrowHelper.ThrowNoMatchException()",
            "Ok"));

    [TestMethod]
    [DynamicData(nameof(SizesAndThemes))]
    public void ConfirmationMessages(int width, int height, string theme)
    {
        TuiReview.Modal("dialog-confirm-delete", width, height, theme, Background, app => MessageBox.Query(
            app,
            "dev-term",
            "Delete 3 profiles (bench-scope, korad-psu, k8055)? This can't be undone.",
            ["Yes", "No"]));
        TuiReview.Modal("dialog-confirm-zip-conflict", width, height, theme, Background, app => MessageBox.Query(
            app,
            "dev-term",
            "A profile named 'bench-scope' already exists.",
            ["Replace", "Rename", "Skip"]));
    }
}
