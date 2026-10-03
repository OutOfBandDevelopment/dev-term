using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class DevTermHomeTests
{
    private string? _saved;

    [TestInitialize]
    public void Save() => _saved = Environment.GetEnvironmentVariable(DevTermHome.EnvironmentVariable);

    [TestCleanup]
    public void Restore() => Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, _saved);

    [TestMethod]
    public void Root_WithTheVariableSet_IsThatFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "devterm-home-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, folder);

        Assert.AreEqual(folder, DevTermHome.Root);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Root_WithNoUsableVariable_IsTheDotDevTermFolderInTheUserProfile(string? value)
    {
        Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, value);

        Assert.AreEqual(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dev-term"),
            DevTermHome.Root);
    }
}
