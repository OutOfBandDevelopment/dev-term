using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class DevTermUserDataPathsTests
{
    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void ResolveManifestDirectory_WithARootedNamePointingAtARealDirectory_ReturnsNullInsteadOfThatDirectory()
    {
        // Regression test for bug 012: ResolveManifestDirectory passed an imported profile's
        // ManifestName straight to Path.Combine. Path.Combine ignores its first argument entirely when
        // the second is rooted, so a rooted ManifestName pointing at any existing directory used to
        // resolve to that directory directly - escaping both the user's and the app's manifest
        // folders. Pointing at Path.GetTempPath() (guaranteed to exist) proves the escape actually
        // worked, rather than merely asserting on a path that happens not to exist either way. See
        // docs/bugs/012-profile-names-not-validated.md.
        var outsideDirectory = CreateTempDirectory();
        try
        {
            Assert.IsNull(DevTermUserDataPaths.ResolveManifestDirectory(outsideDirectory));
        }
        finally
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-userdatapaths-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
