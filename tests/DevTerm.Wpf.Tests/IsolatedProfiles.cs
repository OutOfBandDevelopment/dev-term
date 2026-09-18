using System.IO;
using DevTerm.Configuration;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// A <see cref="ConnectionProfileStore"/> over a directory that doesn't exist, so a test constructing a
/// <c>MainWindow</c> never reads the developer's real <c>~/.dev-term/profiles</c> — a title asserts
/// "not a saved profile" (see <c>ConnectionProfileStore.FindName</c>), which a real profile that
/// happened to match would silently break.
/// </summary>
internal static class IsolatedProfiles
{
    public static ConnectionProfileStore Empty() =>
        new(Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}"));
}
