using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>The WPF "Converter tools" dialog (<see cref="ConverterToolsWindow"/>); constructed, never shown or run modally.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ConverterToolsWindowTests
{
    private static StreamConvertToolOptions Tool(string name) => new() { Name = name, Path = "gs.exe", Arguments = "{input} {output}", Formats = "ps" };

    [TestMethod]
    public void ShowsTheToolsAndTheSelectedOnesFields()
    {
        StaTestRunner.Run(() =>
        {
            var window = new ConverterToolsWindow(new ConverterToolsEditor([Tool("gs"), Tool("gpcl")]));
            StaTestRunner.DoEvents();

            Assert.AreEqual(2, window.List.Items.Count);
            Assert.AreEqual(0, window.List.SelectedIndex);
            Assert.AreEqual("gs", window.NameField.Text);
            Assert.AreEqual("gs.exe", window.PathField.Text);
            Assert.AreEqual("150", window.DpiField.Text);

            window.List.SelectedIndex = 1;
            StaTestRunner.DoEvents();
            Assert.AreEqual("gpcl", window.NameField.Text);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void AddEditMoveRemove_ChangeTheResult()
    {
        StaTestRunner.Run(() =>
        {
            var window = new ConverterToolsWindow(new ConverterToolsEditor([Tool("gs")]));
            StaTestRunner.DoEvents();

            window.AddTool();
            window.NameField.Text = "gpcl";
            window.PathField.Text = "gpcl6.exe";
            window.DpiField.Text = "300";
            StaTestRunner.DoEvents();
            Assert.AreEqual("gpcl", (string)window.List.Items[1]!);

            window.MoveUp();
            StaTestRunner.DoEvents();
            CollectionAssert.AreEqual(new[] { "gpcl", "gs" }, window.Result.Select(t => t.Name).ToArray());
            Assert.AreEqual(300, window.Result[0].Dpi);

            window.RemoveSelected();
            StaTestRunner.DoEvents();
            CollectionAssert.AreEqual(new[] { "gs" }, window.Result.Select(t => t.Name).ToArray());
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Accept_WithAnInvalidList_ShowsTheProblemInsteadOfClosing()
    {
        StaTestRunner.Run(() =>
        {
            var window = new ConverterToolsWindow(new ConverterToolsEditor([Tool("gs")]));
            StaTestRunner.DoEvents();
            window.PathField.Text = string.Empty;
            StaTestRunner.DoEvents();

            window.TryAccept();

            StringAssert.Contains(window.Error.Text, "path");
            return Task.CompletedTask;
        });
    }
}
