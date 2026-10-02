using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests.Editing;

/// <summary>What one entry in the manifest editor's outline is.</summary>
public enum ManifestNodeKind
{
    Identity,
    Commands,
    Command,
    Parameter,
    Patterns,
    Pattern,
    Frame,
    FrameField,
    Panel,
    Section,
    Control,
}

/// <summary>
/// One entry in the manifest editor's outline (Identity; Commands › a command › its parameters;
/// Response patterns › a pattern; Panel › a section › its controls), with the form model that edits
/// it — null for a group heading, which only adds children.
/// </summary>
public sealed class ManifestEditorNode : INotifyPropertyChanged
{
    private readonly Func<string> _label;

    internal ManifestEditorNode(ManifestNodeKind kind, int depth, Func<string> label, EditorForm? form, object? item = null, IList? list = null, string? hint = null)
    {
        Kind = kind;
        Depth = depth;
        _label = label;
        Form = form;
        Item = item;
        List = list;
        Hint = hint;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ManifestNodeKind Kind { get; }

    public int Depth { get; }

    /// <summary>The outline text, indented two spaces per level.</summary>
    public string Display => new string(' ', Depth * 2) + _label();

    /// <summary>The form that edits this entry; null for a heading.</summary>
    public EditorForm? Form { get; }

    /// <summary>What a heading's (form-less) pane says instead.</summary>
    public string? Hint { get; }

    /// <summary>The manifest object this entry is (a command, a section, ...) and the list it sits in — for remove/move.</summary>
    internal object? Item { get; }

    internal IList? List { get; }

    internal void RefreshDisplay() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));

    public override string ToString() => Display;
}

/// <summary>
/// The device manifest editor's logic, shared by both front ends (TUI <c>ManifestEditorMode</c>, WPF
/// <c>ManifestEditorWindow</c>): open/new/save a <see cref="DeviceManifest"/>, an outline of its
/// parts, a generated form per part (see <see cref="EditorForm"/>), add/remove/reorder, and a live
/// panel preview built the way a real manifest panel is (<see cref="BuildPreviewDefinition"/>).
/// Saving validates first with <see cref="DeviceManifestValidator"/> — the loader's own checks —
/// and reloads what it wrote to prove it loads. See docs/specs/manifest-editor.md.
/// </summary>
/// <remarks>
/// A new manifest, or one opened from the installed manifests (or a <c>.zip</c>), saves to
/// <c>{userManifestsDirectory}/{folder}/device.json</c> — the user's own copy, which overrides an
/// installed one of the same folder name; one opened from anywhere else saves back where it came from.
/// </remarks>
public sealed class ManifestEditorViewModel
{
    private readonly string _userManifestsDirectory;
    private readonly string? _installedManifestsDirectory;
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string _statusMessage = string.Empty;
    private string? _folderName;

    /// <summary>
    /// The manifest as it stood before whatever's been edited since the last checkpoint was taken —
    /// armed (see <see cref="ArmCheckpoint"/>) as soon as a node is selected, so field edits to that
    /// node (each firing <see cref="MarkEdited"/> through <see cref="EditorForm.Changed"/>, which
    /// mutates before it notifies — there's no earlier hook to snapshot from) all coalesce into the one
    /// undo step captured here, committed by the first of them. Null once committed, or when nothing's
    /// been armed yet.
    /// </summary>
    private string? _pendingCheckpoint;

    public ManifestEditorViewModel(string userManifestsDirectory, string? installedManifestsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userManifestsDirectory);
        _userManifestsDirectory = userManifestsDirectory;
        _installedManifestsDirectory = installedManifestsDirectory;
        Manifest = NewManifest();
        Rebuild(select: 0);
    }

    /// <summary>The manifest being edited.</summary>
    public DeviceManifest Manifest { get; private set; }

    /// <summary>The outline; rebuilt (see <see cref="StructureChanged"/>) when an entry is added, removed or moved.</summary>
    public ObservableCollection<ManifestEditorNode> Nodes { get; } = [];

    public ManifestEditorNode? SelectedNode { get; private set; }

    /// <summary>Where the manifest was opened from (file, folder or zip), or null for a new one.</summary>
    public string? SourcePath { get; private set; }

    /// <summary>Where <see cref="Save"/> writes: in place, or the user's own copy (see the class remarks).</summary>
    public string SaveTarget => SavePath ?? Path.Combine(_userManifestsDirectory, _folderName ?? FolderNameFor(Manifest.Name));

    public bool IsDirty { get; private set; }

    /// <summary>Whether <see cref="Undo"/> has a checkpoint to restore.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>Whether <see cref="Redo"/> has an undone checkpoint to restore.</summary>
    public bool CanRedo => _redoStack.Count > 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The window title: the manifest's name, and a <c>*</c> while there are unsaved edits.</summary>
    public string Title => $"dev-term — Manifest Editor: {(string.IsNullOrWhiteSpace(Manifest.Name) ? "(unnamed)" : Manifest.Name)}{(IsDirty ? " *" : string.Empty)}";

    /// <summary>Asked before <see cref="New"/>/<see cref="Open"/> discards unsaved edits; null proceeds.</summary>
    public Func<bool>? ConfirmDiscardChanges { get; set; }

    /// <summary>Asked before the first save into the user folder overwrites a manifest already there (given its path); null proceeds.</summary>
    public Func<string, bool>? ConfirmOverwrite { get; set; }

    /// <summary>Any edit, open, or new — refresh the preview and title.</summary>
    public event EventHandler? Edited;

    /// <summary>The outline was rebuilt; the selection is <see cref="SelectedNode"/>.</summary>
    public event EventHandler? StructureChanged;

    public event EventHandler? SelectionChanged;

    public event EventHandler? StatusChanged;

    private string? SavePath { get; set; }

    private string? SourceDirectory { get; set; }

    public void Select(ManifestEditorNode? node)
    {
        if (ReferenceEquals(node, SelectedNode))
        {
            return;
        }

        SelectedNode = node;

        // Arms a checkpoint of the manifest as it stands now, before whatever's about to be edited on
        // this node - the first edit's MarkEdited commits it (see ArmCheckpoint's remarks).
        ArmCheckpoint();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Select(int index) => Select(index >= 0 && index < Nodes.Count ? Nodes[index] : null);

    /// <summary>Starts a new, empty manifest (after <see cref="ConfirmDiscardChanges"/> when there are unsaved edits).</summary>
    public bool New()
    {
        if (!ConfirmDiscard())
        {
            return false;
        }

        Manifest = NewManifest();
        SourcePath = null;
        SourceDirectory = null;
        SavePath = null;
        _folderName = null;
        IsDirty = false;
        ClearHistory();
        Rebuild(select: 0);
        StatusMessage = "New manifest.";
        Edited?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Opens the manifest at <paramref name="path"/> (a file, a folder, or a <c>.zip</c>) — unvalidated, so a broken one can be fixed; false (with a status message) when it can't be read.</summary>
    public bool Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!ConfirmDiscard())
        {
            return false;
        }

        DeviceManifest manifest;
        string manifestFile;
        try
        {
            manifest = DeviceManifestLoader.Load(path, validate: false, out manifestFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            StatusMessage = $"Couldn't open '{path}': {ex.Message}";
            return false;
        }

        var full = Path.GetFullPath(path);
        Manifest = manifest;
        SourcePath = full;
        SourceDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestFile));
        var isZip = string.Equals(Path.GetExtension(full), ".zip", StringComparison.OrdinalIgnoreCase);
        var isFolder = Directory.Exists(full);
        _folderName = isZip ? Path.GetFileNameWithoutExtension(full)
            : isFolder ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : string.Equals(Path.GetFileName(full), DeviceManifestLoader.ManifestFileName, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(SourceDirectory)
            : Path.GetFileNameWithoutExtension(full);
        SavePath = isZip || IsUnder(full, _installedManifestsDirectory) ? null : full;
        IsDirty = false;
        ClearHistory();
        Rebuild(select: 0);
        var warnings = DeviceManifestValidator.Validate(manifest);
        StatusMessage = $"Opened '{full}'." + Summary(warnings) + (SavePath is null ? $" Saving writes your own copy to {SaveTarget}." : string.Empty);
        Edited?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Validates (<see cref="DeviceManifestValidator"/>, the loader's own checks), writes to
    /// <see cref="SaveTarget"/>, and reloads the result to prove it loads. False, with the errors in
    /// <see cref="StatusMessage"/>, when it isn't valid (nothing is written) or the write fails.
    /// </summary>
    public bool Save()
    {
        var validation = DeviceManifestValidator.Validate(Manifest);
        if (!validation.IsValid)
        {
            StatusMessage = "Not saved: " + string.Join(" ", validation.Errors);
            return false;
        }

        var target = SaveTarget;
        if (SavePath is null && (File.Exists(Path.Combine(target, DeviceManifestLoader.ManifestFileName)) || File.Exists(target))
            && ConfirmOverwrite?.Invoke(target) == false)
        {
            StatusMessage = $"Not saved: '{target}' already exists.";
            return false;
        }

        try
        {
            var written = DeviceManifestWriter.Save(Manifest, target, SourceDirectory);
            _ = DeviceManifestLoader.Load(target);
            SavePath = target;
            SourcePath = target;
            SourceDirectory = Path.GetDirectoryName(written);
            IsDirty = false;
            StatusMessage = $"Saved to '{written}'." + Summary(validation);
            Edited?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
        {
            StatusMessage = $"Couldn't save to '{target}': {ex.Message}";
            return false;
        }
    }

    /// <summary>Saves to <paramref name="path"/> (a <c>.json</c> file, or a folder for <c>device.json</c>) and keeps saving there.</summary>
    public bool SaveAs(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var previous = SavePath;
        SavePath = Path.GetFullPath(path);
        if (Save())
        {
            return true;
        }

        SavePath = previous;
        return false;
    }

    /// <summary>The loader's checks on the manifest as it stands (shown on request, and before every save).</summary>
    public DeviceManifestValidation Validate()
    {
        var validation = DeviceManifestValidator.Validate(Manifest);
        StatusMessage = validation.IsValid
            ? "Valid." + Summary(validation)
            : "Not valid: " + string.Join(" ", validation.Errors) + Summary(validation);
        return validation;
    }

    /// <summary>What <see cref="AddChild"/> adds under the selected entry ("Add command", "Add parameter", ...), or null when nothing can be added there.</summary>
    public string? AddLabel => SelectedNode?.Kind switch
    {
        ManifestNodeKind.Commands => "Add command",
        ManifestNodeKind.Command => "Add parameter",
        ManifestNodeKind.Parameter => "Add parameter",
        ManifestNodeKind.Patterns or ManifestNodeKind.Pattern => "Add pattern",
        ManifestNodeKind.Frame or ManifestNodeKind.FrameField => "Add field",
        ManifestNodeKind.Panel => "Add section",
        ManifestNodeKind.Section or ManifestNodeKind.Control => "Add control",
        _ => null,
    };

    public bool CanRemove => SelectedNode is { List: not null } || SelectedNode is { Kind: ManifestNodeKind.Panel } && Manifest.Ui is not null;

    public bool CanMove => SelectedNode is { List: not null };

    /// <summary>Adds a new entry of the kind <see cref="AddLabel"/> names, and selects it.</summary>
    public void AddChild()
    {
        ArmCheckpoint();
        var node = SelectedNode;
        object? added = null;
        switch (node?.Kind)
        {
            case ManifestNodeKind.Commands:
                added = AddTo(Manifest.OutboundCommands, new OutboundCommand { Name = Unique("New command", Manifest.OutboundCommands.Select(c => c.Name)), Template = string.Empty });
                break;
            case ManifestNodeKind.Command or ManifestNodeKind.Parameter:
                var command = node.Kind == ManifestNodeKind.Command ? (OutboundCommand)node.Item! : OwnerCommand(node);
                added = AddTo(command.Parameters, new CommandParameter { Name = Unique("param", command.Parameters.Select(p => p.Name), numberFirst: true) });
                break;
            case ManifestNodeKind.Patterns or ManifestNodeKind.Pattern:
                var inbound = Manifest.Inbound ??= new InboundProtocol();
                added = AddTo(inbound.Patterns, new ResponsePattern { Name = Unique("pattern", inbound.Patterns.Select(p => p.Name), numberFirst: true), Match = "^(.*)$" });
                break;
            case ManifestNodeKind.Frame or ManifestNodeKind.FrameField:
                var frame = (Manifest.Inbound ??= new InboundProtocol()).Frame ??= new FrameSchema();
                added = AddTo(frame.Fields, new FrameField { Name = Unique("field", frame.Fields.Select(f => f.Name), numberFirst: true) });
                break;
            case ManifestNodeKind.Panel:
                var ui = Manifest.Ui ??= new UiDefinition { Name = Manifest.Name };
                added = AddTo(ui.Sections, new UiSection { Label = Unique("Section", ui.Sections.Select(s => s.Label ?? string.Empty)) });
                break;
            case ManifestNodeKind.Section or ManifestNodeKind.Control:
                var section = node.Kind == ManifestNodeKind.Section ? (UiSection)node.Item! : OwnerSection(node);
                var ids = Manifest.Ui!.Sections.SelectMany(s => s.Controls).Select(c => c.Id);
                var id = Unique("button", ids, numberFirst: true);
                added = AddTo(section.Controls, new ButtonControl { Id = id, Label = id });
                break;
        }

        if (added is not null)
        {
            MarkEdited();
            Rebuild(Nodes.Count);
            Select(Nodes.FirstOrDefault(n => ReferenceEquals(n.Item, added)));
            StructureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Removes the selected entry (the Panel entry removes the declared panel, so the manifest's generated one is used again).</summary>
    public void Remove()
    {
        if (SelectedNode is not { } node || !CanRemove)
        {
            return;
        }

        ArmCheckpoint();
        var index = Nodes.IndexOf(node);
        if (node.Kind == ManifestNodeKind.Panel)
        {
            Manifest.Ui = null;
        }
        else
        {
            node.List!.Remove(ItemOf(node));
        }

        MarkEdited();
        Rebuild(Math.Max(index - 1, 0));
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveUp() => Move(-1);

    public void MoveDown() => Move(1);

    /// <summary>
    /// Replaces the manifest's binary frame with the one a Kaitai Struct <c>.ksy</c> file describes (see
    /// <see cref="KsyImporter"/>). Returns false, leaving the manifest alone, when the file can't be read or yields no
    /// frame; the status message carries the importer's warnings (what it had to leave out) either way.
    /// </summary>
    public bool ImportKsy(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't read '{path}': {ex.Message}";
            return false;
        }

        var result = KsyImporter.Import(text);
        if (result.Schema is null)
        {
            StatusMessage = "Not imported: " + string.Join(" ", result.Warnings);
            return false;
        }

        ArmCheckpoint();
        (Manifest.Inbound ??= new InboundProtocol()).Frame = result.Schema;
        MarkEdited();
        Rebuild(Nodes.Count);
        Select(Nodes.FirstOrDefault(n => n.Kind == ManifestNodeKind.Frame));
        StructureChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage = $"Imported {result.Schema.Fields.Count} field(s) from {Path.GetFileName(path)}." + (result.Warnings.Count == 0 ? string.Empty : " " + string.Join(" ", result.Warnings));
        return true;
    }

    /// <summary>For a manifest with no declared panel: declares the one it would get from its commands, to edit from there.</summary>
    public void CreatePanelFromCommands()
    {
        if (Manifest.Ui is not null)
        {
            return;
        }

        ArmCheckpoint();
        var generated = ManifestUiBuilder.Build(Clone(Manifest));
        generated.Description = null;
        Manifest.Ui = generated;
        MarkEdited();
        Rebuild(Nodes.Count);
        Select(Nodes.FirstOrDefault(n => n.Kind == ManifestNodeKind.Panel));
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The panel as it would open (see <see cref="ManifestUiBuilder"/>) — built from a copy, since building can fill in the panel's notes.</summary>
    public UiDefinition BuildPreviewDefinition() => ManifestUiBuilder.Build(Clone(Manifest));

    /// <summary>A surface for the preview panel: previews and "sends" exactly as a live panel would, without a connection.</summary>
    public ManifestControlSurface CreatePreviewSurface(UiDefinition definition) => ManifestControlSurface.ForPreview(Clone(Manifest), definition);

    /// <summary>
    /// A reply presenter for the preview panel — never fed a byte, but it makes the renderers treat
    /// the panel as decoding (as a live manifest panel is) rather than print "not decoding". Null
    /// while a response pattern's regex doesn't compile.
    /// </summary>
    public ManifestReplyPresenter? CreatePreviewPresenter()
    {
        try
        {
            return new ManifestReplyPresenter(Clone(Manifest));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Values from a session log that previews and the expression pickers use instead of generated ones; null when none is loaded.</summary>
    public RecordedSamples? Recording { get; private set; }

    /// <summary>
    /// Loads a session log as the sample-data source. Returns false (status message set) when it can't be read or holds nothing
    /// this manifest recognises; the previous recording is kept then.
    /// </summary>
    public bool LoadRecording(string path)
    {
        RecordedSamples samples;
        try
        {
            samples = RecordedSamples.FromSessionLog(Manifest, DevTerm.Logging.SessionLog.Load(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            StatusMessage = $"Couldn't read '{path}': {ex.Message}";
            return false;
        }

        if (samples.IsEmpty)
        {
            StatusMessage = $"{Path.GetFileName(path)} holds no replies this manifest's patterns or frame recognise.";
            return false;
        }

        Recording = samples;
        StatusMessage = $"Sample data now comes from {Path.GetFileName(path)} ({samples.Paths.Count} value(s)).";
        return true;
    }

    /// <summary>Goes back to generated sample data.</summary>
    public void ClearRecording()
    {
        Recording = null;
        StatusMessage = "Sample data is generated again.";
    }

    /// <summary>An expression picker over this manifest's value paths, drawing sample values from the loaded recording if any.</summary>
    public ExpressionPickerViewModel CreatePicker(string? text, PickerMode mode = PickerMode.Expression) =>
        new(ValuePathCatalog.Enumerate(Manifest), text, mode: mode) { Recording = Recording };

    /// <summary>
    /// Fills the preview panel with realistic sample values (see <see cref="SampleDataGenerator"/>) so its indicators, gauges
    /// and expression results show something without a device. Numbers use the invariant culture; a choice shows its label.
    /// </summary>
    public void PublishSampleData(ManifestReplyPresenter? presenter, int seed = 0, int step = 0)
    {
        if (presenter is null)
        {
            return;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in ValuePathCatalog.Enumerate(Manifest))
        {
            if (Recording?.Text(path.Path, step) is { } recorded)
            {
                values[path.Path] = recorded;
            }
            else if (SampleDataGenerator.Text(path, step) is { } label)
            {
                values[path.Path] = label;
            }
            else if (SampleDataGenerator.Value(path, seed, step) is { } number)
            {
                values[path.Path] = number.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        presenter.PublishSampleValues(values);
    }

    /// <summary>A folder name for a manifest's user copy: its name, lower-cased, spaces as dashes, anything unsafe dropped.</summary>
    public static string FolderNameFor(string? name)
    {
        var builder = new StringBuilder();
        foreach (var c in (name ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                builder.Append(c);
            }
            else if (char.IsWhiteSpace(c) && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var folder = builder.ToString().Trim('-', '.');
        return folder.Length > 0 ? folder : "new-device";
    }

    private static DeviceManifest NewManifest() => new() { Name = "New Device" };

    private static DeviceManifest Clone(DeviceManifest manifest) => DeviceManifestSerializer.FromJson(DeviceManifestSerializer.ToJson(manifest));

    private static bool IsUnder(string path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Summary(DeviceManifestValidation validation) =>
        validation.Warnings.Count == 0 ? string.Empty : $" {validation.Warnings.Count} warning(s): " + string.Join(" ", validation.Warnings);

    private static T AddTo<T>(List<T> list, T item)
    {
        list.Add(item);
        return item;
    }

    private static string Unique(string stem, IEnumerable<string> existing, bool numberFirst = false)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!numberFirst && !taken.Contains(stem))
        {
            return stem;
        }

        for (var n = 1; ; n++)
        {
            var candidate = numberFirst ? $"{stem}{n}" : $"{stem} {n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    // A control's entry follows its form: changing a control's kind replaces the control object.
    private static object? ItemOf(ManifestEditorNode node) => node.Form is ControlForm control ? control.Control : node.Item;

    private bool ConfirmDiscard() => !IsDirty || (ConfirmDiscardChanges?.Invoke() ?? true);

    private OutboundCommand OwnerCommand(ManifestEditorNode parameter) =>
        Manifest.OutboundCommands.First(c => ReferenceEquals(c.Parameters, parameter.List));

    private UiSection OwnerSection(ManifestEditorNode control) =>
        Manifest.Ui!.Sections.First(s => ReferenceEquals(s.Controls, control.List));

    private void Move(int delta)
    {
        if (SelectedNode is not { List: { } list } node || ItemOf(node) is not { } item)
        {
            return;
        }

        var index = list.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }

        ArmCheckpoint();
        list.RemoveAt(index);
        list.Insert(target, item);
        MarkEdited();
        Rebuild(0);
        Select(Nodes.FirstOrDefault(n => ReferenceEquals(n.Item, item)));
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Undoes the most recent checkpoint (an add/remove/move, or every edit made to one node since it was selected).</summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _redoStack.Push(DeviceManifestSerializer.ToJson(Manifest));
        Restore(_undoStack.Pop());
        StatusMessage = "Undone.";
    }

    /// <summary>Re-applies the most recently undone checkpoint.</summary>
    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _undoStack.Push(DeviceManifestSerializer.ToJson(Manifest));
        Restore(_redoStack.Pop());
        StatusMessage = "Redone.";
    }

    private void Restore(string json)
    {
        Manifest = DeviceManifestSerializer.FromJson(json);
        _pendingCheckpoint = null;
        IsDirty = true;

        // Rebuild's own trailing Select(...) re-arms a checkpoint against this just-restored state
        // (a new SelectedNode, always - Nodes were just rebuilt from scratch), so an edit right after
        // Undo/Redo checkpoints correctly instead of reusing whatever was pending before the jump.
        Rebuild(0);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Captures the manifest's current state as the "before" snapshot for whatever edit is about to
    /// happen, unless one's already pending (see <see cref="_pendingCheckpoint"/>'s remarks) — a no-op
    /// then, since that earlier snapshot is still the correct "before".
    /// </summary>
    private void ArmCheckpoint() => _pendingCheckpoint ??= DeviceManifestSerializer.ToJson(Manifest);

    /// <summary>Drops all undo/redo history — a different document (<see cref="New"/>/<see cref="Open"/>) makes the old one meaningless.</summary>
    private void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _pendingCheckpoint = null;
    }

    private void CommitCheckpoint()
    {
        if (_pendingCheckpoint is { } json)
        {
            _undoStack.Push(json);
            _redoStack.Clear();
            _pendingCheckpoint = null;
        }
    }

    private void MarkEdited()
    {
        CommitCheckpoint();
        IsDirty = true;
        foreach (var node in Nodes)
        {
            node.RefreshDisplay();
        }

        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild(int select)
    {
        var manifest = Manifest;
        Nodes.Clear();
        Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Identity, 0, () => "Identity", new ManifestIdentityForm(manifest, MarkEdited)));
        Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Commands, 0, () => $"Commands ({manifest.OutboundCommands.Count})", null, hint: "Outbound commands: what the panel's buttons and fields send. Add one, then fill in its template."));
        foreach (var command in manifest.OutboundCommands)
        {
            Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Command, 1, () => string.IsNullOrWhiteSpace(command.Name) ? "(unnamed command)" : command.Name, new CommandForm(manifest, command, MarkEdited), command, manifest.OutboundCommands));
            foreach (var parameter in command.Parameters)
            {
                Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Parameter, 2, () => $"{parameter.Name} ({parameter.Type})", new ParameterForm(parameter, MarkEdited), parameter, command.Parameters));
            }
        }

        var patterns = manifest.Inbound?.Patterns;
        Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Patterns, 0, () => $"Response patterns ({manifest.Inbound?.Patterns.Count ?? 0})", null, hint: "Regexes tested against every reply line; each match publishes values for the panel's indicators and charts."));
        foreach (var pattern in patterns ?? [])
        {
            Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Pattern, 1, () => string.IsNullOrWhiteSpace(pattern.Name) ? "(unnamed pattern)" : pattern.Name, new PatternForm(pattern, MarkEdited), pattern, patterns));
        }

        if (manifest.Inbound?.Frame is { } frame)
        {
            var frameForm = new FrameForm(frame, MarkEdited);
            Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Frame, 0, () => $"Binary frame ({frame.Fields.Count})", frameForm));
            foreach (var field in frame.Fields)
            {
                Nodes.Add(new ManifestEditorNode(ManifestNodeKind.FrameField, 1, () => field.Publishes ? $"{field.Name} ({field.Type})" : $"(skip {field.Size})", new FrameFieldForm(field, MarkEdited, frameForm.Refresh), field, frame.Fields));
            }
        }
        else
        {
            Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Frame, 0, () => "Binary frame (none)", null, hint: "A fixed-layout binary reply, decoded into values. Import a Kaitai Struct .ksy file, or add a field to describe one by hand."));
        }

        if (manifest.Ui is { } ui)
        {
            Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Panel, 0, () => "Panel", new PanelForm(ui, MarkEdited)));
            foreach (var section in ui.Sections)
            {
                Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Section, 1, () => $"[{(string.IsNullOrWhiteSpace(section.Label) ? "no header" : section.Label)}]", new SectionForm(section, MarkEdited), section, ui.Sections));
                foreach (var control in section.Controls.ToList())
                {
                    var form = new ControlForm(section, control, MarkEdited);
                    Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Control, 2, () => $"{ControlForm.KindOf(form.Control)}: {form.Control.Label}", form, control, section.Controls));
                }
            }
        }
        else
        {
            Nodes.Add(new ManifestEditorNode(ManifestNodeKind.Panel, 0, () => "Panel (generated)", null, hint: "No panel declared: the panel is generated from the commands (a button per command, a field per parameter, a reply per query). Add a section, or create the panel from the commands, to design it yourself."));
        }

        Select(Math.Clamp(select, 0, Nodes.Count - 1));
    }
}
