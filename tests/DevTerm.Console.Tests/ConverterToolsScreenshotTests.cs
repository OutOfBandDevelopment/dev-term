using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Captures the "Converter tools" dialog for docs/user-guide/stream-monitor.md and docs/specs/converter-tools-editor.md:
/// the real dialog (see <see cref="TuiReview.Modal"/>) at 80x25 in the light theme, copied into <c>docs/user-guide/images/</c>.
/// Modal also runs the layout checks, so a clipped or overlapping field fails here.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ConverterToolsScreenshotTests
{
    [TestMethod]
    public void ConverterToolsDialog_IsCaptured()
    {
        StreamConvertToolOptions[] tools =
        [
            new() { Name = "gs", Path = @"C:\Program Files\gs\bin\gswin64c.exe", Arguments = "-q -dNOPAUSE -sDEVICE=png16m -r{dpi} -sOutputFile={output} {input}", Formats = "ps, eps", OutputExtension = "png", Dpi = 150 },
            new() { Name = "gpcl", Path = @"C:\Program Files\gs\bin\gpcl6win64.exe", Arguments = "-sDEVICE=png16m -r{dpi} -o {output} {input}", Formats = "pcl", OutputExtension = "png", Dpi = 200 },
        ];

        TuiReview.Modal(
            "guide-tui-converter-tools",
            80,
            25,
            "light",
            app => new Window { Title = "dev-term", Width = Dim.Fill(), Height = Dim.Fill() },
            app => ConverterToolsDialog.Show(app, tools));

        var source = Path.Combine(TuiReview.Directory, "guide-tui-converter-tools-80x25-light.png");
        var images = Path.Combine(TuiReview.Directory, "..", "..", "..", "docs", "user-guide", "images");
        File.Copy(source, Path.Combine(Path.GetFullPath(images), "tui-converter-tools.png"), overwrite: true);
    }
}
