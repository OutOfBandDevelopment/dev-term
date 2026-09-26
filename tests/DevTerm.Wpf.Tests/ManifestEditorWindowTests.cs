using System.IO;
using System.Windows;
using System.Windows.Controls;
using DevTerm.DeviceManifests;
using DevTerm.DeviceManifests.Editing;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives the real <see cref="ManifestEditorWindow"/> against the bundled Loopback Sensor Demo: a
/// part's generated form edits the manifest, the live preview (the real <see cref="ControlPanelWindow"/>
/// rendering, moved into the editor) follows the edit and sends nothing, and Save writes a copy that
/// loads back. Also captures the docs/user-guide screenshot. Saves go to a temp folder.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ManifestEditorWindowTests
{
    private static readonly string _installed = Path.Combine(AppContext.BaseDirectory, "manifests");
    private static readonly string _bundled = Path.Combine(_installed, "loopback-sensor-demo");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not find the repo root (DevTerm.slnx) above '{AppContext.BaseDirectory}'.");
    }

    private static void Run(Func<ManifestEditorWindow, string, Task> body)
    {
        var userDirectory = Path.Combine(Path.GetTempPath(), "devterm-manifest-editor-wpf", Path.GetRandomFileName());
        Directory.CreateDirectory(userDirectory);
        try
        {
            StaTestRunner.Run(async () =>
            {
                var editor = new ManifestEditorViewModel(userDirectory, _installed);
                Assert.IsTrue(editor.Open(_bundled), editor.StatusMessage);
                var window = new ManifestEditorWindow(editor) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();
                await body(window, userDirectory);
                window.Editor.ConfirmDiscardChanges = () => true;
            });
        }
        finally
        {
            Directory.Delete(userDirectory, recursive: true);
        }
    }

    private static void Select(ManifestEditorWindow window, string display)
    {
        window.OutlineList.SelectedItem = window.Editor.Nodes.First(n => n.Display.Trim() == display);
        StaTestRunner.DoEvents();
    }

    [TestMethod]
    public void EditingAFormField_ChangesTheManifest_AndThePreviewFollows()
    {
        Run(async (window, _) =>
        {
            Assert.AreEqual("Loopback Sensor Demo", window.Form!.TextBoxes[nameof(ManifestIdentityForm.Name)].Text);
            Assert.IsNotNull(window.Preview);
            Assert.IsTrue(window.Preview.ControlViews.ContainsKey("measure"));

            Select(window, "[Acquire]");
            window.Form!.TextBoxes[nameof(SectionForm.Label)].Text = "Take readings";
            StaTestRunner.DoEvents();

            Assert.AreEqual("Take readings", window.Editor.Manifest.Ui!.Sections[0].Label);
            Assert.IsTrue(window.Preview!.SectionExpanders.ContainsKey("Take readings"), "The preview was rebuilt from the edited manifest.");
            Assert.EndsWith(" *", window.Title);

            ((Button)window.Preview.ControlViews["measure"]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            StaTestRunner.DoEvents();
            Assert.AreEqual("Would send: MEAS?\\n", window.PreviewSentText.Text);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void SelectingAHeading_ShowsItsHint_AndAddAddsUnderIt()
    {
        Run(async (window, _) =>
        {
            Select(window, "Commands (3)");
            Assert.IsNull(window.Form);
            Assert.AreEqual(Visibility.Visible, window.HintText.Visibility);
            Assert.AreEqual("Add command", window.AddButton.Content);

            window.AddButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            StaTestRunner.DoEvents();

            Assert.HasCount(4, window.Editor.Manifest.OutboundCommands);
            Assert.AreEqual(ManifestNodeKind.Command, ((ManifestEditorNode)window.OutlineList.SelectedItem).Kind);
            Assert.IsNotNull(window.Form!.TextBoxes[nameof(CommandForm.Template)]);

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Save_WritesTheUsersOwnCopy_WhichLoadsBack()
    {
        Run(async (window, userDirectory) =>
        {
            Assert.IsTrue(window.Editor.Save(), window.Editor.StatusMessage);
            StaTestRunner.DoEvents();

            Assert.StartsWith("Saved to", window.StatusText.Text);
            Assert.AreEqual("Loopback Sensor Demo", DeviceManifestLoader.Load(Path.Combine(userDirectory, "loopback-sensor-demo")).Name);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ManifestEditor_IsCaptured()
    {
        Run(async (window, _) =>
        {
            Select(window, "Stream Samples");
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            WpfScreenshot.ShowOffScreen(window, 1180, 660);
            StaTestRunner.DoEvents();
            window.UpdateLayout();

            var path = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images", "wpf-manifest-editor.png");
            WpfScreenshot.Save(window, path);
            Assert.IsGreaterThan(1000L, new FileInfo(path).Length);

            Select(window, "barGraph: Channels");
            window.UpdateLayout();
            var controlPath = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images", "wpf-manifest-editor-control.png");
            WpfScreenshot.Save(window, controlPath);
            Assert.IsGreaterThan(1000L, new FileInfo(controlPath).Length);

            window.Editor.ConfirmDiscardChanges = () => true;
            window.Close();
            await Task.CompletedTask;
        });
    }
}
