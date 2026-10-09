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

    public WebManifestEditor(string userDirectory, string? installedDirectory)
    {
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
}
