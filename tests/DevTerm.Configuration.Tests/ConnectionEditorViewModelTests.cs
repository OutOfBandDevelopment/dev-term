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
[TestCategory("UNIT")]
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
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Presenter = "ascii" };
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), initial, "some error");

            Assert.AreEqual("tcp", vm.Transport);
            Assert.AreEqual("192.168.0.107", vm.Host);
            Assert.AreEqual("23", vm.TcpPort);
            Assert.AreEqual("ascii", vm.Presenter);
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
            Assert.AreEqual(23, vm.Result.TcpPort);
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
            StringAssert.Contains(vm.StatusMessage, "tcpport");
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
                Presenter = "ascii",
                SaveName = "tek108",
            };

            vm.SaveCommand.Execute(null);

            StringAssert.Contains(vm.StatusMessage, "Saved profile 'tek108'");
            Assert.Contains("tek108", vm.Profiles);
            Assert.AreEqual(string.Empty, vm.SaveName, "SaveName should clear after a successful save.");

            var fresh = new ConnectionEditorViewModel(store, new CliOptions { Transport = "serial" })
            {
                SelectedProfileName = "tek108",
            };

            fresh.LoadCommand.Execute(null);

            Assert.AreEqual("tcp", fresh.Transport);
            Assert.AreEqual("192.168.0.108", fresh.Host);
            Assert.AreEqual("23", fresh.TcpPort);
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

            StringAssert.Contains(vm.StatusMessage, "Select a profile first");
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
                HidVendorId = "4216",
                HidProductId = "63560",
                Presenter = "hex",
                ImportExportPath = path,
            };

            vm.ExportCommand.Execute(null);

            StringAssert.Contains(vm.StatusMessage, "Exported to");
            Assert.IsTrue(File.Exists(path));

            var fresh = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions { Transport = "serial" })
            {
                ImportExportPath = path,
            };

            fresh.ImportCommand.Execute(null);

            Assert.AreEqual("hid", fresh.Transport);
            Assert.AreEqual("4216", fresh.HidVendorId);
            Assert.AreEqual("63560", fresh.HidProductId);
            StringAssert.Contains(fresh.StatusMessage, "Imported");
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

            StringAssert.Contains(vm.StatusMessage, "Exported 2 profile(s)");
            Assert.IsTrue(File.Exists(zipPath));

            var importDirectory = CreateTempDirectory();
            var importStore = new ConnectionProfileStore(importDirectory);
            importStore.ImportZip(zipPath);
            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, importStore.List().ToArray());
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

            StringAssert.Contains(vm.StatusMessage, "Select one or more saved profiles");
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

            StringAssert.Contains(vm.StatusMessage, "Exported 2 profile(s)");

            var importDirectory = CreateTempDirectory();
            var importStore = new ConnectionProfileStore(importDirectory);
            importStore.ImportZip(zipPath);
            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, importStore.List().ToArray());
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
            source.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", TcpPort = 23 });
            var zipPath = Path.Combine(sourceDirectory, "export.zip");
            source.ExportZip(zipPath, ["alpha"]);

            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(destDirectory), new CliOptions { Transport = "serial" })
            {
                ImportExportPath = zipPath,
            };

            vm.ImportCommand.Execute(null);

            StringAssert.Contains(vm.StatusMessage, "Imported 1 profile(s)");
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
            source.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", TcpPort = 23 });
            var zipPath = Path.Combine(sourceDirectory, "export.zip");
            source.ExportZip(zipPath, ["alpha"]);

            var destStore = new ConnectionProfileStore(destDirectory);
            destStore.Save("alpha", new CliOptions { Transport = "tcp", Host = "existing", TcpPort = 1 });
            var vm = new ConnectionEditorViewModel(destStore, new CliOptions())
            {
                ImportExportPath = zipPath,
                ResolveZipImportConflict = _ => ZipImportConflictResolution.Skip,
            };

            vm.ImportCommand.Execute(null);

            StringAssert.Contains(vm.StatusMessage, "skipped 1");
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

            StringAssert.Contains(vm.StatusMessage, "Could not import");
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
            store.Save("existing", new CliOptions { Transport = "tcp", Host = "1.1.1.1", TcpPort = 1 });

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
            StringAssert.Contains(vm.StatusMessage, "already exists");
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
    public void LoadCommand_SetsSaveNameToTheLoadedProfile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", TcpPort = 23 });

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
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", TcpPort = 23 });

            var vm = new ConnectionEditorViewModel(store, new CliOptions()) { SelectedProfileName = "tek108" };
            vm.DeleteCommand.Execute(null);

            StringAssert.Contains(vm.StatusMessage, "Deleted profile 'tek108'");
            Assert.DoesNotContain("tek108", vm.Profiles);
            Assert.IsFalse(store.List().Contains("tek108"));
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

            StringAssert.Contains(vm.StatusMessage, "Select a profile first");
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

            store.Save("added-later", new CliOptions { Transport = "tcp", Host = "1.1.1.1", TcpPort = 1 });
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
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23 });

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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            vm.Host = "192.168.0.108";

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
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", TcpPort = 23 });

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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions()) { Host = "192.168.0.108" };

            vm.ConfirmDiscardChanges = () => false;
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
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", TcpPort = 23 });

            var vm = new ConnectionEditorViewModel(store, new CliOptions())
            {
                Host = "something typed but never saved",
                SelectedProfileName = "tek108",
            };

            vm.ConfirmDiscardChanges = () => false;
            vm.LoadCommand.Execute(null);

            Assert.AreEqual("something typed but never saved", vm.Host, "Declining should leave the unsaved edit in place, not overwrite it.");
            StringAssert.Contains(vm.StatusMessage, "cancelled");

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
            new ConnectionProfileStore(directory).Save("added-elsewhere", new CliOptions { Transport = "tcp", Host = "1.1.1.1", TcpPort = 1 });

            Assert.IsTrue(raised.Wait(TimeSpan.FromSeconds(5)), "Expected the real FileSystemWatcher to notice a profile saved by a different store instance.");
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
            new ConnectionProfileStore(directory).Save("added-after-dispose", new CliOptions { Transport = "tcp", Host = "1.1.1.1", TcpPort = 1 });

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

    private sealed class FakeSerialPortDiscovery(IReadOnlyList<string> portNames) : ISerialPortDiscovery
    {
        public IReadOnlyList<string> GetPortNames() => portNames;
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

            CollectionAssert.AreEqual(new[] { "COM3", "COM7" }, vm.SerialPortOptions.ToArray());
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
    public void Constructor_PopulatesHidDeviceOptionsFromDiscovery_FormattedLikeListHidDevices()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(
                new ConnectionProfileStore(directory),
                new CliOptions(),
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([new HidDeviceDescriptor(0x046D, 0xC08B, "G502 HERO Gaming Mouse", "0E6A395F3531")]));

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
                hidDeviceDiscovery: new FakeHidDeviceDiscovery([new HidDeviceDescriptor(0x10CF, 0x5502, null, null)]));

            Assert.AreEqual("10CF:5502", vm.HidDeviceOptions[0].Display);
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
            var device = new HidDeviceOption("046D:C08B  G502 HERO Gaming Mouse", 0x046D, 0xC08B);
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                SelectedHidDevice = device,
            };

            Assert.AreEqual(0x046D.ToString(), vm.HidVendorId);
            Assert.AreEqual(0xC08B.ToString(), vm.HidProductId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidVendorIdDisplay_WhenHidIdsShowHexIsFalse_MatchesTheCanonicalDecimalValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                HidVendorId = "1234",
            };

            Assert.AreEqual("1234", vm.HidVendorIdDisplay);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidVendorIdDisplay_WhenHidIdsShowHexIsTrue_FormatsAsFourDigitUppercaseHex()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                HidVendorId = "1234",
                HidIdsShowHex = true,
            };

            Assert.AreEqual("04D2", vm.HidVendorIdDisplay);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingHidVendorIdDisplay_WhenHidIdsShowHexIsTrue_ParsesHexIntoTheCanonicalDecimalValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                HidIdsShowHex = true,
                HidVendorIdDisplay = "04D2",
            };

            Assert.AreEqual("1234", vm.HidVendorId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingHidProductIdDisplay_WhenHidIdsShowHexIsTrue_ParsesHexIntoTheCanonicalDecimalValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                HidIdsShowHex = true,
                HidProductIdDisplay = "C08B",
            };

            Assert.AreEqual(0xC08B.ToString(), vm.HidProductId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TogglingHidIdsShowHex_ReformatsTheAlreadyDisplayedValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                HidVendorId = "1234",
            };

            Assert.AreEqual("1234", vm.HidVendorIdDisplay);

            vm.HidIdsShowHex = true;

            Assert.AreEqual("04D2", vm.HidVendorIdDisplay, "Toggling the display format should reformat the already-set canonical value, not require it to be re-entered.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void HidVendorIdDisplay_WithUnparseableHexInput_IsKeptAsIsRatherThanBlanked()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions())
            {
                HidIdsShowHex = true,
                HidVendorIdDisplay = "not hex",
            };

            Assert.AreEqual("not hex", vm.HidVendorId, "An unparseable value should be stored as-is (letting validation catch it later), the same 'don't reject a keystroke' behavior every other typed field already has.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingHidIdsShowHexOrTheDisplayProperties_DoesNotMarkTheEditorDirtyByItself()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            vm.HidIdsShowHex = true;

            Assert.IsFalse(vm.IsDirty, "Toggling the display format is a presentation preference, not a connection-field edit.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingHidVendorIdDisplay_MarksTheEditorDirtyViaTheCanonicalValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            vm.HidVendorIdDisplay = "1234";

            Assert.IsTrue(vm.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
