using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
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
public sealed class ConnectionEditorViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly CliOptionsValidator Validator = new();

    private readonly ConnectionProfileStore _store;
    private readonly FileSystemWatcher? _profilesWatcher;

    private string _transport = "serial";
    private string _port = string.Empty;
    private string _baud = "9600";
    private string _dataBits = "8";
    private string _parityText = "None";
    private string _stopBitsText = "One";
    private string _host = string.Empty;
    private string _tcpPort = "0";
    private bool _listen;
    private string _hidVendorId = "0";
    private string _hidProductId = "0";
    private string _presenter = "hex";
    private string _lineEndingText = "None";
    private string _description = string.Empty;
    private string _saveName = string.Empty;
    private string _importExportPath = string.Empty;
    private string _statusMessage = string.Empty;
    private string? _selectedProfileName;
    private bool _isDirty;

    /// <summary>
    /// Property names that setting doesn't count as an unsaved edit for <see cref="IsDirty"/>
    /// purposes — transient UI/status state, not a connection field a user could lose.
    /// </summary>
    private static readonly HashSet<string> NonDirtyProperties = new(StringComparer.Ordinal)
    {
        nameof(StatusMessage),
        nameof(SelectedProfileName),
        nameof(IsDirty),
        nameof(IsSerialTransport),
        nameof(IsTcpTransport),
        nameof(IsHidTransport),
    };

    public ConnectionEditorViewModel(ConnectionProfileStore store, CliOptions initial, string? statusMessage = null)
    {
        _store = store;
        StatusMessage = statusMessage ?? string.Empty;
        LoadIntoFields(initial);
        RefreshProfiles();
        IsDirty = false; // LoadIntoFields above marks every field it sets as dirty; a freshly-opened editor showing its starting configuration isn't actually dirty yet.

        ConnectCommand = new RelayCommand(Connect);
        LoadCommand = new RelayCommand(LoadSelected);
        SaveCommand = new RelayCommand(SaveAsProfile);
        DeleteCommand = new RelayCommand(DeleteSelected);
        RefreshCommand = new RelayCommand(RefreshProfiles);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export);

        // Auto-refresh when a profile is added/removed/renamed on disk by another process (the
        // other front end, or the user editing ~/.dev-term/profiles by hand) — the manual Refresh
        // button/command above still exists for anyone who doesn't trust the watcher. Directory.CreateDirectory
        // first since FileSystemWatcher throws immediately if the directory doesn't exist yet (a
        // fresh install with no profiles saved yet). Events fire on a background thread, which
        // neither front end's UI can touch directly, so this only raises ProfilesChangedExternally
        // — each front end marshals onto its own UI thread before actually calling RefreshCommand.
        try
        {
            Directory.CreateDirectory(store.ProfilesDirectory);
            _profilesWatcher = new FileSystemWatcher(store.ProfilesDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _profilesWatcher.Created += (_, _) => ProfilesChangedExternally?.Invoke(this, EventArgs.Empty);
            _profilesWatcher.Deleted += (_, _) => ProfilesChangedExternally?.Invoke(this, EventArgs.Empty);
            _profilesWatcher.Renamed += (_, _) => ProfilesChangedExternally?.Invoke(this, EventArgs.Empty);
        }
        catch (SystemException)
        {
            // A watcher is a nice-to-have, not essential — the manual Refresh button/command still
            // works regardless. Don't fail editor construction over e.g. a permissions problem on
            // the profiles directory.
            _profilesWatcher = null;
        }
    }

    /// <summary>Raised once <see cref="ConnectCommand"/> succeeds — see <see cref="Result"/> for what to do with it.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Raised (on a background thread — see the constructor) when a profile is added, removed, or
    /// renamed on disk outside this view model, e.g. by the other front end or by hand. Each front
    /// end subscribes and marshals onto its own UI thread before actually refreshing
    /// (<see cref="RefreshCommand"/>) — this view model has no UI thread of its own to do that on.
    /// </summary>
    public event EventHandler? ProfilesChangedExternally;

    public void Dispose() => _profilesWatcher?.Dispose();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Profiles { get; } = [];

    public ICommand ConnectCommand { get; }

    public ICommand LoadCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand RefreshCommand { get; }

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

    public IReadOnlyList<string> ParityOptions { get; } = Enum.GetNames<Parity>();

    public IReadOnlyList<string> StopBitsOptions { get; } = Enum.GetNames<StopBits>();

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

    public string DataBits { get => _dataBits; set => SetField(ref _dataBits, value); }

    public string ParityText { get => _parityText; set => SetField(ref _parityText, value); }

    public string StopBitsText { get => _stopBitsText; set => SetField(ref _stopBitsText, value); }

    public string Host { get => _host; set => SetField(ref _host, value); }

    public string TcpPort { get => _tcpPort; set => SetField(ref _tcpPort, value); }

    public bool Listen { get => _listen; set => SetField(ref _listen, value); }

    public string HidVendorId { get => _hidVendorId; set => SetField(ref _hidVendorId, value); }

    public string HidProductId { get => _hidProductId; set => SetField(ref _hidProductId, value); }

    public string Presenter { get => _presenter; set => SetField(ref _presenter, value); }

    public string LineEndingText { get => _lineEndingText; set => SetField(ref _lineEndingText, value); }

    public string Description { get => _description; set => SetField(ref _description, value); }

    public string SaveName { get => _saveName; set => SetField(ref _saveName, value); }

    public string ImportExportPath { get => _importExportPath; set => SetField(ref _importExportPath, value); }

    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    /// <summary>The profile name currently selected in the saved-profiles list — bound two-way from WPF's <c>ListBox.SelectedItem</c>; the TUI sets it manually from its <c>ListView</c>'s selected index before calling <see cref="LoadCommand"/>.</summary>
    public string? SelectedProfileName { get => _selectedProfileName; set => SetField(ref _selectedProfileName, value); }

    /// <summary>
    /// <see langword="true"/> if a connection field has changed since the last successful Load,
    /// Save, Connect, or Import — the "would I lose something by closing/loading over this right
    /// now" signal each front end checks via <see cref="ConfirmClose"/> before actually closing.
    /// </summary>
    public bool IsDirty { get => _isDirty; private set => SetField(ref _isDirty, value); }

    /// <summary>
    /// Set by each front end to show its own native "overwrite '{name}'?" confirmation (a
    /// <c>MessageBox</c> in WPF, a Terminal.Gui message box in the TUI) — the view model has no UI
    /// of its own to show one directly. <see cref="SaveCommand"/> calls it only when
    /// <see cref="SaveName"/> already names an existing profile, and only saves if it returns
    /// <see langword="true"/>; left <see langword="null"/>, saving always proceeds without asking
    /// (e.g. in tests that don't wire a real dialog).
    /// </summary>
    public Func<string, bool>? ConfirmOverwrite { get; set; }

    /// <summary>
    /// Set by each front end to show its own native "you have unsaved changes — discard them?"
    /// confirmation, invoked only when <see cref="IsDirty"/> — see <see cref="ConfirmClose"/>. Left
    /// <see langword="null"/>, an in-progress close/load always proceeds without asking (e.g. in
    /// tests that don't wire a real dialog), same convention as <see cref="ConfirmOverwrite"/>.
    /// </summary>
    public Func<bool>? ConfirmDiscardChanges { get; set; }

    /// <summary>
    /// Call before discarding whatever's currently unsaved in the fields — closing/quitting the
    /// editor, or loading a different profile over them (not before Connect, which already resets
    /// <see cref="IsDirty"/> itself on success — see its own doc comment, since Connect doesn't
    /// discard anything). Returns <see langword="true"/> if it's fine to proceed: either nothing's
    /// unsaved, or the user confirmed discarding it via <see cref="ConfirmDiscardChanges"/>.
    /// </summary>
    public bool ConfirmClose() => !IsDirty || (ConfirmDiscardChanges?.Invoke() ?? true);

    public void LoadIntoFields(CliOptions options)
    {
        Transport = options.Transport;
        Port = options.Port ?? string.Empty;
        Baud = options.Baud.ToString();
        DataBits = options.DataBits.ToString();
        ParityText = options.Parity.ToString();
        StopBitsText = options.StopBits.ToString();
        Host = options.Host ?? string.Empty;
        TcpPort = options.TcpPort.ToString();
        Listen = options.Listen;
        HidVendorId = options.HidVendorId.ToString();
        HidProductId = options.HidProductId.ToString();
        Presenter = options.Presenter;
        LineEndingText = options.LineEnding.ToString();
        Description = options.Description ?? string.Empty;
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
            Description = Description.Trim() is { Length: > 0 } d ? d : null,
        };

        if (int.TryParse(Baud, out var baud))
        {
            options.Baud = baud;
        }

        if (int.TryParse(DataBits, out var dataBits))
        {
            options.DataBits = dataBits;
        }

        if (Enum.TryParse<Parity>(ParityText, ignoreCase: true, out var parity))
        {
            options.Parity = parity;
        }

        if (Enum.TryParse<StopBits>(StopBitsText, ignoreCase: true, out var stopBits))
        {
            options.StopBits = stopBits;
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

    private void DeleteSelected()
    {
        if (SelectedProfileName is not { } name)
        {
            StatusMessage = "Select a profile first.";
            return;
        }

        if (_store.Delete(name))
        {
            RefreshProfiles();
            SelectedProfileName = null;
            StatusMessage = $"Deleted profile '{name}'.";
        }
        else
        {
            StatusMessage = $"No profile named '{name}' was found.";
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
        IsDirty = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LoadSelected()
    {
        if (SelectedProfileName is not { } name)
        {
            StatusMessage = "Select a profile first.";
            return;
        }

        if (!ConfirmClose())
        {
            StatusMessage = "Load cancelled — you have unsaved changes.";
            return;
        }

        try
        {
            LoadIntoFields(_store.Load(name));
            SaveName = name;
            IsDirty = false;
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

        if (Profiles.Contains(name) && ConfirmOverwrite?.Invoke(name) == false)
        {
            StatusMessage = $"Not saved — '{name}' already exists.";
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
        IsDirty = false;
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
            IsDirty = false;
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

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        if (propertyName is not null && !NonDirtyProperties.Contains(propertyName))
        {
            IsDirty = true;
        }
    }
}
