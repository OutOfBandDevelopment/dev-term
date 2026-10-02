using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>The Terminal.Gui "Converter tools" dialog, driven through its parts (no key injection, so no injector degradation).</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ConverterToolsDialogTests
{
    private static StreamConvertToolOptions Tool(string name) => new() { Name = name, Path = "gs.exe", Arguments = "{input} {output}", Formats = "ps" };

    private static void Press(Button button) => button.InvokeCommand(Command.Accept);

    [TestMethod]
    public void ShowsTheFirstToolsFields_AndAddEditMoveRemoveChangeTheList()
    {
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var editor = new ConverterToolsEditor([Tool("gs")]);
            var parts = ConverterToolsDialog.Build(app, editor);

            Assert.AreEqual("gs", parts.Name.Text);
            Assert.AreEqual("gs.exe", parts.Path.Text);
            Assert.AreEqual("150", parts.Dpi.Text);

            Press(parts.Add);
            parts.Name.Text = "gpcl";
            parts.Path.Text = "gpcl6.exe";
            Assert.AreEqual(2, editor.Tools.Count);
            Assert.AreEqual("gpcl", editor.Tools[1].Name);

            Press(parts.Up);
            CollectionAssert.AreEqual(new[] { "gpcl", "gs" }, editor.Tools.Select(t => t.Name).ToArray());

            Press(parts.Remove);
            CollectionAssert.AreEqual(new[] { "gs" }, editor.Tools.Select(t => t.Name).ToArray());
        });
    }

    [TestMethod]
    public void Ok_WithAnInvalidList_ShowsTheProblemAndDoesNotAccept()
    {
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ConverterToolsDialog.Build(app, new ConverterToolsEditor([Tool("gs")]));
            parts.Path.Text = string.Empty;

            Press(parts.Ok);

            StringAssert.Contains(parts.Error.Text, "path");
            Assert.IsNull(parts.Accepted);
        });
    }
}
