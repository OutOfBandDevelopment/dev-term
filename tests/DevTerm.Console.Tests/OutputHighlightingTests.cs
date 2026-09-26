using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI output pane colors lines by their source tag (see OutputHighlighting): [error] lines
/// render in the theme's outputError, [dev-term] status lines in its outputStatus, device output in the default color -
/// checked on the real rendered cells, not just the text.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class OutputHighlightingTests
{
    [TestMethod]
    public void ErrorAndStatusLines_RenderInTheirOwnColors_DeviceLinesDoNot()
    {
        var session = new Session(new FakeTransport(), new Pipeline([]));
        var ascii = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] };

        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = TuiMode.BuildWindow(app, session, TuiTestRunner.CatalogFor(ascii, options), options, TuiTestRunner.EmptyProfiles(), "Could not open the connection: refused.");
            parts.Output.Text = string.Join('\n', parts.Output.Text, TuiMode.StatusLine("Connected."), "[ascii] ID TEK/2230");

            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                var buffer = app.Driver!.GetOutputBuffer();

                // The output pane's first three rows (below the menu bar) hold the three lines; col 3
                // is inside each line's leading tag. Row 0 is the window border, row 1 the menu bar.
                Terminal.Gui.Drawing.Color ForegroundAt(int row) => buffer.Contents![row, 3].Attribute!.Value.Foreground;
                var error = ForegroundAt(2);
                var status = ForegroundAt(3);
                var device = ForegroundAt(4);

                Assert.AreEqual(TuiTheme.ToColor(ActiveTheme.Current[ThemeRole.OutputError]), error, "[error] lines use the theme's outputError.");
                Assert.AreEqual(TuiTheme.ToColor(ActiveTheme.Current[ThemeRole.OutputStatus]), status, "[dev-term] lines use the theme's outputStatus.");
                Assert.AreNotEqual(error, device);
                Assert.AreNotEqual(status, device);
            }
            finally
            {
                app.End(token);
            }
        });
    }
}
