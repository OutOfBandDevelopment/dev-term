using DevTerm.DeviceManifests;

namespace DevTerm.Configuration;

/// <summary>
/// The manifests a "Device > Device Manifest..." picker lists: the user's own
/// (<see cref="DevTermUserDataPaths.UserManifestsDirectory"/>) first, then the ones installed with
/// dev-term (<see cref="DevTermUserDataPaths.AppManifestsDirectory"/>, e.g. the bundled
/// "Loopback Sensor Demo") — the same two locations, in the same order, a connection profile's
/// <c>ManifestName</c> resolves from. Shared by both front ends.
/// </summary>
public static class InstalledManifests
{
    public const string UserSource = "user";
    public const string InstalledSource = "installed";

    public static IReadOnlyList<ManifestEntry> Discover() =>
        ManifestCatalog.Discover(
            (DevTermUserDataPaths.UserManifestsDirectory, UserSource),
            (DevTermUserDataPaths.AppManifestsDirectory, InstalledSource));
}
