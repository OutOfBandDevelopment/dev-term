namespace DevTerm.Configuration.Tests;

/// <summary>
/// The "a missing manifest is a warning, not a hard failure" policy from
/// docs/design/connection-profiles.md — every front end calls <see cref="ManifestNameWarning.For"/>
/// instead of resolving/formatting this itself.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class ManifestNameWarningTests
{
    [TestMethod]
    public void For_NoManifestName_ReturnsNull()
    {
        Assert.IsNull(ManifestNameWarning.For(new CliOptions()));
    }

    [TestMethod]
    public void For_ManifestNameThatDoesNotResolve_ReturnsAWarning()
    {
        // A random GUID-suffixed name, not a fixed literal, so this can't collide with a manifest
        // a developer running this test actually has under their real ~/.dev-term/manifests —
        // ManifestNameWarning/DevTermUserDataPaths has no injectable directory to isolate against,
        // unlike ConnectionProfileStore, so this test only exercises the "definitely doesn't
        // resolve" side rather than creating anything under the real user profile.
        var name = "manifest-name-warning-test-" + Guid.NewGuid().ToString("N");

        var warning = ManifestNameWarning.For(new CliOptions { ManifestName = name });

        Assert.IsNotNull(warning);
        Assert.Contains(name, warning);
        Assert.Contains("Warning:", warning);
    }
}
