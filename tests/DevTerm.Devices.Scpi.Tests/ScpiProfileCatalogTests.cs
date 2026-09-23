namespace DevTerm.Devices.Scpi.Tests;

/// <summary>
/// Verifies <see cref="ScpiProfileCatalog"/>'s loading mechanism (bundled <c>Profiles/</c> folder
/// plus a drop-in <c>ScpiProfiles/</c> folder, via the internal <see cref="ScpiProfileCatalog.Load"/>
/// against a synthetic temp directory — no dependency on this process's real output layout), enum
/// string parsing, IDN auto-detect matching, and the code-constructed Generic fallback. No real
/// instrument involved, so UNIT.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class ScpiProfileCatalogTests
{
    private const string MinimalProfileJson = """
        {
            "Name": "Synthetic Instrument",
            "IdnPattern": "SYN9000",
            "Terminator": "\r\n",
            "Commands": [
                { "Id": "idn", "Label": "Identify", "Template": "*IDN?", "IsQuery": true },
                {
                    "Id": "freq",
                    "Label": "Set Frequency",
                    "Template": "SOUR1:FREQ {Frequency}",
                    "Parameters": [ { "Name": "Frequency", "Kind": "Numeric", "Minimum": 0, "Maximum": 100 } ]
                }
            ]
        }
        """;

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DevTermScpiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [TestMethod]
    public void Load_NoProfilesOrScpiProfilesFolder_ReturnsEmpty()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var profiles = ScpiProfileCatalog.Load(baseDirectory);

            Assert.IsEmpty(profiles);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Load_FromBundledProfilesFolder_ParsesNameAndEnumStrings()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var profilesDir = Path.Combine(baseDirectory, "Profiles");
            Directory.CreateDirectory(profilesDir);
            File.WriteAllText(Path.Combine(profilesDir, "synthetic.json"), MinimalProfileJson);

            var profiles = ScpiProfileCatalog.Load(baseDirectory);

            Assert.HasCount(1, profiles);
            Assert.AreEqual("Synthetic Instrument", profiles[0].Name);
            Assert.AreEqual("\r\n", profiles[0].Terminator);
            Assert.AreEqual(ScpiParameterKind.Numeric, profiles[0].Commands[1].Parameters[0].Kind);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Load_FromDropInScpiProfilesFolder_IsAlsoIncluded()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var dropInDir = Path.Combine(baseDirectory, "ScpiProfiles");
            Directory.CreateDirectory(dropInDir);
            File.WriteAllText(Path.Combine(dropInDir, "synthetic.json"), MinimalProfileJson);

            var profiles = ScpiProfileCatalog.Load(baseDirectory);

            Assert.HasCount(1, profiles);
            Assert.AreEqual("Synthetic Instrument", profiles[0].Name);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Load_BundledAndDropIn_ReturnsBundledFirst()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var profilesDir = Path.Combine(baseDirectory, "Profiles");
            var dropInDir = Path.Combine(baseDirectory, "ScpiProfiles");
            Directory.CreateDirectory(profilesDir);
            Directory.CreateDirectory(dropInDir);
            File.WriteAllText(Path.Combine(profilesDir, "bundled.json"), MinimalProfileJson.Replace("Synthetic Instrument", "Bundled"));
            File.WriteAllText(Path.Combine(dropInDir, "extra.json"), MinimalProfileJson.Replace("Synthetic Instrument", "DroppedIn"));

            var profiles = ScpiProfileCatalog.Load(baseDirectory);

            Assert.HasCount(2, profiles);
            Assert.AreEqual("Bundled", profiles[0].Name);
            Assert.AreEqual("DroppedIn", profiles[1].Name);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Generic_HasNoIdnPatternAndBaselineCommonCommands()
    {
        var generic = ScpiProfileCatalog.Generic;

        Assert.IsTrue(string.IsNullOrEmpty(generic.IdnPattern));
        CollectionAssert.AreEquivalent(new[] { "idn", "rst", "cls", "opc" }, generic.Commands.Select(c => c.Id).ToArray());
    }

    [TestMethod]
    public void TryMatchByIdn_MatchingReply_ReturnsThatBundledProfile()
    {
        var matched = ScpiProfileCatalog.TryMatchByIdn("RIGOL TECHNOLOGIES,DG1022Z,DG1ZA123456,1.01");

        Assert.IsNotNull(matched);
        Assert.IsTrue(matched!.Name.Contains("DG1022", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TryMatchByIdn_NoMatchingProfile_ReturnsNull()
    {
        var matched = ScpiProfileCatalog.TryMatchByIdn("SOME,UNKNOWN,DEVICE,0.1");

        Assert.IsNull(matched);
    }

    [TestMethod]
    public void All_BundledProfiles_AreAllLoaded()
    {
        var names = ScpiProfileCatalog.All.Select(p => p.Name).ToArray();

        Assert.HasCount(8, names);
    }

    [TestMethod]
    public void Tektronix2230_HasNoIdnPatternSoItIsNeverAutoDetected()
    {
        var tek2230 = ScpiProfileCatalog.All.Single(p => p.Name.StartsWith("Tektronix 2230", StringComparison.OrdinalIgnoreCase));

        Assert.IsTrue(string.IsNullOrEmpty(tek2230.IdnPattern));
        Assert.IsTrue(tek2230.Commands.Any(c => c.Id == "id" && c.Template == "ID?"));
    }

    [TestMethod]
    public void TektronixTds2024_HasNoIdnPatternSoItIsNeverAutoDetected()
    {
        var tds2024 = ScpiProfileCatalog.All.Single(p => p.Name.Contains("TDS2024", StringComparison.OrdinalIgnoreCase));

        Assert.IsTrue(string.IsNullOrEmpty(tds2024.IdnPattern));
        Assert.IsTrue(tds2024.Commands.Any(c => c.Id == "id" && c.Template == "ID?"));
    }
}
