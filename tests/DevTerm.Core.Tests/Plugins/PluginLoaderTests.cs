using System.Buffers;
using DevTerm.Core.Hosting;
using DevTerm.Core.Plugins;
using DevTerm.Core.Presenters;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Core.Tests.Plugins;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class PluginLoaderTests
{
    private static string SamplePluginFolder => Path.Combine(AppContext.BaseDirectory, "plugins", "sample");

    private static string NewPluginsDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    // A loaded plugin assembly stays locked for the life of the process (the load context isn't collectible), so cleanup is best effort.
    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void CopySample(string pluginsDirectory, string folder, Func<string, string>? manifestEdit = null)
    {
        var target = Path.Combine(pluginsDirectory, folder);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(SamplePluginFolder))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        if (manifestEdit is not null)
        {
            var manifest = Path.Combine(target, PluginManifest.FileName);
            File.WriteAllText(manifest, manifestEdit(File.ReadAllText(manifest)));
        }
    }

    [TestMethod]
    public void LoadAll_RegistersAPluginsPresenter_SoTheCatalogFindsItAndItRenders()
    {
        var services = new ServiceCollection().AddDevTermCore();

        var results = PluginLoader.LoadAll(Path.GetDirectoryName(SamplePluginFolder), services);

        Assert.AreEqual(1, results.Count, string.Join("; ", results.Select(r => r.Message)));
        Assert.IsTrue(results[0].Loaded, results[0].Message);
        using var provider = services.BuildServiceProvider();
        var presenter = provider.GetRequiredService<PresenterCatalog>().Get("sample");
        Assert.AreEqual("sample: 3 bytes", presenter.Render(new ReadOnlySequence<byte>([1, 2, 3]))[0]);
        // The plugin's presenter implements the host's own IPresenter, not a second copy loaded beside it.
        Assert.AreSame(typeof(IPresenter).Assembly, presenter.GetType().GetInterface(nameof(IPresenter))!.Assembly);
    }

    [TestMethod]
    public void LoadAll_SkipsAPluginBuiltForAnotherContract_AndSaysWhy()
    {
        var directory = NewPluginsDirectory();
        try
        {
            CopySample(directory, "future", text => text.Replace("\"contract\": 1", "\"contract\": 99", StringComparison.Ordinal));
            var services = new ServiceCollection();

            var results = PluginLoader.LoadAll(directory, services);

            Assert.IsFalse(results.Single().Loaded);
            StringAssert.Contains(results.Single().Message, "contract 99");
            Assert.AreEqual(0, services.Count);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public void LoadAll_ABrokenPluginDoesNotStopTheGoodOnes()
    {
        var directory = NewPluginsDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "a-broken"));
            File.WriteAllText(Path.Combine(directory, "a-broken", PluginManifest.FileName), "{ not json");
            CopySample(directory, "b-good");
            Directory.CreateDirectory(Path.Combine(directory, "no-manifest"));
            var services = new ServiceCollection();

            var results = PluginLoader.LoadAll(directory, services);

            Assert.AreEqual(2, results.Count);
            Assert.IsFalse(results[0].Loaded);
            Assert.IsTrue(results[1].Loaded, results[1].Message);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public void LoadAll_RefusesAnAssemblyPathOutsideThePluginFolder()
    {
        var directory = NewPluginsDirectory();
        try
        {
            CopySample(directory, "escape", text => text.Replace("DevTerm.Plugins.Sample.dll", "..\\\\outside.dll", StringComparison.Ordinal));

            var result = PluginLoader.LoadAll(directory, new ServiceCollection()).Single();

            Assert.IsFalse(result.Loaded);
            StringAssert.Contains(result.Message, "isn't in the plugin folder");
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public void LoadAll_AMissingOrEmptyDirectoryLoadsNothing()
    {
        var services = new ServiceCollection();

        Assert.AreEqual(0, PluginLoader.LoadAll(null, services).Count);
        Assert.AreEqual(0, PluginLoader.LoadAll(Path.Combine(Path.GetTempPath(), "devterm-no-such-dir"), services).Count);
    }
}
