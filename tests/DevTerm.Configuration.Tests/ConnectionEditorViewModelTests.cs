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
}
