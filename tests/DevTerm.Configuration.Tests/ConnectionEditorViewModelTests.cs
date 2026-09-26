using System.IO.Compression;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Hid;
using DevTerm.Transports.Serial;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// Tests the connection-editor logic directly, independent of either front end — WPF's
/// <c>DeviceProfilesWindow</c> binds to this same view model via XAML, and the TUI's
/// <c>ConfigureMode</c> copies Terminal.Gui field values into/out of it around each button press
/// (see that class's own tests, <c>DevTerm.Console.Tests.ConfigureModeTests</c>, for the
/// Terminal.Gui-specific half of this).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ConnectionEditorViewModelTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-editor-vm-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    [TestMethod]
    public void Constructor_PopulatesFieldsFromInitialOptions()
    {
        var directory = CreateTempDirectory();
        try
        {
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] };
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), initial, "some error");

            Assert.AreEqual("tcp", vm.Transport);
            Assert.AreEqual("192.168.0.107", vm.Host);
            Assert.AreEqual("23", vm.TcpPort);
            Assert.AreSequenceEqual(["ascii"], [.. vm.SelectedPresenters]);
            Assert.AreEqual("ascii", vm.Parser, "An initial CliOptions with no Parser sends as its first presenter, as before.");
            Assert.AreEqual("some error", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectCommand_WithValidFields_SetsResultAndRaisesCloseRequested()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions { Transport = "serial" })
            {
                Transport = "tcp",
                Host = "192.168.0.107",
                TcpPort = "23",
            };

            var closeRequested = false;
            vm.CloseRequested += (_, _) => closeRequested = true;

            vm.ConnectCommand.Execute(null);

            Assert.IsTrue(closeRequested);
            Assert.IsNotNull(vm.Result);
            Assert.AreEqual("tcp", vm.Result.Transport);
            Assert.AreEqual("192.168.0.107", vm.Result.Host);
            Assert.AreEqual("23", vm.Result.Port);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectCommand_WithLoopbackTransport_SetsResultAndRaisesCloseRequested()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions { Transport = "serial" })
            {
                Transport = "loopback",
            };

            var closeRequested = false;
            vm.CloseRequested += (_, _) => closeRequested = true;

            vm.ConnectCommand.Execute(null);

            Assert.IsTrue(closeRequested);
            Assert.IsNotNull(vm.Result);
            Assert.AreEqual("loopback", vm.Result.Transport);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectCommand_WithInvalidFields_SetsStatusMessageAndDoesNotRaiseCloseRequested()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions { Transport = "tcp" });

            var closeRequested = false;
            vm.CloseRequested += (_, _) => closeRequested = true;

            vm.ConnectCommand.Execute(null);

            Assert.IsFalse(closeRequested);
            Assert.IsNull(vm.Result);
            Assert.Contains("--port", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveCommand_ThenLoadCommand_RoundTripsFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Transport = "tcp",
                Host = "192.168.0.108",
                TcpPort = "23",
                Parser = "decimal",
                SaveName = "tek108",
            };
            SelectOnly(vm, "ascii", "hex");

            vm.SaveCommand.Execute(null);

            Assert.Contains("Saved profile 'tek108'", vm.StatusMessage);
            Assert.Contains("tek108", vm.Profiles);
            Assert.AreEqual("tek108", vm.SaveName, "SaveName should stay put after a successful save, so Save Profile can be clicked again to update the same profile.");

            var fresh = new ConnectionEditorViewModel(store, new CliOptions { Transport = "serial" })
            {
                SelectedProfileName = "tek108",
            };

            fresh.LoadCommand.Execute(null);

            Assert.AreEqual("tcp", fresh.Transport);
            Assert.AreEqual("192.168.0.108", fresh.Host);
            Assert.AreEqual("23", fresh.TcpPort);
            Assert.AreSequenceEqual(["ascii", "hex"], [.. fresh.SelectedPresenters]);
            Assert.AreEqual("decimal", fresh.Parser);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void SaveCommand_WithAnInvalidName_SetsStatusMessageInsteadOfThrowing()
    {
        // Regression test for bug 013: SaveAsProfile() called _store.Save(name, options) with no
        // try/catch, so a name invalid on this system (e.g. an NTFS-reserved character) threw
        // straight out of the command - crashing the TUI editor's app.Run loop, or surfacing as an
        // unhandled stack-trace dialog in WPF, instead of a StatusMessage like every other command
        // here. See docs/bugs/013-save-export-no-error-handling.md.
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                Transport = "tcp",
                Host = "192.168.0.1",
                TcpPort = "23",
                SaveName = "a:b",
            };

            vm.SaveCommand.Execute(null);

            Assert.Contains("a:b", vm.StatusMessage);
            Assert.IsEmpty(vm.Profiles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void SaveCommand_WithAnUnparseableBaud_SetsStatusMessageInsteadOfSavingWithTheDefault()
    {
        // Regression test for bug 015: BuildOptions() used int.TryParse(Baud, out var baud) and only
        // assigned options.Baud when it succeeded, silently leaving the CliOptions() default (9600)
        // in place for something like "115200x" - so a mistyped baud rate saved and connected at
        // 9600 with no error. See docs/bugs/015-mistyped-numbers-silently-default.md.
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                Transport = "serial",
                Port = "COM3",
                Baud = "115200x",
                SaveName = "bench",
            };

            vm.SaveCommand.Execute(null);

            Assert.IsEmpty(vm.Profiles);
            Assert.DoesNotContain("Saved", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadCommand_WithNoSelection_SetsStatusMessage()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            vm.LoadCommand.Execute(null);

            Assert.Contains("Select a profile first", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportCommand_ThenImportCommand_RoundTripsFieldsThroughAFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "exported.json");
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                Transport = "hid",
                VendorId = "4216",
                ProductId = "63560",
                ImportExportPath = path,
            };

            vm.ExportCommand.Execute(null);

            Assert.Contains("Exported to", vm.StatusMessage);
            Assert.IsTrue(File.Exists(path));

            var fresh = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions { Transport = "serial" })
            {
                ImportExportPath = path,
            };

            fresh.ImportCommand.Execute(null);

            Assert.AreEqual("hid", fresh.Transport);
            Assert.AreEqual("4216", fresh.VendorId);
            Assert.AreEqual("63560", fresh.ProductId);
            Assert.Contains("Imported", fresh.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void ExportCommand_ToAPathInAMissingFolder_SetsStatusMessageInsteadOfThrowing()
    {
        // Regression test for bug 013: Export() called ConnectionProfileStore.ExportToFile(path,
        // options) with no try/catch, so a path in a folder that doesn't exist threw
        // DirectoryNotFoundException straight out of the command instead of a StatusMessage.
        // See docs/bugs/013-save-export-no-error-handling.md.
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "does-not-exist", "exported.json");
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                Transport = "tcp",
                Host = "192.168.0.1",
                TcpPort = "23",
                ImportExportPath = path,
            };

            vm.ExportCommand.Execute(null);

            Assert.Contains("Could not export", vm.StatusMessage);
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportSelectedProfilesCommand_ExportsOnlyTheNamedProfilesAsAZip()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Transport = "tcp",
                Host = "192.168.0.1",
                TcpPort = "23",
                SaveName = "alpha",
            };
            vm.SaveCommand.Execute(null);
            vm.SaveName = "beta";
            vm.SaveCommand.Execute(null);
            vm.SaveName = "gamma";
            vm.SaveCommand.Execute(null);

            var zipPath = Path.Combine(directory, "export.zip");
            vm.SelectedProfileNames.Add("alpha");
            vm.SelectedProfileNames.Add("beta");
            vm.ImportExportPath = zipPath;

            vm.ExportSelectedProfilesCommand.Execute(null);

            Assert.Contains("Exported 2 profile(s)", vm.StatusMessage);
            Assert.IsTrue(File.Exists(zipPath));

            var importDirectory = CreateTempDirectory();
            var importStore = new ConnectionProfileStore(importDirectory);
            importStore.ImportZip(zipPath);
            Assert.AreSequenceEqual(["alpha", "beta"], [.. importStore.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportSelectedProfilesCommand_WithNothingSelected_SetsStatusMessage()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                ImportExportPath = Path.Combine(directory, "export.zip"),
            };

            vm.ExportSelectedProfilesCommand.Execute(null);

            Assert.Contains("Select one or more saved profiles", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportAllProfilesCommand_ExportsEveryProfileRegardlessOfSelection()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Transport = "tcp",
                Host = "192.168.0.1",
                TcpPort = "23",
                SaveName = "alpha",
            };
            vm.SaveCommand.Execute(null);
            vm.SaveName = "beta";
            vm.SaveCommand.Execute(null);

            var zipPath = Path.Combine(directory, "all.zip");
            vm.ImportExportPath = zipPath;

            vm.ExportAllProfilesCommand.Execute(null);

            Assert.Contains("Exported 2 profile(s)", vm.StatusMessage);

            var importDirectory = CreateTempDirectory();
            var importStore = new ConnectionProfileStore(importDirectory);
            importStore.ImportZip(zipPath);
            Assert.AreSequenceEqual(["alpha", "beta"], [.. importStore.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportCommand_ZipPath_ImportsIntoTheStoreAndRefreshesProfilesInsteadOfLoadingFields()
    {
        var sourceDirectory = CreateTempDirectory();
        var destDirectory = CreateTempDirectory();
        try
        {
            var source = new ConnectionProfileStore(sourceDirectory);
            source.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            var zipPath = Path.Combine(sourceDirectory, "export.zip");
            source.ExportZip(zipPath, ["alpha"]);

            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(destDirectory), new CliOptions { Transport = "serial" })
            {
                ImportExportPath = zipPath,
            };

            vm.ImportCommand.Execute(null);

            Assert.Contains("Imported 1 profile(s)", vm.StatusMessage);
            Assert.Contains("alpha", vm.Profiles);
            Assert.AreEqual("serial", vm.Transport, "A zip import saves straight into the store; it shouldn't load fields the way a single-profile JSON import does.");
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(destDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportCommand_ZipPath_WithConflict_AsksResolveZipImportConflict()
    {
        var sourceDirectory = CreateTempDirectory();
        var destDirectory = CreateTempDirectory();
        try
        {
            var source = new ConnectionProfileStore(sourceDirectory);
            source.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            var zipPath = Path.Combine(sourceDirectory, "export.zip");
            source.ExportZip(zipPath, ["alpha"]);

            var destStore = new ConnectionProfileStore(destDirectory);
            destStore.Save("alpha", new CliOptions { Transport = "tcp", Host = "existing", Port = "1" });
            var vm = new ConnectionEditorViewModel(destStore, new CliOptions())
            {
                ImportExportPath = zipPath,
                ResolveZipImportConflict = _ => ZipImportConflictResolution.Skip,
            };

            vm.ImportCommand.Execute(null);

            Assert.Contains("skipped 1", vm.StatusMessage);
            Assert.AreEqual("existing", destStore.Load("alpha").Host);
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(destDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportCommand_MissingFile_SetsStatusMessageWithoutThrowing()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                ImportExportPath = Path.Combine(directory, "does-not-exist.json"),
            };

            vm.ImportCommand.Execute(null);

            Assert.Contains("Could not import", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveCommand_IncludesSerialAndDescriptionFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Transport = "serial",
                Port = "COM5",
                Baud = "4800",
                DataBits = "7",
                ParityText = "Even",
                StopBitsText = "Two",
                Description = "Test bench Rigol",
                SaveName = "bench",
            };

            vm.SaveCommand.Execute(null);

            var saved = store.Load("bench");
            Assert.AreEqual("COM5", saved.Port);
            Assert.AreEqual(4800, saved.Baud);
            Assert.AreEqual(7, saved.DataBits);
            Assert.AreEqual(System.IO.Ports.Parity.Even, saved.Parity);
            Assert.AreEqual(System.IO.Ports.StopBits.Two, saved.StopBits);
            Assert.AreEqual("Test bench Rigol", saved.Description);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveCommand_WhenNameAlreadyExists_AsksForConfirmationFirst()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("existing", new CliOptions { Transport = "tcp", Host = "1.1.1.1", Port = "1" });

            var confirmPrompts = new List<string>();
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Transport = "tcp",
                Host = "2.2.2.2",
                TcpPort = "2",
                SaveName = "existing",
                ConfirmOverwrite = name =>
                {
                    confirmPrompts.Add(name);
                    return false;
                },
            };

            vm.SaveCommand.Execute(null);

            Assert.Contains("existing", confirmPrompts);
            Assert.Contains("already exists", vm.StatusMessage);
            Assert.AreEqual("1.1.1.1", store.Load("existing").Host, "Declining the overwrite should leave the existing profile untouched.");

            vm.ConfirmOverwrite = _ => true;
            vm.SaveCommand.Execute(null);

            Assert.AreEqual("2.2.2.2", store.Load("existing").Host, "Confirming the overwrite should save the new fields.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void SaveCommand_WhenNameAlreadyExistsUnderADifferentCase_AsksForConfirmationFirst()
    {
        // Regression test for bug 014: Profiles.Contains(name) is ordinal/case-sensitive, but the
        // store and NTFS are case-insensitive (List() itself uses OrdinalIgnoreCase), so saving
        // "bench" over an existing "Bench" skipped ConfirmOverwrite entirely and silently
        // overwrote it. See docs/bugs/014-save-overwrites-different-case.md.
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("Bench", new CliOptions { Transport = "tcp", Host = "1.1.1.1", Port = "1" });

            var confirmPrompts = new List<string>();
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Transport = "tcp",
                Host = "2.2.2.2",
                TcpPort = "2",
                SaveName = "bench",
                ConfirmOverwrite = name =>
                {
                    confirmPrompts.Add(name);
                    return false;
                },
            };

            vm.SaveCommand.Execute(null);

            Assert.Contains("bench", confirmPrompts);
            Assert.AreEqual("1.1.1.1", store.Load("Bench").Host, "Declining the overwrite should leave the existing profile untouched.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadCommand_SetsSaveNameToTheLoadedProfile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23" });

            var vm = new ConnectionEditorViewModel(store, new CliOptions { Transport = "serial" }) { SelectedProfileName = "tek108" };
            vm.LoadCommand.Execute(null);

            Assert.AreEqual("tek108", vm.SaveName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteCommand_RemovesTheSelectedProfile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23" });

            var vm = new ConnectionEditorViewModel(store, new CliOptions()) { SelectedProfileName = "tek108" };
            vm.DeleteCommand.Execute(null);

            Assert.Contains("Deleted profile 'tek108'", vm.StatusMessage);
            Assert.DoesNotContain("tek108", vm.Profiles);
            Assert.DoesNotContain("tek108", store.List());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteCommand_WithNoSelection_SetsStatusMessage()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            vm.DeleteCommand.Execute(null);

            Assert.Contains("Select a profile first", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RefreshCommand_PicksUpAProfileSavedOutsideThisViewModel()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var vm = new ConnectionEditorViewModel(store, new CliOptions());

            store.Save("added-later", new CliOptions { Transport = "tcp", Host = "1.1.1.1", Port = "1" });
            Assert.DoesNotContain("added-later", vm.Profiles);

            vm.RefreshCommand.Execute(null);

            Assert.Contains("added-later", vm.Profiles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PropertyChanged_RaisedWhenAFieldChanges()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());
            var raised = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.PropertyName))
                {
                    raised.Add(e.PropertyName);
                }
            };

            vm.Transport = "tcp";

            Assert.Contains(nameof(ConnectionEditorViewModel.Transport), raised);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_IsNotDirty()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23" });

            Assert.IsFalse(vm.IsDirty, "A freshly-opened editor showing its starting configuration shouldn't already be dirty.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void EditingAField_MarksTheViewModelDirty()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                Host = "192.168.0.108"
            };

            Assert.IsTrue(vm.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingStatusMessageOrSelectedProfileName_DoesNotMarkDirty()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                StatusMessage = "some status",
                SelectedProfileName = "some-profile",
            };

            Assert.IsFalse(vm.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadCommand_ClearsDirty_EvenAfterEditingFieldsFirst()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23" });

            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Host = "something typed but never saved",
                SelectedProfileName = "tek108",
            };
            Assert.IsTrue(vm.IsDirty);

            vm.LoadCommand.Execute(null);

            Assert.IsFalse(vm.IsDirty, "Loading a profile overwrites the unsaved edits, so nothing is dirty relative to what's now shown.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveCommand_ClearsDirty()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                Transport = "tcp",
                Host = "192.168.0.108",
                TcpPort = "23",
                SaveName = "tek108",
            };
            Assert.IsTrue(vm.IsDirty);

            vm.SaveCommand.Execute(null);

            Assert.IsFalse(vm.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectCommand_WithValidFields_ClearsDirty()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                Transport = "tcp",
                Host = "192.168.0.107",
                TcpPort = "23",
            };
            Assert.IsTrue(vm.IsDirty);

            vm.ConnectCommand.Execute(null);

            Assert.IsFalse(vm.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfirmClose_WhenNotDirty_ReturnsTrueWithoutAskingConfirmation()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());
            var asked = false;
            vm.ConfirmDiscardChanges = () => { asked = true; return false; };

            Assert.IsTrue(vm.ConfirmClose());
            Assert.IsFalse(asked, "Not dirty - there's nothing to confirm discarding.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfirmClose_WhenDirty_AsksAndHonorsTheAnswer()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                Host = "192.168.0.108",
                ConfirmDiscardChanges = () => false
            };
            Assert.IsFalse(vm.ConfirmClose());

            vm.ConfirmDiscardChanges = () => true;
            Assert.IsTrue(vm.ConfirmClose());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadCommand_WhenDirty_AsksConfirmDiscardChangesFirst()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23" });

            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Host = "something typed but never saved",
                SelectedProfileName = "tek108",
                ConfirmDiscardChanges = () => false
            };
            vm.LoadCommand.Execute(null);

            Assert.AreEqual("something typed but never saved", vm.Host, "Declining should leave the unsaved edit in place, not overwrite it.");
            Assert.Contains("cancelled", vm.StatusMessage);

            vm.ConfirmDiscardChanges = () => true;
            vm.LoadCommand.Execute(null);

            Assert.AreEqual("192.168.0.108", vm.Host, "Confirming discard should let the load actually proceed.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SavingAProfileThroughAnotherStoreInstance_RaisesProfilesChangedExternally()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());
            using var raised = new ManualResetEventSlim(false);
            vm.ProfilesChangedExternally += (_, _) => raised.Set();

            // A second, independent ConnectionProfileStore instance pointed at the same directory —
            // simulating the other front end (or a user editing the folder by hand) saving a
            // profile while this editor is already open, not this same instance's own Save.
            new ConnectionProfileStore(directory).Save("added-elsewhere", new CliOptions { Transport = "tcp", Host = "1.1.1.1", Port = "1" });

            Assert.IsTrue(raised.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken), "Expected the real FileSystemWatcher to notice a profile saved by a different store instance.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Dispose_StopsRaisingProfilesChangedExternally()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());
            var raisedAfterDispose = false;
            vm.ProfilesChangedExternally += (_, _) => raisedAfterDispose = true;

            vm.Dispose();
            new ConnectionProfileStore(directory).Save("added-after-dispose", new CliOptions { Transport = "tcp", Host = "1.1.1.1", Port = "1" });

            // No good way to prove a negative deterministically against a real filesystem watcher,
            // so this gives it a real moment to (incorrectly) fire before checking - matches the
            // same real-watcher realism the test above needs, just inverted.
            Thread.Sleep(500);
            Assert.IsFalse(raisedAfterDispose, "A disposed view model's watcher should no longer be raising events.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfirmClose_WhenDirtyAndNoHookWired_ProceedsWithoutAsking()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions()) { Host = "192.168.0.108" };

            Assert.IsTrue(vm.ConfirmClose(), "Left null (e.g. in a test that doesn't wire a real dialog), a close should proceed rather than get stuck unable to ask.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeSerialPortDiscovery(
        IReadOnlyList<string> portNames,
        IReadOnlyDictionary<string, string>? descriptions = null) : ISerialPortDiscovery
    {
        public IReadOnlyList<string> GetPortNames() => portNames;

        public IReadOnlyDictionary<string, string> GetPortDescriptions() =>
            descriptions ?? new Dictionary<string, string>();
    }

    private sealed class FailingDescriptionsSerialPortDiscovery(IReadOnlyList<string> portNames) : ISerialPortDiscovery
    {
        public IReadOnlyList<string> GetPortNames() => portNames;

        public IReadOnlyDictionary<string, string> GetPortDescriptions() => throw new IOException("simulated registry failure");
    }

    private sealed class FailingSerialPortDiscovery : ISerialPortDiscovery
    {
        public IReadOnlyList<string> GetPortNames() => throw new IOException("simulated discovery failure");
    }

    private sealed class FakeHidDeviceDiscovery(IReadOnlyList<HidDeviceDescriptor> devices) : IHidDeviceDiscovery
    {
        public IReadOnlyList<HidDeviceDescriptor> GetDevices() => devices;
    }

    [TestMethod]
    public void Constructor_PopulatesSerialPortOptionsFromDiscovery()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FakeSerialPortDiscovery(["COM3", "COM7"]));

            Assert.AreSequenceEqual(["COM3", "COM7"], [.. vm.SerialPortOptions.Select(o => o.Name)]);
            Assert.AreSequenceEqual(
                ["COM3", "COM7"], [.. vm.SerialPortOptions.Select(o => o.Display)], "With no descriptions known, each port shows as just its short name.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_DecoratesPortsWithTheirDescriptions_WhenKnown()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FakeSerialPortDiscovery(
                    ["COM3", "COM7"],
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["COM3"] = "Prolific USB-to-Serial Comm Port" }));

            Assert.AreSequenceEqual(
                ["COM3 — Prolific USB-to-Serial Comm Port", "COM7"], [.. vm.SerialPortOptions.Select(o => o.Display)]);
            Assert.AreSequenceEqual(
                ["COM3", "COM7"], [.. vm.SerialPortOptions.Select(o => o.Name)], "The value that gets written into Port stays the short name.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_DoesNotListAPortThatOnlyHasADescription()
    {
        // The OS keeps records of devices long since unplugged (this machine's registry has a stale
        // Prolific COM3); descriptions must only ever decorate ports GetPortNames actually reported.
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FakeSerialPortDiscovery(
                    ["COM7"],
                    new Dictionary<string, string> { ["COM3"] = "Ghost device", ["COM7"] = "Real device" }));

            Assert.HasCount(1, vm.SerialPortOptions);
            Assert.AreEqual("COM7", vm.SerialPortOptions[0].Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_WhenDescriptionLookupThrows_FallsBackToShortNames()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FailingDescriptionsSerialPortDiscovery(["COM3", "COM7"]));

            Assert.AreSequenceEqual(["COM3", "COM7"], [.. vm.SerialPortOptions.Select(o => o.Display)]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_WhenSerialPortDiscoveryThrows_LeavesSerialPortOptionsEmptyRatherThanFailingConstruction()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FailingSerialPortDiscovery());

            Assert.IsEmpty(vm.SerialPortOptions);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_SelectsTheMatchingDetectedPort_WhenPortMatchesAKnownPort()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions { Port = "COM7" },
                serialPortDiscovery: new FakeSerialPortDiscovery(["COM3", "COM7"]));

            Assert.AreEqual("COM7", vm.SelectedSerialPort, "The detected-ports dropdown should jump to the entry matching the loaded Port.");
            Assert.AreEqual("COM7", vm.Port);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_LeavesTheDetectedPortBlank_WhenPortDoesNotMatchAnyDetectedPort()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions { Port = "COM9" },
                serialPortDiscovery: new FakeSerialPortDiscovery(["COM3", "COM7"]));

            Assert.IsNull(vm.SelectedSerialPort, "COM9 isn't attached, so the dropdown should show blank rather than a wrong selection.");
            Assert.AreEqual("COM9", vm.Port, "The typed/loaded Port itself must not be cleared just because it isn't currently detected.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadIntoFields_BlanksScpiProfile_WhenTheProfileHasNoneSaved()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                ScpiProfile = ScpiProfileCatalog.Generic.Name,
            };

            vm.LoadIntoFields(new CliOptions { ScpiProfile = null });

            Assert.AreEqual(string.Empty, vm.ScpiProfile, "No ScpiProfile saved on the profile should blank the picker, not keep whatever was there before.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadIntoFields_SelectsTheSavedScpiProfile_WhenItNamesARealCatalogProfile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            vm.LoadIntoFields(new CliOptions { ScpiProfile = ScpiProfileCatalog.All[0].Name });

            Assert.AreEqual(ScpiProfileCatalog.All[0].Name, vm.ScpiProfile);
            Assert.Contains(vm.ScpiProfile, vm.ScpiProfileOptions, "The loaded value must be one of the picker's own options for the dropdown to actually show it selected.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectedSerialPort_CopiesTheChoiceIntoPort()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FakeSerialPortDiscovery(["COM3", "COM7"]))
            {
                SelectedSerialPort = "COM7",
            };

            Assert.AreEqual("COM7", vm.Port);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectedSerialPort_MarksTheEditorDirtyViaPort_ButIsNotItselfATrackedEdit()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FakeSerialPortDiscovery(["COM3"]));

            Assert.IsFalse(vm.IsDirty);
            vm.SelectedSerialPort = "COM3";

            Assert.IsTrue(vm.IsDirty, "Picking a port changes Port, which should count as an edit.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectedDeviceNotFound_IsTrue_WhenTheSavedSerialPortIsntAmongDetectedPorts()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FakeSerialPortDiscovery(["COM3"]));

            vm.LoadIntoFields(new CliOptions { Transport = "serial", Port = "COM9" });

            Assert.IsTrue(vm.ConnectedDeviceNotFound, "COM9 isn't in the detected ports list, so the saved profile's device isn't plugged in.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectedDeviceNotFound_IsFalse_WhenTheSavedSerialPortMatchesADetectedPort()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                serialPortDiscovery: new FakeSerialPortDiscovery(["COM3"]));

            vm.LoadIntoFields(new CliOptions { Transport = "serial", Port = "COM3" });

            Assert.IsFalse(vm.ConnectedDeviceNotFound);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectedDeviceNotFound_IsTrue_WhenTheSavedHidVendorProductIdDoesNotMatchAnyDetectedDevice()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([new HidDeviceDescriptor(0x046D, 0xC08B, "Mouse", "SN123", "hid#vid_046d&pid_c08b#0&0&0000#{guid}")]));

            vm.LoadIntoFields(new CliOptions { Transport = "hid", VendorId = 0x10CF, ProductId = 0x5500 });

            Assert.IsTrue(vm.ConnectedDeviceNotFound, "No detected HID device has this VendorId/ProductId, so it isn't currently plugged in.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectedDeviceNotFound_IsFalse_WhenTheSavedHidDeviceIsFoundByBestMatch()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([new HidDeviceDescriptor(0x046D, 0xC08B, "Mouse", "SN123", "hid#vid_046d&pid_c08b#0&0&0000#{guid}")]));

            vm.LoadIntoFields(new CliOptions { Transport = "hid", VendorId = 0x046D, ProductId = 0xC08B, SerialNumber = "SN123" });

            Assert.IsFalse(vm.ConnectedDeviceNotFound);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectedDeviceNotFound_IsFalse_WhenNoVendorOrProductIdIsSpecified()
    {
        // VendorId/ProductId both 0 means "no USB identity filter" (see HasUsbIdentity) - there's
        // nothing to fail to match, so this must never read as "not found".
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([]));

            vm.LoadIntoFields(new CliOptions { Transport = "hid", VendorId = 0, ProductId = 0 });

            Assert.IsFalse(vm.ConnectedDeviceNotFound);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_PopulatesHidDeviceOptionsFromDiscovery_FormattedLikeListHidDevices()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([new HidDeviceDescriptor(0x046D, 0xC08B, "G502 HERO Gaming Mouse", "0E6A395F3531", "hid#vid_046d&pid_c08b#0&0&0000#{guid}")]));

            Assert.HasCount(1, vm.HidDeviceOptions);
            Assert.AreEqual("046D:C08B  G502 HERO Gaming Mouse", vm.HidDeviceOptions[0].Display);
            Assert.AreEqual(0x046D, vm.HidDeviceOptions[0].VendorId);
            Assert.AreEqual(0xC08B, vm.HidDeviceOptions[0].ProductId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_HidDeviceWithNoProductName_DisplaysJustTheIds()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([new HidDeviceDescriptor(0x10CF, 0x5502, null, null, "hid#vid_10cf&pid_5502#0&0&0000#{guid}")]));

            Assert.AreEqual("10CF:5502", vm.HidDeviceOptions[0].Display);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_HidDeviceWithNoSerialNumber_LeavesSerialNumberNullButKeepsDevicePath()
    {
        // A real Velleman K8055 reports no serial descriptor at all - confirmed against real
        // hardware. SerialNumber and DevicePath are genuinely separate fields (not folded together):
        // HidSharp's own matcher only ever understands a real serial descriptor, so a DevicePath
        // folded into SerialNumber could never actually be found again when opening the device for
        // real - see SystemHidDevice.Open.
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([new HidDeviceDescriptor(0x10CF, 0x5500, null, null, "hid#vid_10cf&pid_5500#7&83de718&0&0000#{guid}")]));

            Assert.IsNull(vm.HidDeviceOptions[0].SerialNumber);
            Assert.AreEqual("hid#vid_10cf&pid_5500#7&83de718&0&0000#{guid}", vm.HidDeviceOptions[0].DevicePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_HidDeviceWithBlankSerialNumber_LeavesSerialNumberNullButKeepsDevicePath()
    {
        // Some devices report an empty/whitespace serial descriptor rather than throwing or
        // returning null - that must be treated the same as a true null, not passed through as-is.
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([new HidDeviceDescriptor(0x10CF, 0x5500, null, "   ", "hid#vid_10cf&pid_5500#7&83de718&0&0000#{guid}")]));

            Assert.IsNull(vm.HidDeviceOptions[0].SerialNumber);
            Assert.AreEqual("hid#vid_10cf&pid_5500#7&83de718&0&0000#{guid}", vm.HidDeviceOptions[0].DevicePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadIntoFields_ThreeIdenticalSerialLessHidDevices_SelectsTheOneMatchingDevicePath()
    {
        // Three simultaneously-attached Velleman K8055 boards - same VID/PID, no serial descriptor
        // at all, so DevicePath is the only thing that tells them apart. Loading a profile that
        // saved the second board's DevicePath must resolve back to that exact board, not just
        // whichever one happens to be first in the detected list.
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery(
                [
                    new HidDeviceDescriptor(0x10CF, 0x5500, null, null, "hid#vid_10cf&pid_5500#1&0&0000#{guid}"),
                    new HidDeviceDescriptor(0x10CF, 0x5500, null, null, "hid#vid_10cf&pid_5500#2&0&0000#{guid}"),
                    new HidDeviceDescriptor(0x10CF, 0x5500, null, null, "hid#vid_10cf&pid_5500#3&0&0000#{guid}"),
                ]));

            vm.LoadIntoFields(new CliOptions
            {
                Transport = "hid",
                VendorId = 0x10CF,
                ProductId = 0x5500,
                DevicePath = "hid#vid_10cf&pid_5500#2&0&0000#{guid}",
            });

            Assert.AreEqual("hid#vid_10cf&pid_5500#2&0&0000#{guid}", vm.SelectedHidDevice?.DevicePath);
            Assert.IsFalse(vm.ConnectedDeviceNotFound);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly HidDeviceDescriptor[] _threeHidDevices =
    [
        new HidDeviceDescriptor(0x046D, 0xC08B, "G502 HERO Gaming Mouse", null, "hid#vid_046d&pid_c08b#0&0&0000#{guid}"),
        new HidDeviceDescriptor(0x046D, 0xC31C, "Keyboard K120", null, "hid#vid_046d&pid_c31c#0&0&0000#{guid}"),
        new HidDeviceDescriptor(0x0699, 0x0368, "TDS 2024", null, "hid#vid_0699&pid_0368#0&0&0000#{guid}"),
    ];

    private static string[] HidOptionDisplays(ConnectionEditorViewModel vm) => [.. vm.HidDeviceOptions.Select(o => o.Display)];

    [TestMethod]
    public void HidDeviceOptions_WithZeroIds_ListsEveryDetectedDevice()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices));

            Assert.HasCount(3, vm.HidDeviceOptions);
            Assert.IsFalse(vm.HidDevicesHiddenByFilter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidDeviceOptions_ANonZeroVendorId_KeepsOnlyThatVendorsDevices()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices))
            {
                VendorId = 0x046D.ToString()
            };

            Assert.AreSequenceEqual(["046D:C08B  G502 HERO Gaming Mouse", "046D:C31C  Keyboard K120"], HidOptionDisplays(vm));
            Assert.IsTrue(vm.HidDevicesHiddenByFilter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidDeviceOptions_ANonZeroProductIdAlone_FiltersByProductAcrossVendors()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices))
            {
                ProductId = 0x0368.ToString()
            };

            Assert.AreSequenceEqual(["0699:0368  TDS 2024"], HidOptionDisplays(vm));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidDeviceOptions_BothIdsNonZero_MustBothMatch()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices))
            {
                VendorId = 0x046D.ToString(),
                ProductId = 0xC31C.ToString()
            };

            Assert.AreSequenceEqual(["046D:C31C  Keyboard K120"], HidOptionDisplays(vm));

            vm.ProductId = 0x0368.ToString();

            Assert.IsEmpty(vm.HidDeviceOptions, "Vendor 046D and product 0368 matches nothing detected.");
            Assert.IsTrue(vm.HidDevicesHiddenByFilter, "An empty list caused by the filter must be distinguishable from nothing being detected.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidDeviceOptions_FollowsLaterEdits_AndClearingAnIdWidensTheListAgainInDetectionOrder()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices))
            {
                VendorId = 0x0699.ToString()
            };
            Assert.HasCount(1, vm.HidDeviceOptions);

            vm.VendorId = "0";

            Assert.AreSequenceEqual(
                ["046D:C08B  G502 HERO Gaming Mouse", "046D:C31C  Keyboard K120", "0699:0368  TDS 2024"], HidOptionDisplays(vm));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidDeviceOptions_FollowsTheHexDisplayField_LikeTheDecimalOne()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices))
            {
                IdsShowHex = true,
                VendorIdDisplay = "0699"
            };

            Assert.AreSequenceEqual(["0699:0368  TDS 2024"], HidOptionDisplays(vm));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidDeviceOptions_AnIdThatIsNotANumberYet_DoesNotFilter()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices))
            {
                VendorId = "12ab"
            };

            Assert.HasCount(3, vm.HidDeviceOptions);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidDeviceOptions_IsFilteredByTheInitialOptions_FromTheStart()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions { Transport = "hid", VendorId = 0x0699 },
                hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices));

            Assert.AreSequenceEqual(["0699:0368  TDS 2024"], HidOptionDisplays(vm));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidDeviceOptions_IsTheSameLiveCollectionAcrossFilterChanges_SoABoundListFollowsIt()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices));
            var before = vm.HidDeviceOptions;
            var changes = 0;
            ((System.Collections.Specialized.INotifyCollectionChanged)before).CollectionChanged += (_, _) => changes++;

            vm.VendorId = 0x0699.ToString();

            Assert.AreSame(before, vm.HidDeviceOptions);
            Assert.AreEqual(2, changes, "The two non-matching devices are removed in place - no wholesale reset.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectedHidDevice_FillsTheIdsAsARealEdit_AndThePickedDeviceStaysInTheFilteredList()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(_threeHidDevices));
            Assert.IsFalse(vm.IsDirty);

            vm.SelectedHidDevice = vm.HidDeviceOptions[0];

            Assert.IsTrue(vm.IsDirty, "Picking a device fills in the ids, which is a real edit...");
            Assert.AreSequenceEqual(["046D:C08B  G502 HERO Gaming Mouse"], HidOptionDisplays(vm), "...and the picked device stays in the filtered list.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectedHidDevice_CopiesVendorAndProductIdAsDecimal()
    {
        var directory = CreateTempDirectory();
        try
        {
            var device = new HidDeviceOption("046D:C08B  G502 HERO Gaming Mouse", 0x046D, 0xC08B, "SN123", "hid#vid_046d&pid_c08b#0&0&0000#{guid}");
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                SelectedHidDevice = device,
            };

            Assert.AreEqual(0x046D.ToString(), vm.VendorId);
            Assert.AreEqual(0xC08B.ToString(), vm.ProductId);
            Assert.AreEqual("SN123", vm.SerialNumber);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void VendorIdDisplay_WhenIdsShowHexIsFalse_MatchesTheCanonicalDecimalValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                VendorId = "1234",
            };

            Assert.AreEqual("1234", vm.VendorIdDisplay);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void VendorIdDisplay_WhenIdsShowHexIsTrue_FormatsAsFourDigitUppercaseHex()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                VendorId = "1234",
                IdsShowHex = true,
            };

            Assert.AreEqual("04D2", vm.VendorIdDisplay);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingVendorIdDisplay_WhenIdsShowHexIsTrue_ParsesHexIntoTheCanonicalDecimalValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                IdsShowHex = true,
                VendorIdDisplay = "04D2",
            };

            Assert.AreEqual("1234", vm.VendorId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingProductIdDisplay_WhenIdsShowHexIsTrue_ParsesHexIntoTheCanonicalDecimalValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                IdsShowHex = true,
                ProductIdDisplay = "C08B",
            };

            Assert.AreEqual(0xC08B.ToString(), vm.ProductId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TogglingIdsShowHex_ReformatsTheAlreadyDisplayedValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                VendorId = "1234",
            };

            Assert.AreEqual("1234", vm.VendorIdDisplay);

            vm.IdsShowHex = true;

            Assert.AreEqual("04D2", vm.VendorIdDisplay, "Toggling the display format should reformat the already-set canonical value, not require it to be re-entered.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void VendorIdDisplay_WithUnparseableHexInput_IsKeptAsIsRatherThanBlanked()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                IdsShowHex = true,
                VendorIdDisplay = "not hex",
            };

            Assert.AreEqual("not hex", vm.VendorId, "An unparseable value should be stored as-is (letting validation catch it later), the same 'don't reject a keystroke' behavior every other typed field already has.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingIdsShowHexOrTheDisplayProperties_DoesNotMarkTheEditorDirtyByItself()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                IdsShowHex = true
            };

            Assert.IsFalse(vm.IsDirty, "Toggling the display format is a presentation preference, not a connection-field edit.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingVendorIdDisplay_MarksTheEditorDirtyViaTheCanonicalValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                VendorIdDisplay = "1234"
            };

            Assert.IsTrue(vm.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void SelectOnly(ConnectionEditorViewModel vm, params string[] names)
    {
        foreach (var choice in vm.PresenterChoices)
        {
            choice.IsSelected = names.Contains(choice.Name);
        }
    }

    [TestMethod]
    public void PresenterChoices_OneEntryPerPresenterOption_InOrder()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            Assert.AreSequenceEqual([.. vm.PresenterOptions], [.. vm.PresenterChoices.Select(c => c.Name)]);
            Assert.AreSequenceEqual(["hex"], [.. vm.SelectedPresenters], "A default CliOptions displays as hex.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void BuildOptions_WithSeveralPresentersChecked_ListsThemInPickerOrder()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());
            SelectOnly(vm, "binary", "ascii");
            vm.Parser = "hex";

            var options = vm.BuildOptions();

            Assert.AreSequenceEqual(["ascii", "binary"], options.Presenter);
            Assert.AreEqual("hex", options.Parser, "The send format is independent of which presenters display.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TogglingAPresenterCheckbox_MarksTheEditorDirty()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());
            Assert.IsFalse(vm.IsDirty);

            vm.PresenterChoices.Single(c => c.Name == "ascii").IsSelected = true;

            Assert.IsTrue(vm.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectCommand_WithNoPresenterChecked_ReportsAndDoesNotConnect()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" });
            SelectOnly(vm);

            vm.ConnectCommand.Execute(null);

            Assert.Contains("at least one presenter", vm.StatusMessage);
            Assert.IsNull(vm.Result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [TestMethod]
    public void DeleteSelectedProfilesCommand_DeletesOnlyTheSelectedProfilesAndClearsTheSelection()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            foreach (var name in new[] { "alpha", "beta", "gamma" })
            {
                store.Save(name, new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            }

            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                SelectedProfileName = "alpha"
            };
            vm.SelectedProfileNames.Add("alpha");
            vm.SelectedProfileNames.Add("gamma");

            vm.DeleteSelectedProfilesCommand.Execute(null);

            Assert.Contains("Deleted 2 profile(s).", vm.StatusMessage);
            Assert.AreSequenceEqual(["beta"], [.. store.List()]);
            Assert.AreSequenceEqual(["beta"], [.. vm.Profiles], "The list refreshes itself.");
            Assert.IsEmpty(vm.SelectedProfileNames);
            Assert.IsNull(vm.SelectedProfileName, "The single-selection was one of the deleted profiles.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteSelectedProfilesCommand_WithNothingSelected_SetsStatusMessage()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            var vm = new ConnectionEditorViewModel(store, new CliOptions());

            vm.DeleteSelectedProfilesCommand.Execute(null);

            Assert.Contains("Select one or more saved profiles to delete", vm.StatusMessage);
            Assert.AreSequenceEqual(["alpha"], [.. store.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteSelectedProfilesCommand_WhenTheUserDeclines_DeletesNothing()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            store.Save("beta", new CliOptions { Transport = "tcp", Host = "192.168.0.2", Port = "23" });
            IReadOnlyList<string>? asked = null;
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                ConfirmDeleteProfiles = names =>
                {
                    asked = names;
                    return false;
                },
            };
            vm.SelectedProfileNames.Add("alpha");
            vm.SelectedProfileNames.Add("beta");

            vm.DeleteSelectedProfilesCommand.Execute(null);

            Assert.AreSequenceEqual(["alpha", "beta"], [.. asked!], "The confirmation is told exactly which profiles are about to go.");
            Assert.AreEqual("Delete cancelled.", vm.StatusMessage);
            Assert.AreSequenceEqual(["alpha", "beta"], [.. store.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteSelectedProfilesCommand_ReportsAProfileThatAlreadyVanished()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            var vm = new ConnectionEditorViewModel(store, new CliOptions());
            vm.SelectedProfileNames.Add("alpha");
            vm.SelectedProfileNames.Add("ghost"); // e.g. removed by another process since the list was drawn

            vm.DeleteSelectedProfilesCommand.Execute(null);

            Assert.AreEqual("Deleted 1 profile(s). 1 not found.", vm.StatusMessage);
            Assert.IsEmpty(store.List());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
    public void ReplaceAllFromZipCommand_DeletesEverythingThenImportsTheZip_AfterConfirming()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles"));
            store.Save("old-one", new CliOptions { Transport = "tcp", Host = "10.0.0.1", Port = "23" });
            store.Save("old-two", new CliOptions { Transport = "tcp", Host = "10.0.0.2", Port = "23" });
            var zip = WriteZip(directory, ("new-one.json", "{ \"Transport\": \"tcp\", \"Host\": \"192.168.0.1\", \"Port\": 23 }"));
            (int Existing, int Incoming)? asked = null;
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                ImportExportPath = zip,
                ConfirmReplaceAllProfiles = (existing, incoming) =>
                {
                    asked = (existing, incoming);
                    return true;
                },
                SelectedProfileName = "old-one"
            };
            vm.SelectedProfileNames.Add("old-two");

            vm.ReplaceAllFromZipCommand.Execute(null);

            Assert.AreEqual((2, 1), asked, "The confirmation is told how many profiles will go and how many come in.");
            Assert.AreSequenceEqual(["new-one"], [.. store.List()]);
            Assert.AreSequenceEqual(["new-one"], [.. vm.Profiles]);
            Assert.IsNull(vm.SelectedProfileName);
            Assert.IsEmpty(vm.SelectedProfileNames);
            Assert.Contains("Replaced 2 saved profile(s) with 1", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceAllFromZipCommand_WhenTheUserDeclines_DeletesNothing()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles"));
            store.Save("old-one", new CliOptions { Transport = "tcp", Host = "10.0.0.1", Port = "23" });
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                ImportExportPath = WriteZip(directory, ("new-one.json", "{}")),
                ConfirmReplaceAllProfiles = (_, _) => false,
            };

            vm.ReplaceAllFromZipCommand.Execute(null);

            Assert.AreEqual("Replace All cancelled.", vm.StatusMessage);
            Assert.AreSequenceEqual(["old-one"], [.. store.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceAllFromZipCommand_WithAnUnreadableZip_DeletesNothing()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles"));
            store.Save("old-one", new CliOptions { Transport = "tcp", Host = "10.0.0.1", Port = "23" });
            var asked = false;
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                ImportExportPath = WriteZip(directory, ("good.json", "{}"), ("broken.json", "{ nope")),
                ConfirmReplaceAllProfiles = (_, _) =>
                {
                    asked = true;
                    return true;
                },
            };

            vm.ReplaceAllFromZipCommand.Execute(null);

            Assert.Contains("broken.json", vm.StatusMessage);
            Assert.Contains("Nothing was deleted", vm.StatusMessage);
            Assert.IsFalse(asked, "A bad archive is rejected before the user is even asked.");
            Assert.AreSequenceEqual(["old-one"], [.. store.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceAllFromZipCommand_WithAZipHoldingNoProfiles_DeletesNothing()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles"));
            store.Save("old-one", new CliOptions { Transport = "tcp", Host = "10.0.0.1", Port = "23" });
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                ImportExportPath = WriteZip(directory, ("readme.txt", "not a profile")),
            };

            vm.ReplaceAllFromZipCommand.Execute(null);

            Assert.Contains("contains no profiles", vm.StatusMessage);
            Assert.AreSequenceEqual(["old-one"], [.. store.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceAllFromZipCommand_WithNothingSavedYet_ImportsWithoutAsking()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles"));
            var asked = false;
            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                ImportExportPath = WriteZip(directory, ("new-one.json", "{}")),
                ConfirmReplaceAllProfiles = (_, _) =>
                {
                    asked = true;
                    return false;
                },
            };

            vm.ReplaceAllFromZipCommand.Execute(null);

            Assert.IsFalse(asked, "There's nothing to lose, so there's nothing to confirm.");
            Assert.AreSequenceEqual(["new-one"], [.. store.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceAllFromZipCommand_WithANonZipOrEmptyPath_SetsStatusMessage()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles"));
            store.Save("old-one", new CliOptions { Transport = "tcp", Host = "10.0.0.1", Port = "23" });
            var vm = new ConnectionEditorViewModel(store, new CliOptions());

            vm.ReplaceAllFromZipCommand.Execute(null);
            Assert.Contains("Type a file path", vm.StatusMessage);

            vm.ImportExportPath = Path.Combine(directory, "single.json");
            vm.ReplaceAllFromZipCommand.Execute(null);
            Assert.Contains("needs a .zip file", vm.StatusMessage);

            Assert.AreSequenceEqual(["old-one"], [.. store.List()]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public required TestContext TestContext { get; set; }
}
