using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DevTerm.Configuration;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives a real <see cref="DeviceProfilesWindow"/> — real XAML, real controls, real
/// <c>{Binding ...}</c>/<c>Command="{Binding ...}"</c> wiring, no code-behind business logic to
/// test around. Most of the actual logic (validate/connect/load/save/import/export) already has
/// direct, front-end-agnostic coverage in <c>DevTerm.Configuration.Tests.ConnectionEditorViewModelTests</c>
/// — what's specific to test here is that the XAML bindings are wired correctly at all: a typo in a
/// <c>Binding</c> path fails silently at runtime (a debug-output warning, not an exception), so
/// this checks real control state reflects the view model and vice versa, not just that the view
/// model's own logic works in isolation.
///
/// Never calls <see cref="Window.Show"/>, same reasoning as <c>MainWindowTests</c> — not relevant
/// here (this window doesn't auto-connect on <c>Loaded</c>), but consistent with the rest of this
/// assembly.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class DeviceProfilesWindowTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-profiles-window-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    [TestMethod]
    public void Constructor_BindsInitialOptionsIntoRealControls()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23 };
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), initial) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                Assert.AreEqual("tcp", window.TransportBox.Text);
                Assert.AreEqual("192.168.0.107", window.HostBox.Text);
                Assert.AreEqual("23", window.TcpPortBox.Text);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void EditingATextBox_UpdatesTheViewModel_ThroughTheRealBinding()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.TransportBox.Text = "hid";

                Assert.AreEqual("hid", window.ViewModel.Transport);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ChangingTheViewModel_UpdatesTheRealTextBox_ThroughTheRealBinding()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.ViewModel.Host = "192.168.0.108";

                Assert.AreEqual("192.168.0.108", window.HostBox.Text);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectingATransport_TogglesWhichFieldGroupIsVisible()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions { Transport = "serial" }) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                Assert.AreEqual(Visibility.Visible, window.SerialPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.TcpPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.UsbDevicePanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.LoopbackPanel.Visibility);

                window.ViewModel.Transport = "tcp";

                Assert.AreEqual(Visibility.Collapsed, window.SerialPanel.Visibility);
                Assert.AreEqual(Visibility.Visible, window.TcpPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.UsbDevicePanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.LoopbackPanel.Visibility);

                window.ViewModel.Transport = "hid";

                Assert.AreEqual(Visibility.Collapsed, window.SerialPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.TcpPanel.Visibility);
                Assert.AreEqual(Visibility.Visible, window.UsbDevicePanel.Visibility);
                Assert.AreEqual(Visibility.Visible, window.DetectedHidDevicesRow.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.DetectedUsbtmcDevicesRow.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.LoopbackPanel.Visibility);

                window.ViewModel.Transport = "usbtmc";

                Assert.AreEqual(Visibility.Collapsed, window.SerialPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.TcpPanel.Visibility);
                Assert.AreEqual(Visibility.Visible, window.UsbDevicePanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.DetectedHidDevicesRow.Visibility);
                Assert.AreEqual(Visibility.Visible, window.DetectedUsbtmcDevicesRow.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.LoopbackPanel.Visibility);

                window.ViewModel.Transport = "loopback";

                Assert.AreEqual(Visibility.Collapsed, window.SerialPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.TcpPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.UsbDevicePanel.Visibility);
                Assert.AreEqual(Visibility.Visible, window.LoopbackPanel.Visibility);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteCommand_RemovesTheSelectedProfileFromTheList()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", TcpPort = 23 });

            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(store, new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.ProfilesList.SelectedItem = "tek108";
                window.ViewModel.DeleteCommand.Execute(null);

                StringAssert.Contains(window.ViewModel.StatusMessage, "Deleted profile 'tek108'");
                Assert.IsFalse(store.List().Contains("tek108"));

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Sends a real left-button MouseDown with the given click count through a row's routed-event
    /// path, the way the input system does. Needed because the previous version of the
    /// double-click feature (a <c>MouseBinding</c> on the ListBox) passed a test that only checked
    /// the binding's <c>Command</c> and then never fired for a real double-click: ListBoxItem marks
    /// the mouse-down handled, so the ListBox's own InputBindings never see it. The window must be
    /// shown (off-screen) for the ListBox to have generated its row containers.
    /// </summary>
    private static void ClickRow(DeviceProfilesWindow window, int index, int clickCount)
    {
        var item = (System.Windows.Controls.ListBoxItem)window.ProfilesList.ItemContainerGenerator.ContainerFromIndex(index);
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseDownEvent,
            Source = item,
        };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, clickCount);
        item.RaiseEvent(args);
    }

    private static DeviceProfilesWindow ShowOffScreen(ConnectionProfileStore store)
    {
        // This window doesn't connect on Loaded (unlike MainWindow), so showing it is safe here.
        var window = new DeviceProfilesWindow(store, new CliOptions())
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        window.Show();
        StaTestRunner.DoEvents();
        window.UpdateLayout();
        return window;
    }

    [TestMethod]
    public void DoubleClickingAProfile_LoadsItSameAsSelectingThenPressingLoad()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", TcpPort = 23 });

            StaTestRunner.Run(async () =>
            {
                var window = ShowOffScreen(store);

                ClickRow(window, 0, clickCount: 1);
                Assert.AreEqual("tek108", window.ViewModel.SelectedProfileName);
                Assert.AreEqual(string.Empty, window.HostBox.Text, "A single click only selects.");

                ClickRow(window, 0, clickCount: 2);

                StringAssert.Contains(window.ViewModel.StatusMessage, "Loaded profile 'tek108'");
                Assert.AreEqual("192.168.0.108", window.HostBox.Text);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DoubleClickingAProfile_WithSeveralSelected_LoadsTheOneThatWasClicked()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("alpha", new CliOptions { Transport = "tcp", Host = "10.0.0.1", TcpPort = 23 });
            store.Save("beta", new CliOptions { Transport = "tcp", Host = "10.0.0.2", TcpPort = 23 });

            StaTestRunner.Run(async () =>
            {
                var window = ShowOffScreen(store);

                // An Extended multi-selection (as for Export Selected) covering both rows...
                window.ProfilesList.SelectedItems.Add("alpha");
                window.ProfilesList.SelectedItems.Add("beta");
                StaTestRunner.DoEvents();

                // ...then double-clicking the second row must load that one, not whichever the
                // ListBox happens to report as its primary SelectedItem.
                ClickRow(window, 1, clickCount: 2);

                StringAssert.Contains(window.ViewModel.StatusMessage, "Loaded profile 'beta'");
                Assert.AreEqual("10.0.0.2", window.HostBox.Text);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Closing_WhenDirty_AsksConfirmDiscardChangesAndHonorsTheAnswer()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                var closed = false;
                window.Closed += (_, _) => closed = true;

                var asked = 0;
                window.ViewModel.ConfirmDiscardChanges = () => { asked++; return false; };
                window.ViewModel.Host = "192.168.0.108";

                window.Close();

                Assert.AreEqual(1, asked, "Editing a field first should have made the view model dirty, so closing should ask before discarding it.");
                Assert.IsFalse(closed, "Declining the confirmation should cancel the close.");

                window.ViewModel.ConfirmDiscardChanges = () => true;
                window.Close();

                Assert.IsTrue(closed, "Confirming discard should let the close proceed.");

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DetectedPortsComboBox_IsBoundToSerialPortOptionsAndSelectedSerialPort()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                Assert.AreSame(window.ViewModel.SerialPortOptions, window.DetectedPortsBox.ItemsSource);

                Assert.AreEqual("Display", window.DetectedPortsBox.DisplayMemberPath,
                    "The picker shows each port's description-bearing Display text...");
                Assert.AreEqual("Name", window.DetectedPortsBox.SelectedValuePath,
                    "...while the value it binds to SelectedSerialPort (and so Port) is the short Name.");

                window.ViewModel.SelectedSerialPort = "COM99";

                Assert.AreEqual("COM99", window.PortBox.Text,
                    "Picking a port through SelectedSerialPort should flow into Port and then the real, bound Port TextBox.");

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DetectedHidDevicesComboBox_IsBoundToHidDeviceOptionsAndSelectedHidDevice()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions { Transport = "hid" }) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                Assert.AreSame(window.ViewModel.HidDeviceOptions, window.DetectedHidDevicesBox.ItemsSource);

                window.ViewModel.SelectedHidDevice = new HidDeviceOption("046D:C08B  G502 HERO Gaming Mouse", 0x046D, 0xC08B);

                Assert.AreEqual(0x046D.ToString(), window.VendorBox.Text);
                Assert.AreEqual(0xC08B.ToString(), window.ProductBox.Text);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeHidDiscovery(params Transports.Hid.HidDeviceDescriptor[] devices) : Transports.Hid.IHidDeviceDiscovery
    {
        public IReadOnlyList<Transports.Hid.HidDeviceDescriptor> GetDevices() => devices;
    }

    [TestMethod]
    public void DetectedHidDevicesComboBox_FollowsTheVendorAndProductIdFilterLive_KeepingASurvivingSelection()
    {
        // The real window's discovery is the machine's actual HID devices, so this binds a plain
        // ComboBox the same way the XAML does (ItemsSource + SelectedItem) to a view model with a
        // fake discovery: what's being checked is that WPF follows the view model's in-place list.
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var viewModel = new ConnectionEditorViewModel(
                    new ConnectionProfileStore(directory),
                    new CliOptions(),
                    hidDeviceDiscovery: new FakeHidDiscovery(
                        new Transports.Hid.HidDeviceDescriptor(0x046D, 0xC08B, "Mouse", null),
                        new Transports.Hid.HidDeviceDescriptor(0x046D, 0xC31C, "Keyboard", null),
                        new Transports.Hid.HidDeviceDescriptor(0x0699, 0x0368, "Scope", null)));
                var box = new System.Windows.Controls.ComboBox { DisplayMemberPath = "Display", DataContext = viewModel };
                box.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(viewModel.HidDeviceOptions)));
                box.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new System.Windows.Data.Binding(nameof(viewModel.SelectedHidDevice)) { Mode = System.Windows.Data.BindingMode.TwoWay });
                StaTestRunner.DoEvents();
                Assert.AreEqual(3, box.Items.Count);

                viewModel.SelectedHidDevice = viewModel.HidDeviceOptions[0];
                StaTestRunner.DoEvents();

                Assert.AreEqual(1, box.Items.Count, "Picking the mouse filled in its ids, which narrows the list to it.");
                Assert.AreSame(viewModel.SelectedHidDevice, box.SelectedItem);
                Assert.AreEqual("046D:C08B  Mouse", viewModel.SelectedHidDevice!.Display, "The selection survived the list shrinking around it.");

                viewModel.VendorId = "0";
                viewModel.ProductId = "0";
                StaTestRunner.DoEvents();

                Assert.AreEqual(3, box.Items.Count, "Clearing both ids widens the list back out.");

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PresenterCheckBoxes_AreBoundToTheViewModelsPresenterChoices_InBothDirections()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var initial = new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, Presenter = ["ascii", "binary"], Parser = "hex" };
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), initial) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                // An ItemsControl in a window that's never Show()n doesn't generate its item
                // containers/checkboxes until it's itself laid out - do that directly.
                window.PresenterChoicesList.ApplyTemplate();
                window.PresenterChoicesList.Measure(new Size(500, 200));
                window.PresenterChoicesList.Arrange(new Rect(0, 0, 500, 200));
                window.PresenterChoicesList.UpdateLayout();
                var boxes = FindVisualChildren<System.Windows.Controls.CheckBox>(window.PresenterChoicesList).ToList();
                CollectionAssert.AreEqual(
                    new[] { "ascii", "utf8", "hex", "decimal", "octal", "binary", "k8055", "busylight", "scpi" },
                    boxes.Select(b => (string)b.Content).ToArray());
                CollectionAssert.AreEqual(
                    new[] { true, false, false, false, false, true, false, false, false },
                    boxes.Select(b => b.IsChecked == true).ToArray(),
                    "Each checkbox should reflect its presenter's IsSelected.");
                Assert.AreEqual("hex", window.ParserBox.SelectedItem, "The Send as box shows the profile's parser, separately from the presenters.");

                boxes[1].IsChecked = true; // utf8
                CollectionAssert.AreEqual(new[] { "ascii", "utf8", "binary" }, window.ViewModel.SelectedPresenters.ToArray());
                Assert.IsTrue(window.ViewModel.IsDirty);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<T> FindLogicalChildren<T>(System.Windows.DependencyObject parent)
        where T : System.Windows.DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindLogicalChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject parent)
        where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    [TestMethod]
    public void IdsShowHexCheckBox_TogglesTheRealVendorAndProductIdTextBoxesBetweenDecimalAndHex()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions { Transport = "hid" }) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.ViewModel.VendorId = "1234";
                window.ViewModel.ProductId = "49291";

                Assert.AreEqual("1234", window.VendorBox.Text);
                Assert.AreEqual("49291", window.ProductBox.Text);

                window.IdsShowHexBox.IsChecked = true;

                Assert.AreEqual("04D2", window.VendorBox.Text, "Checking 'Show as hex' should reformat the already-set value through the real binding, not require it to be re-entered.");
                Assert.AreEqual("C08B", window.ProductBox.Text);

                window.IdsShowHexBox.IsChecked = false;

                Assert.AreEqual("1234", window.VendorBox.Text, "Unchecking should revert back to decimal.");
                Assert.AreEqual("49291", window.ProductBox.Text);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectingMultipleProfiles_PopulatesSelectedProfileNamesThroughTheRealSelectionChangedHandler()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", TcpPort = 23 });
            store.Save("beta", new CliOptions { Transport = "tcp", Host = "192.168.0.2", TcpPort = 23 });

            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(store, new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.ProfilesList.SelectedItems.Add("alpha");
                window.ProfilesList.SelectedItems.Add("beta");

                CollectionAssert.AreEquivalent(new[] { "alpha", "beta" }, window.ViewModel.SelectedProfileNames.ToArray());

                window.ProfilesList.SelectedItems.Remove("alpha");

                CollectionAssert.AreEquivalent(new[] { "beta" }, window.ViewModel.SelectedProfileNames.ToArray());

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportSelectedProfilesCommand_ThroughTheRealMultiSelectList_ExportsOnlyTheSelectedProfiles()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", TcpPort = 23 });
            store.Save("beta", new CliOptions { Transport = "tcp", Host = "192.168.0.2", TcpPort = 23 });
            store.Save("gamma", new CliOptions { Transport = "tcp", Host = "192.168.0.3", TcpPort = 23 });

            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(store, new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.ProfilesList.SelectedItems.Add("alpha");
                window.ProfilesList.SelectedItems.Add("gamma");
                var zipPath = Path.Combine(directory, "export.zip");
                window.ViewModel.ImportExportPath = zipPath;

                window.ViewModel.ExportSelectedProfilesCommand.Execute(null);

                StringAssert.Contains(window.ViewModel.StatusMessage, "Exported 2 profile(s)");
                var importStore = new ConnectionProfileStore(Path.Combine(directory, "import"));
                importStore.ImportZip(zipPath);
                CollectionAssert.AreEqual(new[] { "alpha", "gamma" }, importStore.List().ToArray());

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_WiresResolveZipImportConflictForTheImportCommand()
    {
        // Structural only, same convention as Browse_Click/SaveAs_Click - actually triggering a
        // conflict would pop a real, blocking MessageBox.Show with nothing able to dismiss it
        // headlessly. This just proves the constructor wired something real (not left null, which
        // would silently default every conflict to Replace without ever asking).
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                Assert.IsNotNull(window.ViewModel.ResolveZipImportConflict);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectButton_Command_SetsResultAndClosesTheDialog()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();
                window.TransportBox.Text = "tcp";
                window.HostBox.Text = "192.168.0.107";
                window.TcpPortBox.Text = "23";

                var closed = false;
                window.ViewModel.CloseRequested += (_, _) => closed = true;

                window.ViewModel.ConnectCommand.Execute(null);

                Assert.IsTrue(closed);
                Assert.IsNotNull(window.Result);
                Assert.AreEqual("tcp", window.Result.Transport);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [TestMethod]
    public void DeleteSelectedProfilesCommand_ThroughTheRealMultiSelectList_DeletesOnlyTheSelectedProfiles()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("alpha", new CliOptions { Transport = "tcp", Host = "192.168.0.1", TcpPort = 23 });
            store.Save("beta", new CliOptions { Transport = "tcp", Host = "192.168.0.2", TcpPort = 23 });
            store.Save("gamma", new CliOptions { Transport = "tcp", Host = "192.168.0.3", TcpPort = 23 });

            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(store, new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                // The window wires a real MessageBox to this hook (never shown under test, same
                // convention as the ConfirmOverwrite/ConfirmDiscardChanges tests) - the stub proves
                // Delete Selected asks first without opening a modal box.
                IReadOnlyList<string>? asked = null;
                window.ViewModel.ConfirmDeleteProfiles = names =>
                {
                    asked = names;
                    return true;
                };

                window.ProfilesList.SelectedItems.Add("alpha");
                window.ProfilesList.SelectedItems.Add("gamma");
                window.ViewModel.DeleteSelectedProfilesCommand.Execute(null);

                CollectionAssert.AreEqual(new[] { "alpha", "gamma" }, asked!.ToArray());
                CollectionAssert.AreEqual(new[] { "beta" }, store.List().ToArray());
                CollectionAssert.AreEqual(new[] { "beta" }, window.ProfilesList.Items.Cast<string>().ToArray());

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteSelectedButton_IsBoundToDeleteSelectedProfilesCommand()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                // Logical tree, not visual: an unshown window hasn't generated its visual tree yet.
                var button = FindLogicalChildren<System.Windows.Controls.Button>(window)
                    .Single(b => b.Content is "Delete Selected");
                Assert.AreSame(window.ViewModel.DeleteSelectedProfilesCommand, button.Command);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [TestMethod]
    public void ReplaceAllButton_IsBoundToReplaceAllFromZipCommand()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                // Logical tree, not visual: an unshown window hasn't built its visual tree yet.
                var button = FindLogicalChildren<System.Windows.Controls.Button>(window)
                    .Single(b => b.Content is "Replace All");
                Assert.AreSame(window.ViewModel.ReplaceAllFromZipCommand, button.Command);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
