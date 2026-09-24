using System.IO.Compression;
using DevTerm.Devices.Scpi;
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
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] };
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), initial, "some error");

            Assert.AreEqual("tcp", vm.Transport);
            Assert.AreEqual("192.168.0.107", vm.Host);
            Assert.AreEqual("23", vm.TcpPort);
            CollectionAssert.AreEqual(new[] { "ascii" }, vm.SelectedPresenters.ToArray());
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
            StringAssert.Contains(vm.StatusMessage, "--port");
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

            StringAssert.Contains(vm.StatusMessage, "Saved profile 'tek108'");
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
            CollectionAssert.AreEqual(new[] { "ascii", "hex" }, fresh.SelectedPresenters.ToArray());
            Assert.AreEqual("decimal", fresh.Parser);
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
                VendorId = "4216",
                ProductId = "63560",
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
            Assert.AreEqual("4216", fresh.VendorId);
            Assert.AreEqual("63560", fresh.ProductId);
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
            source.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
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
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23" });

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
            new ConnectionProfileStore(directory).Save("added-elsewhere", new CliOptions { Transport = "tcp", Host = "1.1.1.1", Port = "1" });

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

            CollectionAssert.AreEqual(new[] { "COM3", "COM7" }, vm.SerialPortOptions.Select(o => o.Name).ToArray());
            CollectionAssert.AreEqual(
                new[] { "COM3", "COM7" },
                vm.SerialPortOptions.Select(o => o.Display).ToArray(),
                "With no descriptions known, each port shows as just its short name.");
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

            CollectionAssert.AreEqual(
                new[] { "COM3 — Prolific USB-to-Serial Comm Port", "COM7" },
                vm.SerialPortOptions.Select(o => o.Display).ToArray());
            CollectionAssert.AreEqual(
                new[] { "COM3", "COM7" },
                vm.SerialPortOptions.Select(o => o.Name).ToArray(),
                "The value that gets written into Port stays the short name.");
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

            CollectionAssert.AreEqual(new[] { "COM3", "COM7" }, vm.SerialPortOptions.Select(o => o.Display).ToArray());
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

    private static readonly HidDeviceDescriptor[] ThreeHidDevices =
    [
        new HidDeviceDescriptor(0x046D, 0xC08B, "G502 HERO Gaming Mouse", null),
        new HidDeviceDescriptor(0x046D, 0xC31C, "Keyboard K120", null),
        new HidDeviceDescriptor(0x0699, 0x0368, "TDS 2024", null),
    ];

    private static string[] HidOptionDisplays(ConnectionEditorViewModel vm) => [.. vm.HidDeviceOptions.Select(o => o.Display)];

    [TestMethod]
    public void HidDeviceOptions_WithZeroIds_ListsEveryDetectedDevice()
    {
        var directory = CreateTempDirectory();
        try
        {
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));

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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));
            vm.VendorId = 0x046D.ToString();

            CollectionAssert.AreEqual(new[] { "046D:C08B  G502 HERO Gaming Mouse", "046D:C31C  Keyboard K120" }, HidOptionDisplays(vm));
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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));
            vm.ProductId = 0x0368.ToString();

            CollectionAssert.AreEqual(new[] { "0699:0368  TDS 2024" }, HidOptionDisplays(vm));
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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));
            vm.VendorId = 0x046D.ToString();
            vm.ProductId = 0xC31C.ToString();

            CollectionAssert.AreEqual(new[] { "046D:C31C  Keyboard K120" }, HidOptionDisplays(vm));

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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));
            vm.VendorId = 0x0699.ToString();
            Assert.HasCount(1, vm.HidDeviceOptions);

            vm.VendorId = "0";

            CollectionAssert.AreEqual(
                new[] { "046D:C08B  G502 HERO Gaming Mouse", "046D:C31C  Keyboard K120", "0699:0368  TDS 2024" },
                HidOptionDisplays(vm));
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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));
            vm.IdsShowHex = true;
            vm.VendorIdDisplay = "0699";

            CollectionAssert.AreEqual(new[] { "0699:0368  TDS 2024" }, HidOptionDisplays(vm));
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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));
            vm.VendorId = "12ab";

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
                hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));

            CollectionAssert.AreEqual(new[] { "0699:0368  TDS 2024" }, HidOptionDisplays(vm));
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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));
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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions(), hidDeviceDiscovery: new FakeHidDeviceDiscovery(ThreeHidDevices));
            Assert.IsFalse(vm.IsDirty);

            vm.SelectedHidDevice = vm.HidDeviceOptions[0];

            Assert.IsTrue(vm.IsDirty, "Picking a device fills in the ids, which is a real edit...");
            CollectionAssert.AreEqual(new[] { "046D:C08B  G502 HERO Gaming Mouse" }, HidOptionDisplays(vm), "...and the picked device stays in the filtered list.");
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
            var device = new HidDeviceOption("046D:C08B  G502 HERO Gaming Mouse", 0x046D, 0xC08B, "SN123");
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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            vm.IdsShowHex = true;

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
            var vm = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), new CliOptions());

            vm.VendorIdDisplay = "1234";

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

            CollectionAssert.AreEqual(vm.PresenterOptions.ToArray(), vm.PresenterChoices.Select(c => c.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "hex" }, vm.SelectedPresenters.ToArray(), "A default CliOptions displays as hex.");
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

            CollectionAssert.AreEqual(new[] { "ascii", "binary" }, options.Presenter);
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

            StringAssert.Contains(vm.StatusMessage, "at least one presenter");
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

            var vm = new ConnectionEditorViewModel(store, new CliOptions());
            vm.SelectedProfileName = "alpha";
            vm.SelectedProfileNames.Add("alpha");
            vm.SelectedProfileNames.Add("gamma");

            vm.DeleteSelectedProfilesCommand.Execute(null);

            StringAssert.Contains(vm.StatusMessage, "Deleted 2 profile(s).");
            CollectionAssert.AreEqual(new[] { "beta" }, store.List().ToArray());
            CollectionAssert.AreEqual(new[] { "beta" }, vm.Profiles.ToArray(), "The list refreshes itself.");
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

            StringAssert.Contains(vm.StatusMessage, "Select one or more saved profiles to delete");
            CollectionAssert.AreEqual(new[] { "alpha" }, store.List().ToArray());
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

            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, asked!.ToArray(), "The confirmation is told exactly which profiles are about to go.");
            Assert.AreEqual("Delete cancelled.", vm.StatusMessage);
            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, store.List().ToArray());
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
            };
            vm.SelectedProfileName = "old-one";
            vm.SelectedProfileNames.Add("old-two");

            vm.ReplaceAllFromZipCommand.Execute(null);

            Assert.AreEqual((2, 1), asked, "The confirmation is told how many profiles will go and how many come in.");
            CollectionAssert.AreEqual(new[] { "new-one" }, store.List().ToArray());
            CollectionAssert.AreEqual(new[] { "new-one" }, vm.Profiles.ToArray());
            Assert.IsNull(vm.SelectedProfileName);
            Assert.IsEmpty(vm.SelectedProfileNames);
            StringAssert.Contains(vm.StatusMessage, "Replaced 2 saved profile(s) with 1");
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
            CollectionAssert.AreEqual(new[] { "old-one" }, store.List().ToArray());
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

            StringAssert.Contains(vm.StatusMessage, "broken.json");
            StringAssert.Contains(vm.StatusMessage, "Nothing was deleted");
            Assert.IsFalse(asked, "A bad archive is rejected before the user is even asked.");
            CollectionAssert.AreEqual(new[] { "old-one" }, store.List().ToArray());
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

            StringAssert.Contains(vm.StatusMessage, "contains no profiles");
            CollectionAssert.AreEqual(new[] { "old-one" }, store.List().ToArray());
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
            CollectionAssert.AreEqual(new[] { "new-one" }, store.List().ToArray());
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
            StringAssert.Contains(vm.StatusMessage, "Type a file path");

            vm.ImportExportPath = Path.Combine(directory, "single.json");
            vm.ReplaceAllFromZipCommand.Execute(null);
            StringAssert.Contains(vm.StatusMessage, "needs a .zip file");

            CollectionAssert.AreEqual(new[] { "old-one" }, store.List().ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
