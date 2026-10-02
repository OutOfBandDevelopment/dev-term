using DevTerm.DeviceManifests;
using DevTerm.DeviceManifests.Editing;
using DevTerm.Test.Utilities;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Captures the expression picker's states for docs/user-guide/expression-builder.md: a clean build, a syntax error, an
/// unknown-value warning, a text expression, and Channels mode. Each runs the real dialog (see <see cref="TuiReview.Modal"/>)
/// at 80x25 in the light theme and copies the capture into <c>docs/user-guide/images/</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ExpressionPickerScreenshotTests
{
    private static readonly ValuePath[] _paths =
    [
        new("volts", ValuePathType.Number, ValuePathSource.Control, "Voltage", "V", 0, 30),
        new("amps", ValuePathType.Number, ValuePathSource.Control, "Current", "A", 0, 5),
        new("status", ValuePathType.Text, ValuePathSource.ResponsePattern, "Status"),
    ];

    [TestCleanup]
    public void Cleanup() => TuiReview.ResetTheme();

    [TestMethod]
    public void Build() => Capture("tui-expression-picker", "round({volts} * {amps}, 1)");

    [TestMethod]
    public void SyntaxError() => Capture("tui-expression-picker-error", "round({volts} * ");

    [TestMethod]
    public void UnknownValue() => Capture("tui-expression-picker-warning", "{volts} * {watts}");

    [TestMethod]
    public void TextExpression() => Capture("tui-expression-picker-text", "matches({status}, 'READY') ? 100 : 0");

    [TestMethod]
    public void ChannelsMode() => Capture("tui-expression-picker-channels", "volts:Volts:#FF6600; amps", PickerMode.Channels);

    [TestMethod]
    public void ParameterExpressionsMode() => Capture("tui-expression-picker-parameters", "round({volts} * 100, 0); {amps}", PickerMode.ExpressionList);

    [TestMethod]
    public void FindFiltersTheList() => Capture("tui-expression-picker-find", "{volts}", filter: "amp");

    private static void Capture(string imageName, string text, PickerMode mode = PickerMode.Expression, string? filter = null)
    {
        TuiReview.Modal(
            "guide-" + imageName,
            80,
            25,
            "light",
            app => new Window { Title = "dev-term", Width = Dim.Fill(), Height = Dim.Fill() },
            app => ExpressionPickerDialog.Show(app, new ExpressionPickerViewModel(_paths, text, seed: 7, mode: mode) { Filter = filter ?? string.Empty }));

        var source = Path.Combine(TuiReview.Directory, $"guide-{imageName}-80x25-light.png");
        var images = Path.Combine(TuiReview.Directory, "..", "..", "..", "docs", "user-guide", "images");
        File.Copy(source, Path.Combine(Path.GetFullPath(images), imageName + ".png"), overwrite: true);
    }
}
