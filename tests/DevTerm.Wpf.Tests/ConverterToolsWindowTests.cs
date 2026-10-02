using System.IO;
using System.Windows;
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
    public void MoveDown_ReordersAndKeepsTheMovedToolSelected()
    {
        StaTestRunner.Run(() =>
        {
            var window = new ConverterToolsWindow(new ConverterToolsEditor([Tool("a"), Tool("b")]));
            StaTestRunner.DoEvents();

            window.MoveDown();
            StaTestRunner.DoEvents();

            CollectionAssert.AreEqual(new[] { "b", "a" }, window.Result.Select(t => t.Name).ToArray());
            Assert.AreEqual(1, window.List.SelectedIndex);
            Assert.AreEqual("a", window.NameField.Text);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Remove_LastTool_DisablesTheDetailFields()
    {
        StaTestRunner.Run(() =>
        {
            var window = new ConverterToolsWindow(new ConverterToolsEditor([Tool("a")]));
            StaTestRunner.DoEvents();

            window.RemoveSelected();
            StaTestRunner.DoEvents();

            Assert.AreEqual(0, window.List.Items.Count);
            Assert.IsFalse(window.NameField.IsEnabled);
            Assert.AreEqual(0, window.Result.Count);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Accept_WithAValidList_ClearsTheErrorAndReturnsTheTools()
    {
        StaTestRunner.Run(() =>
        {
            var window = new ConverterToolsWindow(new ConverterToolsEditor([Tool("gs")]));
            StaTestRunner.DoEvents();
            window.Error.Text = "stale";
            window.NameField.Text = "gs2";
            StaTestRunner.DoEvents();

            window.TryAccept();

            Assert.AreEqual(string.Empty, window.Error.Text);
            Assert.AreEqual("gs2", window.Result.Single().Name);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void EditToolsButton_ReplacesTheProfilesTools_WhenTheDialogIsAccepted_AndLeavesThemWhenCancelled()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-converter-tools-wpf", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            StaTestRunner.Run(() =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions { Transport = "loopback", StreamConvertTools = [Tool("gs")] }) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();
                IReadOnlyList<StreamConvertToolOptions>? seenByDialog = null;

                window.ShowConverterToolsDialog = editor =>
                {
                    seenByDialog = editor.ToList();
                    editor.Add();
                    return null;
                };
                window.EditConverterToolsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.AreEqual("gs", seenByDialog!.Single().Name);
                Assert.AreEqual(1, window.ViewModel.ConverterTools.Count);

                window.ShowConverterToolsDialog = editor =>
                {
                    editor.Add();
                    return editor.ToList();
                };
                window.EditConverterToolsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                StaTestRunner.DoEvents();

                Assert.AreEqual(2, window.ViewModel.ConverterTools.Count);
                Assert.IsTrue(window.ViewModel.IsDirty);
                StringAssert.Contains(window.ViewModel.ConverterToolsSummary, "2 converter tools");
                return Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
