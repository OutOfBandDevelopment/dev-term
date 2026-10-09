using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Web.Tests;

/// <summary>Without a <c>--project</c> file the manager works on the shared saved-profile store, the same one the TUI and WPF use.</summary>
[TestClass]
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Web)]
public class ConnectionManagerStoreTests
{
    [TestMethod]
    public async Task WithoutAProject_ListsSavesOpensAndDeletesTheSharedProfiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"devterm-web-store-{Guid.NewGuid():N}");
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("Sim", new CliOptions { Transport = "loopback", Presenter = ["ascii"] });
            var manager = new ConnectionManager(null, 10, new HostEvents(), store);

            Assert.IsTrue(manager.HasProject);
            Assert.AreEqual("Sim", manager.Project().Single().Name);
            Assert.IsNull(manager.Upsert("Lab", """{"Transport":"tcp","Host":"10.0.0.5","Port":23}"""));
            CollectionAssert.AreEqual(new[] { "Lab", "Sim" }, store.List().ToArray());
            Assert.AreEqual("10.0.0.5", manager.ProjectOptions("lab")!.Host);

            var opened = await manager.OpenAsync("Sim");
            Assert.IsNotNull(opened);
            await manager.CloseAllAsync();

            Assert.IsTrue(manager.Remove("Lab"));
            Assert.IsFalse(manager.Remove("Lab"));
            Assert.IsNull(await manager.OpenAsync("Lab"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task TheStreamMonitor_FollowsAnExtraConnection_UntilItCloses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"devterm-web-monitor-{Guid.NewGuid():N}");
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("Bench Scope", new CliOptions { Transport = "loopback", Presenter = ["ascii"] });
            var manager = new ConnectionManager(null, 10, new HostEvents(), store);
            var built = WebHost.Build(new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true }, new WebOptions { Urls = "http://127.0.0.1:0", Token = "secret" }, []);
            await using (built.Hub)
            {
                await built.Hub.StartAsync();
                using var monitor = new WebStreamMonitor(built.Hub, new ConverterToolsStore(Path.Combine(directory, "tools.json")), manager);
                var before = monitor.Monitor.DeviceName;

                var opened = await manager.OpenAsync("Bench Scope");
                Assert.IsNotNull(opened);
                Assert.AreEqual($"{before}, {before}", monitor.Monitor.DeviceName);

                await manager.CloseAsync(opened.Id);
                Assert.AreEqual(before, monitor.Monitor.DeviceName);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void WithNeitherAProjectNorAStore_HasNothingToEdit()
    {
        var manager = new ConnectionManager(null, 10, new HostEvents());

        Assert.IsFalse(manager.HasProject);
        Assert.IsEmpty(manager.Project());
        Assert.IsNotNull(manager.Upsert("x", """{"Transport":"loopback"}"""));
    }
}
