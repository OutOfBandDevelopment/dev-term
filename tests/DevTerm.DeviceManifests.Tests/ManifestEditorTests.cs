using DevTerm.DeviceManifests.Editing;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using DevTerm.UiDefinitions.Forms;

namespace DevTerm.DeviceManifests.Tests;

/// <summary>
/// The device manifest editor's front-end-agnostic half: <see cref="ManifestEditorViewModel"/>, its
/// generated forms (<see cref="EditorForm"/> subclasses through <see cref="FormBinding"/>),
/// <see cref="DeviceManifestValidator"/> (shared with the loader) and <see cref="DeviceManifestWriter"/>.
/// Every save goes to a temp "user manifests" folder, never the real <c>~/.dev-term/manifests</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ManifestEditorTests
{
    private static readonly string _installed = Path.Combine(AppContext.BaseDirectory, "manifests");
    private static readonly string _bundled = Path.Combine(_installed, "loopback-sensor-demo");

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-manifest-editor-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void InTemp(Action<string> body)
    {
        var directory = CreateTempDirectory();
        try
        {
            body(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Reads every field of every entry's generated form back into itself, as rendering and re-committing each field would.</summary>
    private static void TouchEveryField(ManifestEditorViewModel editor)
    {
        foreach (var node in editor.Nodes.ToList())
        {
            if (node.Form is not { } form)
            {
                continue;
            }

            var definition = FormDefinitionGenerator.Generate(form.GetType(), form);
            using var binding = new FormBinding(form);
            foreach (var control in definition.Sections.SelectMany(s => s.Controls).Where(c => !binding.IsReadOnly(c.Id)))
            {
                switch (control)
                {
                    case ToggleControl:
                        binding.SetBool(control.Id, binding.GetBool(control.Id));
                        break;
                    default:
                        binding.SetText(control, binding.GetText(control.Id));
                        break;
                }
            }
        }
    }

    [TestMethod]
    public void BundledDemo_LoadsEditsSavesAndReloadsUnchanged()
    {
        InTemp(userDirectory =>
        {
            var original = DeviceManifestSerializer.ToJson(DeviceManifestLoader.Load(_bundled));
            var editor = new ManifestEditorViewModel(userDirectory, _installed);

            Assert.IsTrue(editor.Open(_bundled), editor.StatusMessage);
            Assert.AreEqual(Path.Combine(userDirectory, "loopback-sensor-demo"), editor.SaveTarget, "An installed manifest saves as the user's own copy, same folder name.");

            TouchEveryField(editor);
            var identity = (ManifestIdentityForm)editor.Nodes[0].Form!;
            identity.Name = "Renamed";
            Assert.IsTrue(editor.IsDirty);
            Assert.EndsWith(" *", editor.Title);
            identity.Name = "Loopback Sensor Demo";

            Assert.IsTrue(editor.Save(), editor.StatusMessage);
            Assert.IsFalse(editor.IsDirty);

            var saved = Path.Combine(userDirectory, "loopback-sensor-demo", DeviceManifestLoader.ManifestFileName);
            Assert.IsTrue(File.Exists(saved));
            Assert.AreEqual(original, DeviceManifestSerializer.ToJson(DeviceManifestLoader.Load(saved)), "Saved and reloaded, the manifest is exactly what was opened.");
            Assert.Contains("\"name\": \"Loopback Sensor Demo\"", File.ReadAllText(saved), "Written camelCase like the hand-written original.");
            Assert.DoesNotContain("\"uiFile\"", File.ReadAllText(saved), "Unset values are left out.");
        });
    }

    [TestMethod]
    public void Outline_ListsEveryPartOfTheManifest()
    {
        var editor = new ManifestEditorViewModel(Path.GetTempPath());
        Assert.IsTrue(editor.Open(_bundled), editor.StatusMessage);

        var outline = editor.Nodes.Select(n => n.Display).ToList();

        Assert.AreEqual("Identity", outline[0]);
        Assert.Contains("Commands (3)", outline);
        Assert.Contains("  Stream Samples", outline);
        Assert.Contains("    count (integer)", outline);
        Assert.Contains("Response patterns (1)", outline);
        Assert.Contains("  sample", outline);
        Assert.Contains("  [Acquire]", outline);
        Assert.Contains("    barGraph: Channels", outline);
        Assert.IsNull(editor.Nodes.First(n => n.Kind == ManifestNodeKind.Commands).Form, "A heading has no form, only a hint.");
    }

    [TestMethod]
    public void AddRemoveAndMove_EditTheManifestAndKeepTheSelection()
    {
        var editor = new ManifestEditorViewModel(Path.GetTempPath());
        var commands = editor.Nodes.First(n => n.Kind == ManifestNodeKind.Commands);
        editor.Select(commands);
        Assert.AreEqual("Add command", editor.AddLabel);

        editor.AddChild();
        editor.Select(commands.Kind == ManifestNodeKind.Commands ? editor.Nodes.First(n => n.Kind == ManifestNodeKind.Commands) : null);
        editor.AddChild();

        Assert.AreSequenceEqual(["New command", "New command 1"], [.. editor.Manifest.OutboundCommands.Select(c => c.Name)]);
        Assert.AreEqual(ManifestNodeKind.Command, editor.SelectedNode!.Kind, "The new entry is selected.");

        editor.AddChild();
        Assert.AreEqual("param1", editor.Manifest.OutboundCommands[1].Parameters.Single().Name);
        Assert.AreEqual(ManifestNodeKind.Parameter, editor.SelectedNode!.Kind);

        editor.Select(editor.Nodes.First(n => n.Display.Trim() == "New command 1"));
        editor.MoveUp();
        Assert.AreSequenceEqual(["New command 1", "New command"], [.. editor.Manifest.OutboundCommands.Select(c => c.Name)]);
        Assert.AreEqual("  New command 1", editor.SelectedNode!.Display);

        editor.Remove();
        Assert.AreSequenceEqual(["New command"], [.. editor.Manifest.OutboundCommands.Select(c => c.Name)]);
        Assert.IsTrue(editor.IsDirty);

        editor.Select(editor.Nodes.First(n => n.Kind == ManifestNodeKind.Panel));
        Assert.IsFalse(editor.CanRemove, "No declared panel yet.");
        editor.AddChild();
        Assert.IsNotNull(editor.Manifest.Ui);
        Assert.AreEqual(ManifestNodeKind.Section, editor.SelectedNode!.Kind);
        editor.AddChild();
        Assert.IsInstanceOfType<ButtonControl>(editor.Manifest.Ui.Sections[0].Controls.Single());
    }

    [TestMethod]
    public void ChangingAControlsKind_ReplacesItWithTheNewKind_KeepingIdLabelAndHelp()
    {
        var editor = new ManifestEditorViewModel(Path.GetTempPath());
        editor.Select(editor.Nodes.First(n => n.Kind == ManifestNodeKind.Panel));
        editor.AddChild();
        editor.AddChild();
        var form = (ControlForm)editor.SelectedNode!.Form!;
        form.Label = "Level";
        form.Help = "How loud";

        var definition = FormDefinitionGenerator.Generate(form.GetType(), form);
        using var binding = new FormBinding(form);
        var channels = definition.Sections.SelectMany(s => s.Controls).Single(c => c.Id == nameof(ControlForm.Channels));
        Assert.IsFalse(binding.IsVisible(channels.VisibleWhen), "A button has no chart channels.");

        form.Kind = "barGraph";
        form.Channels = "chA:A, chB:B:#112233";

        var control = (BarGraphControl)editor.Manifest.Ui!.Sections[0].Controls.Single();
        Assert.AreEqual("button1", control.Id);
        Assert.AreEqual("Level", control.Label);
        Assert.AreEqual("How loud", control.Description);
        Assert.AreSequenceEqual(["chA", "chB"], [.. control.Channels.Select(c => c.Id)]);
        Assert.AreEqual("#112233", control.Channels[1].Color);
        Assert.IsTrue(binding.IsVisible(channels.VisibleWhen));

        editor.Remove();
        Assert.IsEmpty(editor.Manifest.Ui.Sections[0].Controls, "Remove follows the replaced control, not the original button.");
    }

    [TestMethod]
    public void Save_RefusesAnInvalidManifest_AndWritesNothing()
    {
        InTemp(userDirectory =>
        {
            var editor = new ManifestEditorViewModel(userDirectory);
            editor.Select(editor.Nodes.First(n => n.Kind == ManifestNodeKind.Patterns));
            editor.AddChild();
            ((PatternForm)editor.SelectedNode!.Form!).Match = "([unclosed";

            Assert.IsFalse(editor.Save());
            Assert.StartsWith("Not saved:", editor.StatusMessage);
            Assert.Contains("doesn't compile", editor.StatusMessage);
            Assert.IsEmpty(Directory.GetFileSystemEntries(userDirectory));

            ((PatternForm)editor.SelectedNode!.Form!).Match = "^(?<v>\\d+)$";
            Assert.IsTrue(editor.Save(), editor.StatusMessage);
            Assert.IsTrue(File.Exists(Path.Combine(userDirectory, "new-device", DeviceManifestLoader.ManifestFileName)));
        });
    }

    [TestMethod]
    public void Loader_RejectsWhatTheValidatorCallsAnError_ButTheEditorStillOpensIt()
    {
        InTemp(directory =>
        {
            var path = Path.Combine(directory, "broken.json");
            File.WriteAllText(path, """{ "name": "Broken", "outboundCommands": [ { "name": "A", "template": "A" }, { "name": "A", "template": "B" } ] }""");

            var ex = Assert.ThrowsExactly<DeviceManifestValidationException>(() => DeviceManifestLoader.Load(path));
            Assert.Contains("already uses the id 'A'", ex.Errors.Single());

            var editor = new ManifestEditorViewModel(directory);
            Assert.IsTrue(editor.Open(path), "Opened unvalidated, to be fixed.");
            Assert.AreEqual(path, editor.SaveTarget, "Not an installed manifest: saved back in place.");
            ((CommandForm)editor.Nodes.First(n => n.Kind == ManifestNodeKind.Command && n.Display.Trim() == "A").Form!).Id = "second";
            Assert.IsTrue(editor.Save(), editor.StatusMessage);
            Assert.AreEqual(2, DeviceManifestLoader.Load(path).OutboundCommands.Count);
        });
    }

    [TestMethod]
    public void Validator_WarnsAboutAPanelControlNoCommandServes()
    {
        var manifest = new DeviceManifest
        {
            Name = "Warn",
            OutboundCommands = [new OutboundCommand { Name = "Go", Template = "GO {speed}" }],
            Ui = new UiDefinition
            {
                Name = "Warn",
                Sections = [new UiSection { Controls = [new ButtonControl { Id = "stop", Label = "Stop" }, new TextFieldControl { Id = "free", Label = "Free" }] }],
            },
        };

        var validation = DeviceManifestValidator.Validate(manifest);

        Assert.IsTrue(validation.IsValid, "Warnings don't stop a load.");
        Assert.HasCount(3, validation.Warnings);
        Assert.Contains("'{speed}' isn't one of its parameters", validation.Warnings[0]);
        Assert.Contains("invokes 'stop'", validation.Warnings[1]);
        Assert.Contains("'free'", validation.Warnings[2]);
    }

    [TestMethod]
    public void Writer_KeepsAPackageManifestsPanelInItsOwnFile_AndCopiesItsKaitaiFile()
    {
        InTemp(directory =>
        {
            var source = Path.Combine(directory, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "layout.ksy"), "meta: {}");
            var manifest = new DeviceManifest
            {
                Name = "Package",
                UiFile = "ui.json",
                Inbound = new InboundProtocol { KaitaiFile = "layout.ksy" },
                Ui = new UiDefinition { Name = "Package", Sections = [new UiSection { Label = "S", Controls = [new IndicatorControl { Id = "v", Label = "V" }] }] },
            };

            var file = DeviceManifestWriter.Save(manifest, Path.Combine(directory, "out"), source);

            Assert.DoesNotContain("\"ui\":", File.ReadAllText(file), "The panel lives in ui.json, not inline.");
            Assert.IsTrue(File.Exists(Path.Combine(directory, "out", "ui.json")));
            Assert.IsTrue(File.Exists(Path.Combine(directory, "out", "layout.ksy")));
            var reloaded = DeviceManifestLoader.Load(Path.Combine(directory, "out"));
            Assert.AreEqual("v", reloaded.Ui!.Sections[0].Controls[0].Id);
            Assert.IsNotNull(manifest.Ui, "The edited manifest itself keeps its panel.");
        });
    }

    [TestMethod]
    public void Preview_DoesNotChangeTheManifest_AndItsSurfaceSendsNothing()
    {
        var editor = new ManifestEditorViewModel(Path.GetTempPath());
        Assert.IsTrue(editor.Open(_bundled), editor.StatusMessage);
        var before = DeviceManifestSerializer.ToJson(editor.Manifest);

        var definition = editor.BuildPreviewDefinition();
        var surface = editor.CreatePreviewSurface(definition);
        string? sent = null;
        surface.PreviewInvoked += (_, text) => sent = text;
        surface.InvokeAsync("samples", "12").GetAwaiter().GetResult();

        Assert.AreEqual(before, DeviceManifestSerializer.ToJson(editor.Manifest), "Building the preview (which fills in its Notes) works on a copy.");
        Assert.IsFalse(string.IsNullOrEmpty(definition.Description));
        Assert.AreEqual("Samples: 12\\n", sent);
        Assert.AreEqual("MEAS?\\n", surface.PreviewCommand("measure", null));
    }

    [TestMethod]
    public void CommandForm_ShowsWhatItSends_AndEditsTemplatesWithEscapes()
    {
        var editor = new ManifestEditorViewModel(Path.GetTempPath());
        Assert.IsTrue(editor.Open(_bundled), editor.StatusMessage);
        var form = (CommandForm)editor.Nodes.First(n => n.Display.Trim() == "Stream Samples").Form!;

        Assert.AreEqual("Samples: 40\\n", form.Sends.Split(' ', 3)[0] + " " + form.Sends.Split(' ', 3)[1]);
        form.Template = "S {count}\\r";
        Assert.AreEqual("S {count}\r", form.Command.Template);
        Assert.AreEqual("S {count}\\r", form.Template);
    }

    [TestMethod]
    [DataRow("plain")]
    [DataRow("a\\b")]
    [DataRow("line\r\n")]
    [DataRow("\u0003end\t")]
    public void EscapeAndUnescape_RoundTrip(string text) =>
        Assert.AreEqual(text, EditorForm.Unescape(EditorForm.Escape(text)));

    [TestMethod]
    [DataRow("Loopback Sensor Demo", "loopback-sensor-demo")]
    [DataRow("  Korad KA3005P / v2 ", "korad-ka3005p-v2")]
    [DataRow("***", "new-device")]
    public void FolderNameFor_MakesASafeFolderName(string name, string expected) =>
        Assert.AreEqual(expected, ManifestEditorViewModel.FolderNameFor(name));

    [TestMethod]
    public void EveryEditorForm_GeneratesAForm()
    {
        foreach (var type in new[] { typeof(ManifestIdentityForm), typeof(CommandForm), typeof(ParameterForm), typeof(PatternForm), typeof(PanelForm), typeof(SectionForm), typeof(ControlForm) })
        {
            var definition = FormDefinitionGenerator.Generate(type);
            Assert.IsNotEmpty(definition.Sections.SelectMany(s => s.Controls), type.Name);
        }
    }
}
