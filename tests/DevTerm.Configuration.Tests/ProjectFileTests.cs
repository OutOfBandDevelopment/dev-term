using DevTerm.Test.Utilities;
using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ProjectFileTests
{
    private static ProjectFile Sample() => ProjectFile.From("Bench", [
        ("Scope", new CliOptions { Transport = "tcp", Host = "192.168.0.110", Port = "23", Presenter = ["ascii", "hex"], Parser = "ascii" }),
        ("Supply", new CliOptions { Transport = "serial", Port = "COM3", Baud = 2400, Presenter = ["raw"] }),
    ]);

    [TestMethod]
    public void RoundTrip_KeepsEachConnectionsTransportAndPresenterChoices()
    {
        var loaded = ProjectFile.FromJson(Sample().ToJson());

        Assert.AreEqual("Bench", loaded.Name);
        Assert.HasCount(2, loaded.Connections);
        var scope = loaded.Find("scope")!.ToOptions();
        Assert.AreEqual("tcp", scope.Transport);
        Assert.AreEqual("192.168.0.110", scope.Host);
        CollectionAssert.AreEqual(new[] { "ascii", "hex" }, scope.Presenter);
        var supply = loaded.Find("Supply")!.ToOptions();
        Assert.AreEqual(2400, supply.Baud);
    }

    [TestMethod]
    public void Find_WithNoName_ReturnsTheFirstConnection_AndUnknownReturnsNull()
    {
        var project = Sample();

        Assert.AreEqual("Scope", project.Find(null)!.Name);
        Assert.IsNull(project.Find("nope"));
    }

    [TestMethod]
    public void FromJson_RejectsGarbageAndAConnectionWithoutAProfile()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ProjectFile.FromJson("not json"));
        Assert.ThrowsExactly<InvalidDataException>(() => ProjectFile.FromJson("""{"Connections":[{"Name":"x"}]}"""));
    }

    [TestMethod]
    public void SaveThenLoad_UsesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"devterm-project-{Guid.NewGuid():N}.json");
        try
        {
            Sample().Save(path);
            Assert.HasCount(2, ProjectFile.Load(path).Connections);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ProjectFlag_LayersTheChosenConnectionUnderCommandLineFlags()
    {
        var path = Path.Combine(Path.GetTempPath(), $"devterm-project-{Guid.NewGuid():N}.json");
        try
        {
            Sample().Save(path);
            var builder = new ConfigurationBuilder();
            DevTermConfiguration.Configure(builder, ["--project", path, "--projectconnection", "Supply", "--baud", "9600"], "Production");
            var options = new CliOptions();
            DevTermConfiguration.Bind(builder.Build(), options);

            Assert.AreEqual("serial", options.Transport);
            Assert.AreEqual("COM3", options.Port);
            Assert.AreEqual(9600, options.Baud, "an explicit flag outranks the project.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ProjectFlag_WithAMissingFile_DoesNotThrow()
    {
        var builder = new ConfigurationBuilder();
        DevTermConfiguration.Configure(builder, ["--project", Path.Combine(Path.GetTempPath(), "does-not-exist.json")], "Production");

        Assert.IsNotNull(builder.Build());
    }
}
