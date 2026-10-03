using DevTerm.DeviceManifests;

namespace DevTerm.Configuration;

/// <summary>
/// The "offer, don't force" hint for a connection profile whose <see cref="CliOptions.ManifestName"/>
/// names a manifest that declares a control panel: a status line telling the user where to open it. It
/// never opens the panel itself. Shared so both front ends say the same thing.
/// </summary>
public static class ManifestPanelHint
{
    /// <summary>
    /// <see langword="null"/> if no manifest is named, it can't be found or loaded (that case is
    /// <see cref="ManifestNameWarning"/>'s), or it declares no panel.
    /// </summary>
    public static string? For(CliOptions cliOptions)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        if (cliOptions.ManifestName is not { } name || DevTermUserDataPaths.ResolveManifestDirectory(name) is not { } directory)
        {
            return null;
        }

        try
        {
            var manifest = DeviceManifestLoader.Load(directory);
            return manifest.Ui is null && manifest.UiFile is null
                ? null
                : $"Device manifest '{name}' has a control panel - open it from Device > Device Manifest...";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
