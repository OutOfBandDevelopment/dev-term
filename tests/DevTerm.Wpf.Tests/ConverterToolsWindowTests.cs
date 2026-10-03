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
    public void DeviceMenu_ConverterTools_SavesTheAppWideList_AndCancelLeavesItAlone()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-converter-tools-wpf", Path.GetRandomFileName(), "converter-tools.json");
        try
        {
            StaTestRunner.Run(() =>
            {
                var ascii = new DevTerm.Presenters.Text.AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new DevTerm.Presenters.Text.AsciiPresenterOptions()));
                var window = new MainWindow(new DevTerm.Core.Sessions.Session(new FakeTransport(), new DevTerm.Core.Presenters.Pipeline([ascii])), new DevTerm.Core.Presenters.PresenterCatalog([ascii]), new CliOptions { Transport = "loopback", Parser = "ascii" }, IsolatedProfiles.Empty())
                {
                    ShowInTaskbar = false,
                    ConverterToolsStore = new ConverterToolsStore(path),
                };
                StaTestRunner.DoEvents();

                window.ShowConverterToolsDialog = _ => null;
                window.EditConverterTools();
                Assert.IsFalse(File.Exists(path));

                IReadOnlyList<StreamConvertToolOptions>? offered = null;
                window.ShowConverterToolsDialog = editor =>
                {
                    offered = editor.ToList();
                    editor.Add();
                    editor.Tools[0].Name = "gs";
                    editor.Tools[0].Path = "gs.exe";
                    return editor.ToList();
                };
                window.EditConverterTools();

                Assert.AreEqual(0, offered!.Count);
                Assert.AreEqual("gs", new ConverterToolsStore(path).Load().Single().Name);
                return Task.CompletedTask;
            });
        }
        finally
        {
            if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void DeviceMenu_ConverterTools_ASaveFailure_IsReportedNotThrown()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "devterm-converter-tools-blocker-" + Path.GetRandomFileName());
        File.WriteAllText(blocker, "a file where the settings folder would have to be");
        try
        {
            StaTestRunner.Run(() =>
            {
                var ascii = new DevTerm.Presenters.Text.AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new DevTerm.Presenters.Text.AsciiPresenterOptions()));
                string? reported = null;
                var window = new MainWindow(new DevTerm.Core.Sessions.Session(new FakeTransport(), new DevTerm.Core.Presenters.Pipeline([ascii])), new DevTerm.Core.Presenters.PresenterCatalog([ascii]), new CliOptions { Transport = "loopback", Parser = "ascii" }, IsolatedProfiles.Empty())
                {
                    ShowInTaskbar = false,
                    ConverterToolsStore = new ConverterToolsStore(Path.Combine(blocker, "converter-tools.json")),
                    ReportConverterToolsError = message => reported = message,
                };
                StaTestRunner.DoEvents();
                window.ShowConverterToolsDialog = editor =>
                {
                    editor.Add();
                    editor.Tools[0].Name = "gs";
                    editor.Tools[0].Path = "gs.exe";
                    return editor.ToList();
                };

                window.EditConverterTools();

                StringAssert.StartsWith(reported, "Could not save the converter tools:");
                return Task.CompletedTask;
            });
        }
        finally
        {
            File.Delete(blocker);
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
