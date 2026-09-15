using System.IO.Compression;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests.Tests;

/// <summary>
/// Round-trips and loads a real manifest — a Korad KA3005P-style bench power supply, matching
/// docs/design/proposals/scpi-instrument-control.md's actual target hardware — rather than a
/// synthetic minimal example.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class DeviceManifestTests
{
    private static DeviceManifest BuildKoradManifest(UiDefinition? inlineUi = null, string? uiFile = null) => new()
    {
        Name = "Korad KA3005P",
        Vendor = "Korad",
        Version = "1.0",
        Description = "Programmable bench power supply",
        Transport = new TransportHint
        {
            Type = "serial",
            Options = [new TransportOption { Key = "Baud", Value = "9600" }],
        },
        Inbound = new InboundProtocol
        {
            Patterns =
            [
                new ResponsePattern { Name = "Identity", Match = @"^.+,KA3005P,.+$" },
            ],
        },
        OutboundCommands =
        [
            new OutboundCommand
            {
                Name = "Set Voltage",
                Template = "VSET1:{value}\r",
                Parameters = [new CommandParameter { Name = "value", Type = "number", Minimum = 0, Maximum = 30, Unit = "V" }],
            },
            new OutboundCommand
            {
                Name = "Set Current",
                Template = "ISET1:{value}\r",
                Parameters = [new CommandParameter { Name = "value", Type = "number", Minimum = 0, Maximum = 5, Unit = "A" }],
            },
        ],
        Ui = inlineUi,
        UiFile = uiFile,
    };

    private static UiDefinition BuildKoradUi() => new()
    {
        Name = "Korad KA3005P",
        Sections =
        [
            new UiSection
            {
                Label = "Output",
                Controls =
                [
                    new SliderControl { Id = "value", Label = "Voltage", Minimum = 0, Maximum = 30, Unit = "V" },
                    new SliderControl { Id = "value", Label = "Current", Minimum = 0, Maximum = 5, Unit = "A" },
                ],
            },
        ],
    };

    [TestMethod]
    public void ToJson_ThenFromJson_PreservesEverythingIncludingInlineUi()
    {
        var original = BuildKoradManifest(inlineUi: BuildKoradUi());

        var json = DeviceManifestSerializer.ToJson(original);
        var roundTripped = DeviceManifestSerializer.FromJson(json);

        Assert.AreEqual(original.Name, roundTripped.Name);
        Assert.AreEqual(original.Transport!.Type, roundTripped.Transport!.Type);
        Assert.AreEqual(original.Transport.Options[0].Value, roundTripped.Transport.Options[0].Value);
        Assert.AreEqual(original.Inbound!.Patterns[0].Match, roundTripped.Inbound!.Patterns[0].Match);
        Assert.HasCount(2, roundTripped.OutboundCommands);
        Assert.AreEqual("VSET1:{value}\r", roundTripped.OutboundCommands[0].Template);
        Assert.AreEqual(30, roundTripped.OutboundCommands[0].Parameters[0].Maximum);
        Assert.IsNotNull(roundTripped.Ui);
        Assert.AreEqual("Korad KA3005P", roundTripped.Ui!.Name);
        Assert.HasCount(2, roundTripped.Ui.Sections[0].Controls);
        Assert.IsInstanceOfType<SliderControl>(roundTripped.Ui.Sections[0].Controls[0]);
    }

    [TestMethod]
    public void ToXml_ThenFromXml_PreservesEverythingIncludingInlineUi()
    {
        var original = BuildKoradManifest(inlineUi: BuildKoradUi());

        var xml = DeviceManifestSerializer.ToXml(original);
        var roundTripped = DeviceManifestSerializer.FromXml(xml);

        Assert.AreEqual(original.Name, roundTripped.Name);
        Assert.HasCount(2, roundTripped.OutboundCommands);
        Assert.IsNotNull(roundTripped.Ui);
        Assert.HasCount(2, roundTripped.Ui!.Sections[0].Controls);
        Assert.IsInstanceOfType<SliderControl>(roundTripped.Ui.Sections[0].Controls[0]);
    }

    [TestMethod]
    public void Load_SingleJsonFile_LoadsDirectly()
    {
        var directory = CreateTempDirectory();
        try
        {
            var manifestPath = Path.Combine(directory, "korad.devterm.json");
            File.WriteAllText(manifestPath, DeviceManifestSerializer.ToJson(BuildKoradManifest(inlineUi: BuildKoradUi())));

            var loaded = DeviceManifestLoader.Load(manifestPath);

            Assert.AreEqual("Korad KA3005P", loaded.Name);
            Assert.IsNotNull(loaded.Ui);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Load_Folder_ResolvesExternalUiFileRelativeToManifest()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, DeviceManifestLoader.ManifestFileName),
                DeviceManifestSerializer.ToJson(BuildKoradManifest(uiFile: "ui.json")));
            File.WriteAllText(
                Path.Combine(directory, "ui.json"),
                UiDefinitionSerializer.ToJson(BuildKoradUi()));

            var loaded = DeviceManifestLoader.Load(directory);

            Assert.IsNotNull(loaded.Ui);
            Assert.AreEqual("Korad KA3005P", loaded.Ui!.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Load_Zip_ExtractsAndLoadsExactlyLikeAFolder()
    {
        var directory = CreateTempDirectory();
        var zipPath = directory + ".zip";
        try
        {
            File.WriteAllText(
                Path.Combine(directory, DeviceManifestLoader.ManifestFileName),
                DeviceManifestSerializer.ToJson(BuildKoradManifest(uiFile: "ui.json")));
            File.WriteAllText(
                Path.Combine(directory, "ui.json"),
                UiDefinitionSerializer.ToJson(BuildKoradUi()));

            ZipFile.CreateFromDirectory(directory, zipPath);

            var loaded = DeviceManifestLoader.Load(zipPath);

            Assert.AreEqual("Korad KA3005P", loaded.Name);
            Assert.IsNotNull(loaded.Ui);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            File.Delete(zipPath);
        }
    }

    [TestMethod]
    public void Load_MissingReferencedKaitaiFile_Throws()
    {
        var directory = CreateTempDirectory();
        try
        {
            var manifest = BuildKoradManifest(inlineUi: BuildKoradUi());
            manifest.Inbound!.KaitaiFile = "does-not-exist.ksy";
            File.WriteAllText(Path.Combine(directory, DeviceManifestLoader.ManifestFileName), DeviceManifestSerializer.ToJson(manifest));

            Assert.ThrowsExactly<FileNotFoundException>(() => DeviceManifestLoader.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Load_MissingManifestInFolder_ThrowsWithAHelpfulMessage()
    {
        var directory = CreateTempDirectory();
        try
        {
            Assert.ThrowsExactly<FileNotFoundException>(() => DeviceManifestLoader.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-manifest-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
