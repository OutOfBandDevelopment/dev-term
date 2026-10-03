using System.IO.Compression;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests.Tests;

/// <summary>
/// Round-trips and loads a real manifest — a Korad KA3005P-style bench power supply, matching
/// docs/design/features/scpi-instrument-control.md's actual target hardware — rather than a
/// synthetic minimal example.
/// </summary>
[TestCategory(TestCategories.Unit)]
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
    [DoNotParallelize]
    public void Load_Zip_ExtractsUnderTheAppDataFolder()
    {
        var directory = CreateTempDirectory();
        var zipPath = directory + ".zip";
        var home = CreateTempDirectory();
        var previous = Environment.GetEnvironmentVariable("DEVTERM_HOME");
        try
        {
            Environment.SetEnvironmentVariable("DEVTERM_HOME", home);
            File.WriteAllText(
                Path.Combine(directory, DeviceManifestLoader.ManifestFileName),
                DeviceManifestSerializer.ToJson(BuildKoradManifest()));
            ZipFile.CreateFromDirectory(directory, zipPath);

            DeviceManifestLoader.Load(zipPath);
            DeviceManifestLoader.Load(zipPath);

            Assert.IsTrue(Directory.GetDirectories(Path.Combine(home, "manifest-cache")).Length >= 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEVTERM_HOME", previous);
            Directory.Delete(directory, recursive: true);
            Directory.Delete(home, recursive: true);
            File.Delete(zipPath);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void FromXml_DocumentWithAnInternalDtdEntity_DoesNotExpandIt()
    {
        // Regression test for bug 049: a manifest's device.xml is untrusted input, and
        // XmlSerializer.Deserialize(TextReader) applied no DtdProcessing restriction, so an
        // internal-entity DOCTYPE (billion-laughs style) could expand into the deserialized model.
        // See docs/bugs/resolved/049-uidefinition-xml-dtd.md.
        const string maliciousXml = """
            <?xml version="1.0"?>
            <!DOCTYPE DeviceManifest [<!ENTITY evil "expanded">]>
            <DeviceManifest><Name>&evil;</Name></DeviceManifest>
            """;

        Assert.ThrowsExactly<InvalidOperationException>(() => DeviceManifestSerializer.FromXml(maliciousXml));
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
    [TestCategory(TestCategories.BugRegression)]
    public void Load_ZipWithTooManyEntries_ThrowsAndDoesNotExtract()
    {
        var directory = CreateTempDirectory();
        var zipPath = directory + ".zip";
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                for (var i = 0; i < 501; i++)
                {
                    archive.CreateEntry($"entry-{i}.txt");
                }
            }

            Assert.ThrowsExactly<InvalidDataException>(() => DeviceManifestLoader.Load(zipPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            File.Delete(zipPath);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Load_SameZipTwice_ReusesOneExtractionFolderInsteadOfLeakingANewOneEachTime()
    {
        var directory = CreateTempDirectory();
        var zipPath = directory + ".zip";
        string? firstExtractDirectory = null;
        string? secondExtractDirectory = null;
        try
        {
            File.WriteAllText(
                Path.Combine(directory, DeviceManifestLoader.ManifestFileName),
                DeviceManifestSerializer.ToJson(BuildKoradManifest(inlineUi: BuildKoradUi())));
            ZipFile.CreateFromDirectory(directory, zipPath);

            DeviceManifestLoader.Load(zipPath, validate: true, out var firstManifestFile);
            firstExtractDirectory = Path.GetDirectoryName(firstManifestFile);

            DeviceManifestLoader.Load(zipPath, validate: true, out var secondManifestFile);
            secondExtractDirectory = Path.GetDirectoryName(secondManifestFile);

            Assert.AreEqual(firstExtractDirectory, secondExtractDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            File.Delete(zipPath);
            if (firstExtractDirectory is not null && Directory.Exists(firstExtractDirectory))
            {
                Directory.Delete(firstExtractDirectory, recursive: true);
            }

            if (secondExtractDirectory is not null && secondExtractDirectory != firstExtractDirectory && Directory.Exists(secondExtractDirectory))
            {
                Directory.Delete(secondExtractDirectory, recursive: true);
            }
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

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Load_UiFileIsARootedPath_ThrowsInsteadOfReadingIt()
    {
        var directory = CreateTempDirectory();
        var outsideDirectory = CreateTempDirectory();
        try
        {
            var outsideUiPath = Path.Combine(outsideDirectory, "evil.json");
            File.WriteAllText(outsideUiPath, UiDefinitionSerializer.ToJson(BuildKoradUi()));

            var manifest = BuildKoradManifest(uiFile: outsideUiPath);
            File.WriteAllText(Path.Combine(directory, DeviceManifestLoader.ManifestFileName), DeviceManifestSerializer.ToJson(manifest));

            Assert.ThrowsExactly<InvalidOperationException>(() => DeviceManifestLoader.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Load_UiFileEscapesTheManifestFolderWithDotDot_Throws()
    {
        var directory = CreateTempDirectory();
        try
        {
            var manifest = BuildKoradManifest(uiFile: @"..\..\evil.json");
            File.WriteAllText(Path.Combine(directory, DeviceManifestLoader.ManifestFileName), DeviceManifestSerializer.ToJson(manifest));

            Assert.ThrowsExactly<InvalidOperationException>(() => DeviceManifestLoader.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Load_KaitaiFileIsARootedPath_ThrowsInsteadOfProbingIt()
    {
        var directory = CreateTempDirectory();
        try
        {
            var manifest = BuildKoradManifest(inlineUi: BuildKoradUi());
            manifest.Inbound!.KaitaiFile = Path.Combine(Path.GetTempPath(), "some-unrelated-file.ksy");
            File.WriteAllText(Path.Combine(directory, DeviceManifestLoader.ManifestFileName), DeviceManifestSerializer.ToJson(manifest));

            Assert.ThrowsExactly<InvalidOperationException>(() => DeviceManifestLoader.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Save_UiFileIsARootedPath_ThrowsInsteadOfWritingOutsideTheManifestFolder()
    {
        var directory = CreateTempDirectory();
        var outsideDirectory = CreateTempDirectory();
        try
        {
            var outsideUiPath = Path.Combine(outsideDirectory, "evil.json");
            var manifest = BuildKoradManifest(inlineUi: BuildKoradUi(), uiFile: outsideUiPath);

            Assert.ThrowsExactly<InvalidOperationException>(() => DeviceManifestWriter.Save(manifest, directory));
            Assert.IsFalse(File.Exists(outsideUiPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Save_KaitaiFileEscapesTheManifestFolderWithDotDot_Throws()
    {
        var directory = CreateTempDirectory();
        var sourceDirectory = CreateTempDirectory();
        try
        {
            var manifest = BuildKoradManifest(inlineUi: BuildKoradUi());
            manifest.Inbound!.KaitaiFile = @"..\..\evil.ksy";

            Assert.ThrowsExactly<InvalidOperationException>(() => DeviceManifestWriter.Save(manifest, directory, sourceDirectory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_UiFileOrKaitaiFileEscapesTheManifestFolder_ReportsAnError()
    {
        var manifest = BuildKoradManifest(inlineUi: BuildKoradUi(), uiFile: @"C:\evil.json");
        manifest.Inbound!.KaitaiFile = @"..\evil.ksy";

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsFalse(result.IsValid);
        Assert.Contains("UiFile", string.Join(" ", result.Errors));
        Assert.Contains("KaitaiFile", string.Join(" ", result.Errors));
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_StripChartHistoryLengthAboveTheHardMaximum_Warns()
    {
        // Regression test for bug 050: StripChartControl.HistoryLength came straight from the
        // manifest with no upper bound, so a manifest could make the chart's per-sample queue grow
        // without limit. See docs/bugs/resolved/050-strip-chart-history-unbounded.md.
        var manifest = BuildKoradManifest(inlineUi: new UiDefinition
        {
            Name = "Korad KA3005P",
            Sections =
            [
                new UiSection
                {
                    Controls =
                    [
                        new StripChartControl
                        {
                            Id = "trace",
                            Label = "Trace",
                            HistoryLength = StripChartState.MaxCapacity + 1,
                            Channels = [new ChartChannel { Id = "value" }],
                        },
                    ],
                },
            ],
        });

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsTrue(result.IsValid, "An excessive history length is clamped, not rejected.");
        Assert.Contains($"Trace' declares a history length of {StripChartState.MaxCapacity + 1}", string.Join(" ", result.Warnings));
    }

    [TestMethod]
    public void Validate_PatternExampleThatDoesNotMatch_Warns_AndRoundTripsThroughJson()
    {
        var manifest = BuildKoradManifest();
        manifest.Inbound = new InboundProtocol { Patterns = [new ResponsePattern { Name = "v", Match = @"^V=(\d+)$", Example = "oops" }] };

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsTrue(result.IsValid);
        Assert.Contains("example line doesn't match", string.Join(" ", result.Warnings));
        manifest.Inbound.Patterns[0].Example = "V=12";
        Assert.DoesNotContain("example line", string.Join(" ", DeviceManifestValidator.Validate(manifest).Warnings));
        var again = DeviceManifestSerializer.FromJson(DeviceManifestSerializer.ToJson(manifest));
        Assert.AreEqual("V=12", again.Inbound!.Patterns[0].Example);
    }

    [TestMethod]
    public void Validate_IndicatorExpression_InvalidSyntax_ReportsAnError()
    {
        var manifest = BuildKoradManifest(inlineUi: new UiDefinition
        {
            Name = "Korad KA3005P",
            Sections = [new UiSection { Controls = [new IndicatorControl { Id = "i", Label = "Readout", Expression = "1 +" }] }],
        });

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsFalse(result.IsValid);
        Assert.Contains("Readout", string.Join(" ", result.Errors));
    }

    [TestMethod]
    public void Validate_IndicatorExpression_ValidSyntax_ReportsNoError()
    {
        var manifest = BuildKoradManifest(inlineUi: new UiDefinition
        {
            Name = "Korad KA3005P",
            Sections = [new UiSection { Controls = [new IndicatorControl { Id = "i", Label = "Readout", Expression = "{raw_mv} / 1000" }] }],
        });

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Validate_ChartChannelExpression_InvalidSyntax_ReportsAnError(bool stripChart)
    {
        UiControl chart = stripChart
            ? new StripChartControl { Id = "trace", Label = "Trace", Channels = [new ChartChannel { Id = "value", Expression = "1 +" }] }
            : new BarGraphControl { Id = "levels", Label = "Levels", Channels = [new ChartChannel { Id = "value", Expression = "1 +" }] };
        var manifest = BuildKoradManifest(inlineUi: new UiDefinition { Name = "Korad KA3005P", Sections = [new UiSection { Controls = [chart] }] });

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsFalse(result.IsValid);
        Assert.Contains("invalid expression", string.Join(" ", result.Errors));
    }

    [TestMethod]
    public void Validate_ChartChannelExpression_ValidSyntax_ReportsNoError()
    {
        var manifest = BuildKoradManifest(inlineUi: new UiDefinition
        {
            Name = "Korad KA3005P",
            Sections = [new UiSection { Controls = [new BarGraphControl { Id = "levels", Label = "Levels", Channels = [new ChartChannel { Id = "value", Expression = "{raw} / 1000" }] }] }],
        });

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void Validate_ButtonParameterExpression_InvalidSyntax_ReportsAnErrorWithItsIndex()
    {
        var manifest = BuildKoradManifest(inlineUi: new UiDefinition
        {
            Name = "Korad KA3005P",
            Sections =
            [
                new UiSection
                {
                    Controls =
                    [
                        new NumericControl { Id = "field", Label = "Field" },
                        new ButtonControl { Id = "send", Label = "Send", ParameterFieldIds = ["field"], ParameterExpressions = ["1 +"] },
                    ],
                },
            ],
        });

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsFalse(result.IsValid);
        Assert.Contains("index 0", string.Join(" ", result.Errors));
    }

    [TestMethod]
    public void Validate_ButtonParameterExpression_BlankOrValidEntries_ReportNoError()
    {
        var manifest = BuildKoradManifest(inlineUi: new UiDefinition
        {
            Name = "Korad KA3005P",
            Sections =
            [
                new UiSection
                {
                    Controls =
                    [
                        new NumericControl { Id = "a", Label = "A" },
                        new NumericControl { Id = "b", Label = "B" },
                        new ButtonControl { Id = "send", Label = "Send", ParameterFieldIds = ["a", "b"], ParameterExpressions = [null, "round({a} * 2)"] },
                    ],
                },
            ],
        });

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsTrue(result.IsValid);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-manifest-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
