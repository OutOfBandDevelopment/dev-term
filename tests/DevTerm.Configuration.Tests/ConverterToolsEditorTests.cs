using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// The converter-tools list editor (<see cref="ConverterToolsEditor"/>) both front ends' "Converter tools" dialogs share,
/// and that the tools survive the connection editor's build and a profile save/load.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ConverterToolsEditorTests
{
    private static StreamConvertToolOptions Tool(string name, string path = "gs.exe") => new() { Name = name, Path = path, Arguments = "{input} {output}" };

    [TestMethod]
    public void Edits_GoToCopies_SoCancellingLeavesTheOriginalsAlone()
    {
        var original = Tool("gs");
        var editor = new ConverterToolsEditor([original]);

        editor.Tools[0].Name = "renamed";

        Assert.AreEqual("gs", original.Name);
    }

    [TestMethod]
    public void Add_GivesEachNewToolAUniqueName()
    {
        var editor = new ConverterToolsEditor([Tool("tool2")]);

        var first = editor.Add();
        var second = editor.Add();

        Assert.AreNotEqual(first.Name, second.Name);
        Assert.AreEqual(3, editor.Tools.Count);
        Assert.AreEqual(3, editor.Tools.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void MoveUpAndDown_ReorderAndStopAtTheEnds()
    {
        var editor = new ConverterToolsEditor([Tool("a"), Tool("b"), Tool("c")]);

        Assert.AreEqual(0, editor.MoveUp(1));
        Assert.AreEqual("b", editor.Tools[0].Name);
        Assert.AreEqual(0, editor.MoveUp(0));
        Assert.AreEqual(2, editor.MoveDown(1));
        Assert.AreEqual(2, editor.MoveDown(2));
        CollectionAssert.AreEqual(new[] { "b", "c", "a" }, editor.Tools.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public void Remove_ReturnsTheIndexToSelectNext()
    {
        var editor = new ConverterToolsEditor([Tool("a"), Tool("b")]);

        Assert.AreEqual(0, editor.Remove(1));
        Assert.AreEqual(-1, editor.Remove(0));
        Assert.AreEqual(0, editor.Tools.Count);
    }

    [TestMethod]
    public void Validate_ReportsTheFirstProblem()
    {
        Assert.IsNull(new ConverterToolsEditor([Tool("a"), Tool("b")]).Validate());
        StringAssert.Contains(new ConverterToolsEditor([Tool(" ")]).Validate(), "needs a name");
        StringAssert.Contains(new ConverterToolsEditor([Tool("a"), Tool("A")]).Validate(), "unique");
        StringAssert.Contains(new ConverterToolsEditor([Tool("a", "")]).Validate(), "path");
        var badDpi = Tool("a");
        badDpi.Dpi = 0;
        StringAssert.Contains(new ConverterToolsEditor([badDpi]).Validate(), "DPI");
        var noExtension = Tool("a");
        noExtension.OutputExtension = " ";
        StringAssert.Contains(new ConverterToolsEditor([noExtension]).Validate(), "extension");
    }

    [TestMethod]
    public void ToList_TrimsTheFieldsAndTheExtensionsDot()
    {
        var tool = Tool("  gs ", "  C:\\gs\\gs.exe ");
        tool.OutputExtension = " .png ";
        tool.Formats = " ps, eps ";

        var saved = new ConverterToolsEditor([tool]).ToList().Single();

        Assert.AreEqual("gs", saved.Name);
        Assert.AreEqual("C:\\gs\\gs.exe", saved.Path);
        Assert.AreEqual("png", saved.OutputExtension);
        Assert.AreEqual("ps, eps", saved.Formats);
    }

    [TestMethod]
    public void ConnectionEditor_CarriesTheToolsAndModeThroughBuildOptions_AndMarksEditsDirty()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-converter-tools", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var initial = new CliOptions { Transport = "loopback", StreamConvertMode = "auto", StreamConvertDpi = 300, StreamConvertTools = [Tool("gs")] };
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), initial);
            Assert.IsFalse(vm.IsDirty);

            vm.ConverterTools = [Tool("gs"), Tool("gpcl")];

            Assert.IsTrue(vm.IsDirty);
            var built = vm.BuildOptions();
            Assert.AreEqual("auto", built.StreamConvertMode);
            Assert.AreEqual(300, built.StreamConvertDpi);
            CollectionAssert.AreEqual(new[] { "gs", "gpcl" }, built.StreamConvertTools.Select(t => t.Name).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Profile_SaveThenLoad_RoundTripsTheTopLevelConverterSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-converter-tools", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("scope", new CliOptions
            {
                Transport = "loopback",
                StreamConvertMode = "externaltool",
                StreamConvertExternalToolPath = @"C:\gs\gs.exe",
                StreamConvertExternalToolArguments = "-r{dpi} {input}",
                StreamConvertDpi = 300,
                StreamConvertOutputExtension = "jpg",
            });

            var loaded = store.Load("scope");

            Assert.AreEqual("externaltool", loaded.StreamConvertMode);
            Assert.AreEqual(@"C:\gs\gs.exe", loaded.StreamConvertExternalToolPath);
            Assert.AreEqual("-r{dpi} {input}", loaded.StreamConvertExternalToolArguments);
            Assert.AreEqual(300, loaded.StreamConvertDpi);
            Assert.AreEqual("jpg", loaded.StreamConvertOutputExtension);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Profile_SaveThenLoad_RoundTripsTheConverterSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-converter-tools", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ConnectionProfileStore(directory);
            var tool = Tool("gs");
            tool.Formats = "ps";
            tool.OutputExtension = "jpg";
            tool.Dpi = 200;
            store.Save("scope", new CliOptions { Transport = "loopback", StreamConvertMode = "tool:gs", StreamConvertTools = [tool] });

            var loaded = store.Load("scope");

            Assert.AreEqual("tool:gs", loaded.StreamConvertMode);
            var back = loaded.StreamConvertTools.Single();
            Assert.AreEqual("gs", back.Name);
            Assert.AreEqual("gs.exe", back.Path);
            Assert.AreEqual("{input} {output}", back.Arguments);
            Assert.AreEqual("ps", back.Formats);
            Assert.AreEqual("jpg", back.OutputExtension);
            Assert.AreEqual(200, back.Dpi);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
