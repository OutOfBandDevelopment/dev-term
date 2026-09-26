using DevTerm.DeviceManifests;
using DevTerm.DeviceManifests.Editing;
using DevTerm.Test.Utilities;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives the real <see cref="ManifestEditorMode"/> window headlessly against the bundled Loopback
/// Sensor Demo manifest: the outline, a part's generated form writing the manifest, the live panel
/// preview (drawn by <see cref="ControlPanelMode"/>, sending nothing), and Save. Also captures the
/// docs/user-guide screenshots. Saves go to a temp folder, never the real <c>~/.dev-term/manifests</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ManifestEditorModeTests
{
    private static readonly string _bundled = Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo");
    private static readonly string _imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");

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

    private static void Run(Action<ManifestEditorParts, string> body)
    {
        var userDirectory = Path.Combine(Path.GetTempPath(), "devterm-manifest-editor-tui", Path.GetRandomFileName());
        Directory.CreateDirectory(userDirectory);
        try
        {
            TuiTestRunner.RunHeadlessApp(app =>
            {
                var editor = new ManifestEditorViewModel(userDirectory, Path.Combine(AppContext.BaseDirectory, "manifests"));
                Assert.IsTrue(editor.Open(_bundled), editor.StatusMessage);
                var parts = ManifestEditorMode.BuildWindow(app, editor);
                var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
                app.LayoutAndDraw(true);
                try
                {
                    body(parts, userDirectory);
                }
                finally
                {
                    app.End(token);
                    parts.Window.Dispose();
                }
            });
        }
        finally
        {
            Directory.Delete(userDirectory, recursive: true);
        }
    }

    private static void Select(ManifestEditorParts parts, string display)
    {
        parts.Outline.SelectedItem = parts.ViewModel.Nodes.ToList().FindIndex(n => n.Display.Trim() == display);
        TuiTestRunner.CurrentApp.LayoutAndDraw(true);
    }

    [TestMethod]
    public void SelectingAPart_ShowsItsGeneratedForm_WhichEditsTheManifest()
    {
        Run((parts, _) =>
        {
            var dump = TuiTestRunner.DumpBuffer();
            Assert.Contains("Identity", dump);
            Assert.Contains("Commands (3)", dump);
            var name = (TextField)parts.Form!.ControlViews[nameof(ManifestIdentityForm.Name)];
            Assert.AreEqual("Loopback Sensor Demo", name.Text);

            Select(parts, "Stream Samples");
            var template = (TextField)parts.Form!.ControlViews[nameof(CommandForm.Template)];
            Assert.AreEqual("Samples: {count}", template.Text);
            template.Text = "Samples: {count}\\r";
            TuiTestRunner.CurrentApp.LayoutAndDraw(true);

            Assert.AreEqual("Samples: {count}\r", parts.ViewModel.Manifest.OutboundCommands[1].Template);
            Assert.EndsWith(" *", parts.Window.Title.ToString());
            Assert.Contains("Samples: 40\\r\\n", TuiTestRunner.DumpBuffer(), "The form's Sends line shows the new wire text.");
        });
    }

    [TestMethod]
    public void AddButton_AddsUnderTheSelection_AndTheOutlineFollows()
    {
        Run((parts, _) =>
        {
            Select(parts, "Stream Samples");
            Assert.AreEqual("Add parameter", parts.AddButton.Text);

            parts.AddButton.InvokeCommand(Command.Accept);
            TuiTestRunner.CurrentApp.LayoutAndDraw(true);

            Assert.AreEqual("param1", parts.ViewModel.Manifest.OutboundCommands[1].Parameters[1].Name);
            Assert.Contains("    param1 (string)", TuiTestRunner.DumpBuffer());
            Assert.AreEqual(ManifestNodeKind.Parameter, parts.ViewModel.SelectedNode!.Kind);
        });
    }

    [TestMethod]
    public void Preview_DrawsThePanelLive_AndItsButtonsSendNothing()
    {
        Run((parts, _) =>
        {
            parts.ShowPreview();
            TuiTestRunner.CurrentApp.LayoutAndDraw(true);

            var dump = TuiTestRunner.DumpBuffer();
            Assert.Contains("[-] Acquire", dump);
            Assert.Contains("Stream Samples", dump);

            ((Button)parts.Preview!.ControlViews["measure"]).InvokeCommand(Command.Accept);
            Assert.AreEqual("Preview: would send MEAS?\\n", parts.StatusLabel.Text);

            parts.PreviewButton.InvokeCommand(Command.Accept);
            Assert.IsFalse(parts.ShowingPreview, "Preview toggles back to the form.");
            Assert.IsNotNull(parts.Form);
        });
    }

    [TestMethod]
    public void SaveButton_WritesTheUsersOwnCopy_WhichLoadsBack()
    {
        Run((parts, userDirectory) =>
        {
            parts.SaveButton.InvokeCommand(Command.Accept);

            var saved = Path.Combine(userDirectory, "loopback-sensor-demo");
            Assert.StartsWith("Saved to", parts.StatusLabel.Text);
            Assert.AreEqual("Loopback Sensor Demo", DeviceManifestLoader.Load(saved).Name);
            Assert.DoesNotContain("*", parts.Window.Title.ToString());
        });
    }

    [TestMethod]
    public void Screens_AreCaptured()
    {
        Run((parts, _) =>
        {
            Select(parts, "Stream Samples");
            Directory.CreateDirectory(_imagesDirectory);
            File.WriteAllText(Path.Combine(_imagesDirectory, "tui-manifest-editor.txt"), TuiTestRunner.DumpBuffer());
            TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-manifest-editor.png"));

            Select(parts, "barGraph: Channels");
            File.WriteAllText(Path.Combine(_imagesDirectory, "tui-manifest-editor-control.txt"), TuiTestRunner.DumpBuffer());
            TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-manifest-editor-control.png"));

            parts.ShowPreview();
            TuiTestRunner.CurrentApp.LayoutAndDraw(true);
            var preview = TuiTestRunner.DumpBuffer();
            File.WriteAllText(Path.Combine(_imagesDirectory, "tui-manifest-editor-preview.txt"), preview);
            TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-manifest-editor-preview.png"));

            Assert.Contains("[-] Acquire", preview);
        });
    }
}
