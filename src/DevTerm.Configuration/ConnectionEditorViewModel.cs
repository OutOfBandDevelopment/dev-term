using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace DevTerm.Configuration;

/// <summary>
/// The connection-editor screen's logic, shared by both front ends — WPF's <c>DeviceProfilesWindow</c>
/// binds its controls directly to these properties/commands (<c>Command="{Binding ConnectCommand}"</c>,
/// no code-behind event handlers); the TUI's <c>ConfigureMode</c> has no data-binding system of its
/// own, so it manually copies Terminal.Gui field values into these properties before calling a
/// command's <see cref="ICommand.Execute"/> and copies back afterward — but the validation, I/O,
/// and business logic itself (connect/load/save/import/export) lives here exactly once. See
/// docs/design/connection-profiles.md.
/// </summary>
public sealed class ConnectionEditorViewModel : INotifyPropertyChanged
{
    private static readonly CliOptionsValidator Validator = new();

    private readonly ConnectionProfileStore _store;

    private string _transport = "serial";
    private string _port = string.Empty;
    private string _baud = "9600";
    private string _host = string.Empty;
    private string _tcpPort = "0";
    private bool _listen;
    private string _hidVendorId = "0";
    private string _hidProductId = "0";
    private string _presenter = "hex";
    private string _lineEndingText = "None";
    private string _saveName = string.Empty;
    private string _importExportPath = string.Empty;
    private string _statusMessage = string.Empty;
    private string? _selectedProfileName;

    public ConnectionEditorViewModel(ConnectionProfileStore store, CliOptions initial, string? statusMessage = null)
    {
        _store = store;
        StatusMessage = statusMessage ?? string.Empty;
        LoadIntoFields(initial);
        RefreshProfiles();

        ConnectCommand = new RelayCommand(Connect);
        LoadCommand = new RelayCommand(LoadSelected);
        SaveCommand = new RelayCommand(SaveAsProfile);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export);
    }

    /// <summary>Raised once <see cref="ConnectCommand"/> succeeds — see <see cref="Result"/> for what to do with it.</summary>
    public event EventHandler? CloseRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Profiles { get; } = [];

    public ICommand ConnectCommand { get; }

    public ICommand LoadCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand ExportCommand { get; }

    /// <summary>
    /// Set once <see cref="ConnectCommand"/> validates; <see langword="null"/> until then. What
    /// "Connect" means depends on the caller: at startup, with no valid configuration yet, it's
    /// used directly to build the DI host and connect immediately. From the "Device Profiles..."
    /// menu item (already connected), it's saved as the default profile and a restart is requested
    /// instead — see docs/design/connection-profiles.md's note on why this doesn't live-swap the
    /// running session's transport.
    /// </summary>
    public CliOptions? Result { get; private set; }

    /// <summary>
    /// The valid values for <see cref="Transport"/>/<see cref="Presenter"/>/<see cref="LineEndingText"/>
    /// — instance properties (not static) purely so WPF's <c>{Binding TransportOptions}</c> can find
    /// them on the DataContext directly; the lists themselves are fixed and shared. Matches
    /// <see cref="CliOptionsValidator"/>'s own switch (transports), every presenter
    /// <c>AddTextPresenters</c> registers (see <c>DevTerm.Presenters.Text.ServiceCollectionExtensions</c>),
    /// and every <see cref="Configuration.LineEnding"/> member, respectively.
    /// </summary>
    public IReadOnlyList<string> TransportOptions { get; } = ["serial", "tcp", "hid"];

    public IReadOnlyList<string> PresenterOptions { get; } = ["ascii", "utf8", "hex", "decimal", "octal", "binary"];

    public IReadOnlyList<string> LineEndingOptions { get; } = Enum.GetNames<LineEnding>();

    public string Transport
    {
        get => _transport;
        set
        {
            if (_transport == value)
            {
                return;
            }

            SetField(ref _transport, value);

            // Not raised by SetField's [CallerMemberName] (that only fires for "Transport" itself)
            // - these three exist so each front end's Serial/TCP/USB HID field group can bind its
            // own visibility to "is this the selected transport" without re-deriving that
            // comparison itself (see CliOptions' [Category] grouping this mirrors).
            OnPropertyChanged(nameof(IsSerialTransport));
            OnPropertyChanged(nameof(IsTcpTransport));
            OnPropertyChanged(nameof(IsHidTransport));
        }
    }

    public bool IsSerialTransport => string.Equals(Transport, "serial", StringComparison.OrdinalIgnoreCase);

    public bool IsTcpTransport => string.Equals(Transport, "tcp", StringComparison.OrdinalIgnoreCase);

    public bool IsHidTransport => string.Equals(Transport, "hid", StringComparison.OrdinalIgnoreCase);

    public string Port { get => _port; set => SetField(ref _port, value); }

    public string Baud { get => _baud; set => SetField(ref _baud, value); }

    public string Host { get => _host; set => SetField(ref _host, value); }

    public string TcpPort { get => _tcpPort; set => SetField(ref _tcpPort, value); }

    public bool Listen { get => _listen; set => SetField(ref _listen, value); }

    public string HidVendorId { get => _hidVendorId; set => SetField(ref _hidVendorId, value); }

    public string HidProductId { get => _hidProductId; set => SetField(ref _hidProductId, value); }

    public string Presenter { get => _presenter; set => SetField(ref _presenter, value); }

    public string LineEndingText { get => _lineEndingText; set => SetField(ref _lineEndingText, value); }

    public string SaveName { get => _saveName; set => SetField(ref _saveName, value); }

    public string ImportExportPath { get => _importExportPath; set => SetField(ref _importExportPath, value); }

    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    /// <summary>The profile name currently selected in the saved-profiles list — bound two-way from WPF's <c>ListBox.SelectedItem</c>; the TUI sets it manually from its <c>ListView</c>'s selected index before calling <see cref="LoadCommand"/>.</summary>
    public string? SelectedProfileName { get => _selectedProfileName; set => SetField(ref _selectedProfileName, value); }

    public void LoadIntoFields(CliOptions options)
    {
        Transport = options.Transport;
        Port = options.Port ?? string.Empty;
        Baud = options.Baud.ToString();
        Host = options.Host ?? string.Empty;
        TcpPort = options.TcpPort.ToString();
        Listen = options.Listen;
        HidVendorId = options.HidVendorId.ToString();
        HidProductId = options.HidProductId.ToString();
        Presenter = options.Presenter;
        LineEndingText = options.LineEnding.ToString();
    }

    public CliOptions BuildOptions()
    {
        var options = new CliOptions
        {
            Transport = Transport.Trim(),
            Port = Port.Trim() is { Length: > 0 } p ? p : null,
            Host = Host.Trim() is { Length: > 0 } h ? h : null,
            Listen = Listen,
            Presenter = Presenter.Trim() is { Length: > 0 } pr ? pr : "hex",
        };

        if (int.TryParse(Baud, out var baud))
        {
            options.Baud = baud;
        }

        if (int.TryParse(TcpPort, out var tcpPort))
        {
            options.TcpPort = tcpPort;
        }

        if (int.TryParse(HidVendorId, out var vendorId))
        {
            options.HidVendorId = vendorId;
        }

        if (int.TryParse(HidProductId, out var productId))
        {
            options.HidProductId = productId;
        }

        if (Enum.TryParse<LineEnding>(LineEndingText, ignoreCase: true, out var lineEnding))
        {
            options.LineEnding = lineEnding;
        }

        return options;
    }

    public void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var name in _store.List())
        {
            Profiles.Add(name);
        }
    }

    private void Connect()
    {
        var options = BuildOptions();
        var validation = Validator.Validate(null, options);
        if (validation.Failed)
        {
            StatusMessage = string.Join(" ", validation.Failures);
            return;
        }

        Result = options;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LoadSelected()
    {
        if (SelectedProfileName is not { } name)
        {
            StatusMessage = "Select a profile first.";
            return;
        }

        try
        {
            LoadIntoFields(_store.Load(name));
            StatusMessage = $"Loaded profile '{name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load profile '{name}': {ex.Message}";
        }
    }

    private void SaveAsProfile()
    {
        var name = SaveName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Type a name to save this connection as a profile.";
            return;
        }

        var options = BuildOptions();
        var validation = Validator.Validate(null, options);
        if (validation.Failed)
        {
            StatusMessage = string.Join(" ", validation.Failures);
            return;
        }

        _store.Save(name, options);
        RefreshProfiles();
        SaveName = string.Empty;
        StatusMessage = $"Saved profile '{name}'.";
    }

    private void Import()
    {
        var path = ImportExportPath.Trim();
        if (path.Length == 0)
        {
            StatusMessage = "Type a file path to import from.";
            return;
        }

        try
        {
            LoadIntoFields(ConnectionProfileStore.LoadFromFile(path));
            StatusMessage = $"Imported '{path}' — review the fields, then Save Profile to keep it.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not import '{path}': {ex.Message}";
        }
    }

    private void Export()
    {
        var path = ImportExportPath.Trim();
        if (path.Length == 0)
        {
            StatusMessage = "Type a file path to export to.";
            return;
        }

        var options = BuildOptions();
        var validation = Validator.Validate(null, options);
        if (validation.Failed)
        {
            StatusMessage = string.Join(" ", validation.Failures);
            return;
        }

        ConnectionProfileStore.ExportToFile(path, options);
        StatusMessage = $"Exported to '{path}'.";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
