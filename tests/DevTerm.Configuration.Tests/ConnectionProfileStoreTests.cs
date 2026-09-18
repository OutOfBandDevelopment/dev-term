using System.IO.Compression;
using System.IO.Ports;

namespace DevTerm.Configuration.Tests;

[TestCategory("UNIT")]
[TestClass]
public sealed class ConnectionProfileStoreTests
{
    private static CliOptions BuildSerialOptions() => new()
    {
        Transport = "serial",
        Port = "COM3",
        Baud = 4800,
        DataBits = 8,
        Parity = Parity.None,
        StopBits = StopBits.One,
        Handshake = Handshake.RequestToSend,
        Presenter = ["ascii"],
        LineEnding = LineEnding.Cr,
        AsciiMaxLineLength = 512,
        ManifestName = "tek-2230",
        // One-shot/mode flags should not survive a save/load round trip.
        Tui = false,
        Cli = true,
        ListPorts = true,
    };

    [TestMethod]
    public void SaveThenLoad_RoundTripsConnectionFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", BuildSerialOptions());

            var loaded = store.Load("tek2230");

            Assert.AreEqual("serial", loaded.Transport);
            Assert.AreEqual("COM3", loaded.Port);
            Assert.AreEqual(4800, loaded.Baud);
            Assert.AreEqual(Handshake.RequestToSend, loaded.Handshake);
            CollectionAssert.AreEqual(new[] { "ascii" }, loaded.Presenter);
            Assert.AreEqual(LineEnding.Cr, loaded.LineEnding);
            Assert.AreEqual(512, loaded.AsciiMaxLineLength);
            Assert.AreEqual("tek-2230", loaded.ManifestName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveThenLoad_DoesNotPersistOneShotOrModeFlags()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", BuildSerialOptions());

            var loaded = store.Load("tek2230");

            Assert.IsTrue(loaded.Tui, "Tui should come back as CliOptions' own default, not the saved value.");
            Assert.IsFalse(loaded.Cli);
            Assert.IsFalse(loaded.ListPorts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Save_TcpTransport_OnlySavesTcpFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("bridge", new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23 });

            var loaded = store.Load("bridge");

            Assert.AreEqual("192.168.0.107", loaded.Host);
            Assert.AreEqual(23, loaded.TcpPort);
            Assert.IsNull(loaded.Port, "A TCP profile shouldn't carry serial-specific fields.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void List_ReturnsSavedProfileNamesAlphabetically()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("zebra", BuildSerialOptions());
            store.Save("alpha", BuildSerialOptions());

            CollectionAssert.AreEqual(new[] { "alpha", "zebra" }, store.List().ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void List_WhenDirectoryDoesNotExist_ReturnsEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-profile-tests", Path.GetRandomFileName());
        var store = new ConnectionProfileStore(directory);

        Assert.IsEmpty(store.List());
    }

    [TestMethod]
    public void Load_UnknownProfile_Throws()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);

            Assert.ThrowsExactly<FileNotFoundException>(() => store.Load("does-not-exist"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Delete_ExistingProfile_RemovesItAndReturnsTrue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", BuildSerialOptions());

            Assert.IsTrue(store.Delete("tek2230"));
            CollectionAssert.DoesNotContain(store.List().ToArray(), "tek2230");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Delete_UnknownProfile_ReturnsFalse()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);

            Assert.IsFalse(store.Delete("does-not-exist"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportToFile_ThenLoadFromFile_RoundTripsConnectionFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "exported.json");

            ConnectionProfileStore.ExportToFile(path, BuildSerialOptions());
            var loaded = ConnectionProfileStore.LoadFromFile(path);

            Assert.AreEqual("serial", loaded.Transport);
            Assert.AreEqual("COM3", loaded.Port);
            Assert.AreEqual(4800, loaded.Baud);
            CollectionAssert.AreEqual(new[] { "ascii" }, loaded.Presenter);
            Assert.AreEqual(LineEnding.Cr, loaded.LineEnding);
            Assert.AreEqual("tek-2230", loaded.ManifestName);

            // Same exclusions as a named profile save — see BuildSerialOptions' comment.
            Assert.IsTrue(loaded.Tui);
            Assert.IsFalse(loaded.Cli);
            Assert.IsFalse(loaded.ListPorts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadFromFile_MissingFile_Throws()
    {
        var directory = CreateTempDirectory();
        try
        {
            Assert.ThrowsExactly<FileNotFoundException>(() => ConnectionProfileStore.LoadFromFile(Path.Combine(directory, "missing.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportZip_ThenImportZip_RoundTripsSelectedProfilesOnly()
    {
        var sourceDirectory = CreateTempDirectory();
        var destDirectory = CreateTempDirectory();
        try
        {
            var source = new ConnectionProfileStore(sourceDirectory);
            source.Save("alpha", BuildSerialOptions());
            source.Save("beta", BuildSerialOptions());
            source.Save("gamma", BuildSerialOptions());
            var zipPath = Path.Combine(sourceDirectory, "export.zip");

            source.ExportZip(zipPath, ["alpha", "beta"]);

            var dest = new ConnectionProfileStore(destDirectory);
            var result = dest.ImportZip(zipPath);

            Assert.AreEqual(2, result.Imported);
            Assert.AreEqual(0, result.Skipped);
            Assert.AreEqual(0, result.Renamed);
            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, dest.List().ToArray());
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(destDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportZip_UnknownProfileName_Throws()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);

            Assert.ThrowsExactly<FileNotFoundException>(() => store.ExportZip(Path.Combine(directory, "export.zip"), ["does-not-exist"]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportZip_ConflictingName_DefaultsToReplaceWhenNoResolverGiven()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "serial", Port = "COM9" });
            var zipPath = Path.Combine(directory, "export.zip");
            store.ExportZip(zipPath, ["tek2230"]);
            store.Save("tek2230", new CliOptions { Transport = "serial", Port = "COM1" }); // changed after export

            var result = store.ImportZip(zipPath);

            Assert.AreEqual(1, result.Imported);
            Assert.AreEqual(0, result.Skipped);
            Assert.AreEqual(0, result.Renamed);
            Assert.AreEqual("COM9", store.Load("tek2230").Port);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportZip_ConflictingName_SkipsWhenResolverSaysSkip()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "serial", Port = "COM9" });
            var zipPath = Path.Combine(directory, "export.zip");
            store.ExportZip(zipPath, ["tek2230"]);
            store.Save("tek2230", new CliOptions { Transport = "serial", Port = "COM1" });

            var result = store.ImportZip(zipPath, _ => ZipImportConflictResolution.Skip);

            Assert.AreEqual(0, result.Imported);
            Assert.AreEqual(1, result.Skipped);
            Assert.AreEqual(0, result.Renamed);
            Assert.AreEqual("COM1", store.Load("tek2230").Port);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportZip_ConflictingName_RenamesWhenResolverSaysRename()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "serial", Port = "COM9" });
            var zipPath = Path.Combine(directory, "export.zip");
            store.ExportZip(zipPath, ["tek2230"]);

            var result = store.ImportZip(zipPath, _ => ZipImportConflictResolution.Rename);

            Assert.AreEqual(1, result.Imported);
            Assert.AreEqual(0, result.Skipped);
            Assert.AreEqual(1, result.Renamed);
            CollectionAssert.AreEqual(new[] { "tek2230", "tek2230 (2)" }, store.List().ToArray());
            Assert.AreEqual("COM9", store.Load("tek2230 (2)").Port);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-profile-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }


    [TestMethod]
    public void FindName_ForOptionsThatMatchASavedProfile_ReturnsItsName()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("other", new CliOptions { Transport = "tcp", Host = "192.168.0.2", TcpPort = 23 });
            store.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.110", TcpPort = 23, Presenter = ["ascii"] });

            // Run-mode flags aren't part of a profile, so they can't cause a false mismatch.
            var running = new CliOptions { Transport = "tcp", Host = "192.168.0.110", TcpPort = 23, Presenter = ["ascii"], Cli = true };

            Assert.AreEqual("tek2230", store.FindName(running));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FindName_AfterAFieldIsEdited_ReturnsNull()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.110", TcpPort = 23 });

            Assert.IsNull(store.FindName(new CliOptions { Transport = "tcp", Host = "192.168.0.110", TcpPort = 24 }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FindName_MatchesAnOlderProfileWithNoParserAgainstItsEffectiveParser()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "legacy.json"), "{ \"Transport\": \"tcp\", \"Host\": \"10.0.0.1\", \"TcpPort\": 23, \"Presenter\": \"ascii\" }");

            Assert.AreEqual("legacy", store.FindName(new CliOptions { Transport = "tcp", Host = "10.0.0.1", TcpPort = 23, Presenter = ["ascii"], Parser = "ascii" }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FindName_SkipsAnUnreadableProfileAndKeepsLooking()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var options = new CliOptions { Transport = "tcp", Host = "192.168.0.110", TcpPort = 23 };
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "a-broken.json"), "{ not json");
            store.Save("b-good", options);

            Assert.AreEqual("b-good", store.FindName(options));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FindName_WithTwoIdenticalProfiles_ReturnsTheFirstAlphabetically()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var options = new CliOptions { Transport = "tcp", Host = "192.168.0.110", TcpPort = 23 };
            store.Save("zulu", options);
            store.Save("alpha", options);

            Assert.AreEqual("alpha", store.FindName(options));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FindName_WithNoProfilesDirectory_ReturnsNull()
    {
        var store = new ConnectionProfileStore(Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}"));

        Assert.IsNull(store.FindName(new CliOptions { Transport = "tcp", Host = "h", TcpPort = 1 }));
    }


    private static string WriteZip(string directory, params (string EntryName, string Content)[] entries)
    {
        var zipPath = Path.Combine(directory, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return zipPath;
    }

    [TestMethod]
    public void ReadZip_ReturnsEveryJsonEntryAndIgnoresOthers()
    {
        var directory = CreateTempDirectory();
        try
        {
            var zip = WriteZip(directory, ("alpha.json", "{ \"Transport\": \"tcp\" }"), ("notes.txt", "hello"), ("beta.json", "{}"));

            var profiles = ConnectionProfileStore.ReadZip(zip);

            CollectionAssert.AreEquivalent(new[] { "alpha", "beta" }, profiles.Select(p => p.Name).ToArray());
            Assert.AreEqual("{ \"Transport\": \"tcp\" }", profiles.Single(p => p.Name == "alpha").Json);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReadZip_WithAnEntryThatIsNotJson_ThrowsNamingTheEntry()
    {
        var directory = CreateTempDirectory();
        try
        {
            var zip = WriteZip(directory, ("good.json", "{}"), ("broken.json", "{ nope"));

            var ex = Assert.ThrowsExactly<InvalidDataException>(() => ConnectionProfileStore.ReadZip(zip));

            StringAssert.Contains(ex.Message, "broken.json");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReadZip_WithTheSameNameInTwoFolders_KeepsTheLastOne()
    {
        var directory = CreateTempDirectory();
        try
        {
            var zip = WriteZip(directory, ("a/dup.json", "{ \"Host\": \"first\" }"), ("b/dup.json", "{ \"Host\": \"last\" }"));

            var profiles = ConnectionProfileStore.ReadZip(zip);

            Assert.AreEqual(1, profiles.Count);
            StringAssert.Contains(profiles[0].Json, "last");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceAll_DeletesEveryExistingProfileIncludingOnesTheZipDoesNotMention()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles"));
            store.Save("keep-me-not", new CliOptions { Transport = "tcp", Host = "10.0.0.1", TcpPort = 23 });
            store.Save("shared", new CliOptions { Transport = "tcp", Host = "10.0.0.2", TcpPort = 23 });
            var zip = WriteZip(directory, ("shared.json", "{ \"Transport\": \"tcp\", \"Host\": \"192.168.9.9\", \"TcpPort\": 99 }"), ("fresh.json", "{ \"Transport\": \"tcp\", \"Host\": \"192.168.9.8\", \"TcpPort\": 98 }"));

            var removed = store.ReplaceAll(ConnectionProfileStore.ReadZip(zip));

            Assert.AreEqual(2, removed);
            CollectionAssert.AreEqual(new[] { "fresh", "shared" }, store.List().ToArray());
            Assert.AreEqual("192.168.9.9", store.Load("shared").Host, "An overwritten name takes the zip's content.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
