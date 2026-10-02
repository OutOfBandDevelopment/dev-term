using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

/// <summary>The app-wide converter tools (<see cref="ConverterToolsStore"/>) and how they merge with a profile's older ones.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ConverterToolsStoreTests
{
    private static StreamConvertToolOptions Tool(string name, string path = "gs.exe") => new() { Name = name, Path = path, Arguments = "{input} {output}", Formats = "ps", Dpi = 200 };

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "devterm-converter-store", Path.GetRandomFileName(), "converter-tools.json");

    [TestMethod]
    public void Load_MissingFile_IsEmpty()
    {
        Assert.AreEqual(0, new ConverterToolsStore(TempFile()).Load().Count);
    }

    [TestMethod]
    public void Load_CorruptFile_IsEmptyNotAnError()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.AreEqual(0, new ConverterToolsStore(path).Load().Count);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [TestMethod]
    public void SaveThenLoad_RoundTripsEveryFieldInOrder()
    {
        var path = TempFile();
        try
        {
            var store = new ConverterToolsStore(path);
            store.Save([Tool("gs"), Tool("gpcl", "gpcl6.exe")]);

            var loaded = new ConverterToolsStore(path).Load();

            CollectionAssert.AreEqual(new[] { "gs", "gpcl" }, loaded.Select(t => t.Name).ToArray());
            Assert.AreEqual("gpcl6.exe", loaded[1].Path);
            Assert.AreEqual("{input} {output}", loaded[0].Arguments);
            Assert.AreEqual("ps", loaded[0].Formats);
            Assert.AreEqual(200, loaded[0].Dpi);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [TestMethod]
    public void Merge_GlobalFirst_ProfileToolsFillInMissingNames_GlobalWinsOnAClash()
    {
        var merged = ConverterToolsStore.Merge([Tool("gs", "global.exe")], [Tool("GS", "profile.exe"), Tool("old")]);

        CollectionAssert.AreEqual(new[] { "gs", "old" }, merged.Select(t => t.Name).ToArray());
        Assert.AreEqual("global.exe", merged[0].Path);
    }

    [TestMethod]
    public void FromCliOptions_AddsTheGlobalTools_AndToolModeFindsAGlobalOne()
    {
        var options = StreamCaptureConverterOptions.FromCliOptions(new CliOptions { StreamConvertMode = "tool:gs" }, [Tool("gs")]);

        Assert.AreEqual(StreamConversionMode.Tool, options.Mode);
        Assert.AreEqual("gs", options.ToolName);
        Assert.AreEqual("gs", options.Tools.Single().Name);
    }

    [TestMethod]
    public void FromCliOptions_WithoutGlobalTools_KeepsTheProfilesOwn()
    {
        var options = StreamCaptureConverterOptions.FromCliOptions(new CliOptions { StreamConvertTools = [Tool("old")] });

        Assert.AreEqual("old", options.Tools.Single().Name);
    }
}
