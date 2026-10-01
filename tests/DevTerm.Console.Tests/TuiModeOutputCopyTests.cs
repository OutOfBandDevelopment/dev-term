using DevTerm.Test.Utilities;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Confirms the output pane's underlying control (a read-only <c>Terminal.Gui.Editor.Editor</c> —
/// see <c>TuiMode</c>'s and <c>PlaybackMode</c>'s own <c>ReadOnly = true</c> output editors) still
/// supports selecting and copying its text, and that Ctrl+C is actually bound to
/// <see cref="Command.Copy"/> by default rather than relying on the <see cref="Command"/> enum value
/// alone.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiModeOutputCopyTests
{
    [TestMethod]
    public void ReadOnlyEditor_SelectAllThenCopy_CopiesTextAndBindsCtrlC()
    {
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var editor = new Terminal.Gui.Editor.Editor
            {
                X = 0,
                Y = 0,
                Width = 40,
                Height = 10,
                ReadOnly = true,
                Text = "hello world\nsecond line",
            };

            var window = new Window();
            window.Add(editor);
            app.Begin(window);
            app.LayoutAndDraw(true);
            editor.SetFocus();

            var selectAllHandled = editor.InvokeCommand(Command.SelectAll);
            var copyHandled = editor.InvokeCommand(Command.Copy);

            var clip = app.Clipboard!.GetClipboardData();

            Assert.IsTrue(selectAllHandled == true, "SelectAll should be handled");
            Assert.IsTrue(copyHandled == true, "Copy should be handled");
            Assert.AreEqual("hello world\nsecond line", clip);

            var boundCommands = editor.KeyBindings.GetCommands(Key.C.WithCtrl);
            Assert.IsTrue(boundCommands.Contains(Command.Copy), "Ctrl+C should be bound to Copy");
        });
    }
}
