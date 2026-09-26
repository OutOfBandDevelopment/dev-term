using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using DevTerm.Configuration;
using DevTerm.UiDefinitions.Forms;
using Microsoft.Win32;

namespace DevTerm.Wpf;

/// <summary>
/// A full connection editor — pick a saved profile to load, edit any field by hand, save it under
/// a name, import/export a profile as a standalone JSON file, or connect with the current fields.
/// All of that logic lives in <see cref="ConnectionEditorViewModel"/>, shared with the TUI's
/// <c>ConfigureMode</c>: the profile list and the save/import/export/connect rows are XAML bound to
/// it (<c>Command="{Binding ...}"</c>), and the connection fields are <em>generated</em> —
/// <see cref="ConnectionEditorViewModel.FormDefinition"/> rendered by the generic
/// <see cref="FormRenderer"/> and bound back to the view model, the same definition the TUI renders.
/// No business logic in code-behind; the native file-browse dialogs are the one exception, having
/// no pure-binding equivalent. See docs/design/connection-profiles.md and
/// docs/specs/connection-editor.md.
/// </summary>
public partial class DeviceProfilesWindow : Window
{
    public ConnectionEditorViewModel ViewModel { get; }

    /// <summary>
    /// Set once the user presses Connect with fields that validate; <see langword="null"/> if they
    /// close the window instead. What "Connect" means depends on the caller: at startup, with no
    /// valid configuration yet, it's used directly to build the DI host and connect immediately
    /// (see <see cref="App"/>). From <see cref="MainWindow"/>'s "Device Profiles..." menu item
    /// (already connected), it's saved as the default profile *and* live-switched to immediately
    /// via <see cref="MainWindow.SwitchProfileAsync"/> — no restart needed.
    /// </summary>
    public CliOptions? Result => ViewModel.Result;

    /// <summary>The generated connection-field form (see <see cref="FormRenderer"/>).</summary>
    internal WpfFormParts Form { get; }

    // The generated widgets the tests (and nothing else) reach by their old XAML names.
    internal ComboBox TransportBox => (ComboBox)Form.ControlViews[nameof(ConnectionEditorViewModel.Transport)];

    internal TextBox PortBox => Form.TextBoxes[nameof(ConnectionEditorViewModel.Port)];

    internal TextBox HostBox => Form.TextBoxes[nameof(ConnectionEditorViewModel.Host)];

    internal TextBox TcpPortBox => Form.TextBoxes[nameof(ConnectionEditorViewModel.TcpPort)];

    internal TextBox VendorBox => Form.TextBoxes[nameof(ConnectionEditorViewModel.VendorIdDisplay)];

    internal TextBox ProductBox => Form.TextBoxes[nameof(ConnectionEditorViewModel.ProductIdDisplay)];

    internal TextBox SerialNumberBox => Form.TextBoxes[nameof(ConnectionEditorViewModel.SerialNumber)];

    internal CheckBox IdsShowHexBox => (CheckBox)Form.ControlViews[nameof(ConnectionEditorViewModel.IdsShowHex)];

    internal ComboBox ParserBox => (ComboBox)Form.ControlViews[nameof(ConnectionEditorViewModel.Parser)];

    internal FrameworkElement PresenterChoicesList => Form.ControlViews[nameof(ConnectionEditorViewModel.PresentersText)];

    internal FrameworkElement SerialPanel => Form.SectionPanels["Serial"];

    internal FrameworkElement TcpPanel => Form.SectionPanels["TCP"];

    internal FrameworkElement UsbDevicePanel => Form.SectionPanels["USB Device"];

    internal FrameworkElement LoopbackPanel => Form.SectionPanels["Loopback"];

    internal FrameworkElement DetectedHidDevicesRow => Form.Rows[nameof(ConnectionEditorViewModel.SelectedHidDevice)];

    internal FrameworkElement DetectedUsbtmcDevicesRow => Form.Rows[nameof(ConnectionEditorViewModel.SelectedUsbtmcDevice)];

    /// <summary>The serial "not found" hint's row (collapsed unless <see cref="ConnectionEditorViewModel.ConnectedDeviceNotFound"/>).</summary>
    internal FrameworkElement PortNotFoundText => Form.Rows[nameof(ConnectionEditorViewModel.SerialPortNotFoundHint)];

    internal FrameworkElement UsbDeviceNotFoundText => Form.Rows[nameof(ConnectionEditorViewModel.UsbDeviceNotFoundHint)];

    internal ComboBox DetectedPortsBox { get; }

    internal ComboBox DetectedHidDevicesBox { get; }

    internal ComboBox DetectedUsbtmcDevicesBox { get; }

    public DeviceProfilesWindow(ConnectionProfileStore store, CliOptions initial, string? statusText = null)
    {
        InitializeComponent();
        ViewModel = new ConnectionEditorViewModel(store, initial, statusText);
        DataContext = ViewModel;

        // The detected-device pickers stay hand-built, real WPF bindings to the view model's rich
        // device lists (a port's description-bearing Display over its Name; a live-filtered HID/USBTMC
        // list whose selection survives filtering) - the generic form places, labels and shows/hides
        // them with their transport like every other field.
        DetectedPortsBox = new ComboBox { DisplayMemberPath = nameof(SerialPortOption.Display), SelectedValuePath = nameof(SerialPortOption.Name) };
        DetectedPortsBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ConnectionEditorViewModel.SerialPortOptions)));
        DetectedPortsBox.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(ConnectionEditorViewModel.SelectedSerialPort)));
        DetectedHidDevicesBox = new ComboBox { DisplayMemberPath = nameof(HidDeviceOption.Display) };
        DetectedHidDevicesBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ConnectionEditorViewModel.HidDeviceOptions)));
        DetectedHidDevicesBox.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(ConnectionEditorViewModel.SelectedHidDevice)));
        DetectedUsbtmcDevicesBox = new ComboBox { DisplayMemberPath = nameof(UsbtmcDeviceOption.Display) };
        DetectedUsbtmcDevicesBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ConnectionEditorViewModel.UsbtmcDeviceOptions)));
        DetectedUsbtmcDevicesBox.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(ConnectionEditorViewModel.SelectedUsbtmcDevice)));

        var options = new WpfFormOptions();
        options.CustomWidgets[nameof(ConnectionEditorViewModel.SelectedSerialPort)] = _ => DetectedPortsBox;
        options.CustomWidgets[nameof(ConnectionEditorViewModel.SelectedHidDevice)] = _ => DetectedHidDevicesBox;
        options.CustomWidgets[nameof(ConnectionEditorViewModel.SelectedUsbtmcDevice)] = _ => DetectedUsbtmcDevicesBox;
        var binding = new FormBinding(ViewModel);
        Form = FormRenderer.Build(ViewModel.FormDefinition, binding, options);
        ConnectionFormHost.Content = Form.Root;
        Closed += (_, _) => binding.Dispose();
        ViewModel.ConfirmOverwrite = name => MessageBox.Show(
            this,
            $"A profile named '{name}' already exists. Overwrite it?",
            "dev-term",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        ViewModel.ConfirmDiscardChanges = () => MessageBox.Show(
            this,
            "You have unsaved changes. Close without saving?",
            "dev-term",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        ViewModel.ConfirmDeleteProfiles = names => MessageBox.Show(
            this,
            names.Count == 1
                ? $"Delete profile '{names[0]}'? This can't be undone."
                : $"Delete {names.Count} profiles ({string.Join(", ", names)})? This can't be undone.",
            "dev-term",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        ViewModel.ConfirmReplaceAllProfiles = (existing, incoming) => MessageBox.Show(
            this,
            $"Delete all {existing} saved profile(s) and import the {incoming} in the zip? This can't be undone.",
            "dev-term",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        // Yes/No/Cancel maps naturally onto the three resolutions without a custom dialog: Yes
        // overwrites, No keeps both (renamed), Cancel leaves the existing profile untouched.
        ViewModel.ResolveZipImportConflict = name => MessageBox.Show(
            this,
            $"A profile named '{name}' already exists. Replace it? (No renames the imported copy, Cancel skips it.)",
            "dev-term",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning) switch
        {
            MessageBoxResult.No => ZipImportConflictResolution.Rename,
            MessageBoxResult.Cancel => ZipImportConflictResolution.Skip,
            _ => ZipImportConflictResolution.Replace,
        };
        // Covers both the "Close" button (IsCancel="True", no ViewModel command of its own) and
        // pressing Escape - WPF triggers an IsCancel button's click for Escape by default, and
        // either way ends up here via Window.Close(). Doesn't affect the Connect path above:
        // Connect() already clears IsDirty before raising CloseRequested, so ConfirmClose() sees
        // IsDirty == false and never prompts for a result the user is actively trying to keep.
        Closing += (_, e) =>
        {
            if (!ViewModel.ConfirmClose())
            {
                e.Cancel = true;
            }
        };
        // The watcher fires on a background thread - Dispatcher.BeginInvoke marshals onto the UI
        // thread before touching the (data-bound) Profiles collection or anything else.
        ViewModel.ProfilesChangedExternally += (_, _) => Dispatcher.BeginInvoke(() => ViewModel.RefreshCommand.Execute(null));
        Closed += (_, _) => ViewModel.Dispose();
        ViewModel.CloseRequested += (_, _) =>
        {
            try
            {
                // Setting DialogResult both closes the window and makes ShowDialog() return true -
                // both real callers (App/MainWindow) need that. It throws if this window wasn't
                // actually shown via ShowDialog() though, which is exactly the case in tests that
                // drive ViewModel.ConnectCommand directly without ever showing the window (the same
                // "don't Show() a window under direct test" convention MainWindowTests already
                // follows, just hitting WPF's dialog-specific version of it) - ViewModel.Result is
                // already set correctly by that point regardless, so there's nothing else to do.
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    // ListBox.SelectedItems has no dependency property of its own to bind two-way in pure XAML
    // (see the ProfilesList comment in the .xaml) - this just mirrors it into the view model's
    // plain ObservableCollection, which ExportSelectedProfilesCommand reads from.
    private void ProfilesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ViewModel.SelectedProfileNames.Clear();
        foreach (var item in ProfilesList.SelectedItems)
        {
            if (item is string name)
            {
                ViewModel.SelectedProfileNames.Add(name);
            }
        }
    }

    // Same LoadCommand the Load button is bound to - not a separate code path. Selects the
    // double-clicked row first so a ctrl/shift-extended multi-selection can't leave Load acting on
    // some other profile than the one that was actually clicked.
    private void ProfilesList_ItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem { DataContext: string name })
        {
            ViewModel.SelectedProfileName = name;
        }

        ViewModel.LoadCommand.Execute(null);
        e.Handled = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "dev-term connection profile (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.ImportExportPath = dialog.FileName;
        }
    }

    // The save-style counterpart to Browse: unlike OpenFileDialog, SaveFileDialog lets you type a
    // brand-new filename that doesn't exist yet - for Export specifically. Browse/OpenFileDialog
    // stays as the picker for Import (an existing file only).
    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "dev-term connection profile (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.ImportExportPath = dialog.FileName;
        }
    }
}
