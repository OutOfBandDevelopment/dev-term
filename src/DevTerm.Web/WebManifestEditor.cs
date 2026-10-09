using DevTerm.DeviceManifests;
using DevTerm.DeviceManifests.Editing;

namespace DevTerm.Web;

/// <summary>
/// The host's one Manifest Editor (Device > Edit Device Manifest... in the desktop apps): the shared
/// <see cref="ManifestEditorViewModel"/> over the user's manifests folder. Like the desktop editor it needs no
/// connection. Only manifests found in the user and installed folders can be opened, not arbitrary host paths.
/// </summary>
public sealed class WebManifestEditor
{
    private readonly string _userDirectory;
    private readonly string? _installedDirectory;
    private readonly PlaybackLibrary? _recordings;

    public WebManifestEditor(string userDirectory, string? installedDirectory, PlaybackLibrary? recordings = null)
    {
        _recordings = recordings;
        _userDirectory = userDirectory;
        _installedDirectory = installedDirectory;
        Editor = new ManifestEditorViewModel(userDirectory, installedDirectory)
        {
            // A web page has no modal prompts; the page asks before it calls New/Open, so these just allow it.
            ConfirmDiscardChanges = () => true,
            ConfirmOverwrite = _ => true,
        };
    }

    public ManifestEditorViewModel Editor { get; }

    /// <summary>The manifests a viewer can open: the user's own, then the installed ones.</summary>
    public IReadOnlyList<ManifestEntry> Available() =>
        _installedDirectory is null
            ? ManifestCatalog.Discover((_userDirectory, "user"))
            : ManifestCatalog.Discover((_userDirectory, "user"), (_installedDirectory, "installed"));

    /// <summary>Opens one of <see cref="Available"/> by its path; false when it isn't one of them.</summary>
    public bool OpenAvailable(string path) =>
        Available().Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)) && Editor.Open(path);

    /// <summary>Saves under the user's manifests folder as a folder named from <paramref name="name"/> (anything unsafe dropped); false (status set) when the name leaves nothing.</summary>
    public bool SaveAs(string name)
    {
        var folder = ManifestEditorViewModel.FolderNameFor(name);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(folder))
        {
            Editor.StatusMessage = "Not saved: give the copy a name.";
            return false;
        }

        return Editor.SaveAs(Path.Combine(_userDirectory, folder));
    }

    /// <summary>The session logs a viewer can use as sample data (the Playback folder).</summary>
    public IReadOnlyList<string> Recordings() => _recordings?.List() ?? [];

    /// <summary>Uses a log from <see cref="Recordings"/> as the sample-data source; false when it isn't one or holds nothing the manifest recognises.</summary>
    public bool UseRecording(string name) => _recordings?.Resolve(name) is { } path && Editor.LoadRecording(path);

    /// <summary>Imports an uploaded Kaitai <c>.ksy</c> as the manifest's binary frame (the editor reads a file, so the text goes through a temporary one).</summary>
    public bool ImportKsy(string fileName, string text)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"devterm-ksy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var safeName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "upload.ksy" : fileName);
            var path = Path.Combine(directory, safeName);
            File.WriteAllText(path, text);
            return Editor.ImportKsy(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
