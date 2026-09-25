using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DevTerm.Devices.Scpi;
using DevTerm.Transports.Hid;
using DevTerm.Transports.Serial;
using DevTerm.Transports.Usbtmc;
using Microsoft.Extensions.Options;

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
    private static readonly CliOptionsValidator _validator = new();

    private readonly ConnectionProfileStore _store;
    private readonly FileSystemWatcher? _profilesWatcher;

    private string _transport = "serial";
    private string _port = string.Empty;
    private string _baud = "9600";
    private string _dataBits = "8";
    private string _parityText = "None";
    private string _stopBitsText = "One";
    private string _handshakeText = "None";
    private string _scpiProfile = string.Empty;
    private string _host = string.Empty;
    private string _tcpPort = "0";
    private bool _listen;
    private string _vendorId = "0";
    private string _productId = "0";
    private string _serialNumber = string.Empty;
    private string _devicePath = string.Empty;
    private string _parser = CliOptions.DefaultPresenter;
    private string _lineEndingText = "None";
    private string _description = string.Empty;
    private string _saveName = string.Empty;
    private string _importExportPath = string.Empty;
    private string _statusMessage = string.Empty;
    private string? _selectedProfileName;

    // The profile most recently loaded into the form - see BuildOptions for why it's kept.
    private CliOptions _loadedOptions = new();
    private bool _isDirty;
    private string? _selectedSerialPort;
    private HidDeviceOption? _selectedHidDevice;
    private IReadOnlyList<HidDeviceOption> _detectedHidDevices = [];
    private readonly ObservableCollection<HidDeviceOption> _hidDeviceOptions = [];
    private UsbtmcDeviceOption? _selectedUsbtmcDevice;
    private IReadOnlyList<UsbtmcDeviceOption> _detectedUsbtmcDevices = [];
    private readonly ObservableCollection<UsbtmcDeviceOption> _usbtmcDeviceOptions = [];
    private bool _idsShowHex;

    /// <summary>
    /// Property names that setting doesn't count as an unsaved edit for <see cref="IsDirty"/>
    /// purposes — transient UI/status state, not a connection field a user could lose.
    /// </summary>
    private static readonly HashSet<string> _nonDirtyProperties = new(StringComparer.Ordinal)
    {
        nameof(StatusMessage),
        nameof(SelectedProfileName),
        nameof(IsDirty),
        nameof(IsSerialTransport),
        nameof(IsTcpTransport),
        nameof(IsHidTransport),
        nameof(IsUsbtmcTransport),
        nameof(IsUsbDeviceTransport),
        nameof(IsLoopbackTransport),
        nameof(SelectedSerialPort),
        nameof(SelectedHidDevice),
        nameof(SelectedUsbtmcDevice),
        nameof(IdsShowHex),
        nameof(VendorIdDisplay),
        nameof(ProductIdDisplay),
        nameof(ConnectedDeviceNotFound),
    };

    /// <summary>
    /// Which saved profiles are checked in the profiles list, for <see cref="ExportSelectedProfilesCommand"/>
    /// — a separate collection from <see cref="SelectedProfileName"/> (which stays single-item,
    /// unchanged, since Load/Delete only ever operate on one profile at a time). Each front end
    /// populates this itself from its own list control: WPF's <c>ListBox.SelectedItems</c>
    /// (<c>SelectionMode="Extended"</c>) via a <c>SelectionChanged</c> handler, the TUI's
    /// <c>ListView.GetAllMarkedItems()</c> (<c>MarkMultiple = true</c>) read once right before the
    /// export button's own handler runs — plain mutation, not itself a tracked edit (see
    /// <see cref="_nonDirtyProperties"/>' comment on <see cref="SelectedProfileName"/>: this is the
    /// same kind of transient UI state).
    /// </summary>
    public ObservableCollection<string> SelectedProfileNames { get; } = [];

    public ConnectionEditorViewModel(
        ConnectionProfileStore store,
        CliOptions initial,
        string? statusMessage = null,
        ISerialPortDiscovery? serialPortDiscovery = null,
        IHidDeviceDiscovery? hidDeviceDiscovery = null,
        IUsbtmcDeviceDiscovery? usbtmcDeviceDiscovery = null)
    {
        _store = store;
        PresenterChoices = [.. PresenterOptions.Select(name => new PresenterSelection(name))];
        foreach (var choice in PresenterChoices)
        {
            // A checkbox toggling is an edit like any other field's - same dirty tracking, without
            // a property-changed name of its own to put in NonDirtyProperties.
            choice.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(PresenterChoices));
                OnPropertyChanged(nameof(IsScpiPresenterSelected));
            };
        }

        StatusMessage = statusMessage ?? string.Empty;
        SerialPortOptions = SafeDiscover(serialPortDiscovery ?? new SystemSerialPortDiscovery());
        _detectedHidDevices = SafeDiscover(hidDeviceDiscovery ?? new SystemHidDeviceDiscovery());
        RefreshHidDeviceOptions();
        _detectedUsbtmcDevices = SafeDiscover(usbtmcDeviceDiscovery ?? new SystemUsbtmcDeviceDiscovery());
        RefreshUsbtmcDeviceOptions();
        LoadIntoFields(initial);
        RefreshProfiles();
        IsDirty = false; // LoadIntoFields above marks every field it sets as dirty; a freshly-opened editor showing its starting configuration isn't actually dirty yet.

        ConnectCommand = new RelayCommand(Connect);
        LoadCommand = new RelayCommand(LoadSelected);
        SaveCommand = new RelayCommand(SaveAsProfile);
        DeleteCommand = new RelayCommand(DeleteSelected);
        RefreshCommand = new RelayCommand(RefreshProfiles);
        ImportCommand = new RelayCommand(Import);
        ReplaceAllFromZipCommand = new RelayCommand(ReplaceAllFromZip);
        ExportCommand = new RelayCommand(Export);
        ExportSelectedProfilesCommand = new RelayCommand(() => ExportProfilesZip(SelectedProfileNames));
        ExportAllProfilesCommand = new RelayCommand(() => ExportProfilesZip(Profiles));
        DeleteSelectedProfilesCommand = new RelayCommand(DeleteMarkedProfiles);

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

    // Enumeration can fail on a locked-down machine (permissions, a driver quirk) - a picker list
    // is a convenience, not something construction should fail over, the same reasoning already
    // applied to the profiles-folder FileSystemWatcher above.
    private static IReadOnlyList<SerialPortOption> SafeDiscover(ISerialPortDiscovery discovery)
    {
        IReadOnlyList<string> names;
        try
        {
            names = discovery.GetPortNames();
        }
        catch (SystemException)
        {
            return [];
        }

        // Descriptions only decorate ports GetPortNames already reported (the OS keeps records of
        // long-gone devices too), and are optional - failing to read them just means short names.
        IReadOnlyDictionary<string, string> descriptions;
        try
        {
            descriptions = discovery.GetPortDescriptions();
        }
        catch (SystemException)
        {
            descriptions = new Dictionary<string, string>();
        }

        return [.. names.Select(name => SerialPortOption.From(name, descriptions))];
    }

    private static IReadOnlyList<HidDeviceOption> SafeDiscover(IHidDeviceDiscovery discovery)
    {
        try
        {
            return DisambiguateDisplay([.. discovery.GetDevices().Select(HidDeviceOption.FromDescriptor)]);
        }
        catch (SystemException)
        {
            return [];
        }
    }

    // Two attached devices can share an identical Display — confirmed live with three simultaneously-
    // attached Velleman K8055 boards, all configured to the same board address (same VID/PID, no
    // serial, no product name). Appends a short suffix built from DevicePath (the one field
    // guaranteed to differ per physical connection — see HidDeviceOption's doc comment) so the picker
    // shows something a person can actually tell apart, instead of several indistinguishable rows.
    private static List<HidDeviceOption> DisambiguateDisplay(List<HidDeviceOption> options)
    {
        var duplicateDisplays = options
            .GroupBy(o => o.Display, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (duplicateDisplays.Count == 0)
        {
            return options;
        }

        return [.. options.Select(o => duplicateDisplays.Contains(o.Display)
            ? o with { Display = $"{o.Display}  [{DevicePathTag(o.DevicePath)}]" }
            : o)];
    }

    // The USB instance-qualifier segment of a HidSharp device path
    // ("hid#vid_10cf&pid_5500#7&83de718&0&0000#{guid}" -> "7&83de718&0&0000") — the part that
    // actually varies per physical connection; the trailing {GUID} is always the constant HID
    // device-interface class id, so it's useless for telling two devices apart.
    private static string DevicePathTag(string devicePath)
    {
        var segments = devicePath.Split('#');
        return segments.Length >= 3 ? segments[2] : devicePath;
    }

    // "Best match" for auto-selecting the live device a loaded profile's VendorId/ProductId/
    // SerialNumber/DevicePath most likely refers to, mirroring LoadIntoFields' SelectedSerialPort
    // sync. VendorId+ProductId must match exactly, so this never returns a device the profile
    // didn't ask for; SerialNumber and DevicePath only break a tie among several currently-attached
    // devices sharing the same VendorId+ProductId (a real case, not hypothetical — three
    // simultaneously-attached Velleman K8055 boards, same VID/PID, no serial descriptor at all —
    // see HidDeviceOption's doc comment). SerialNumber is preferred when it's non-blank (a real
    // serial descriptor is portable across ports, so it's the more reliable identity); DevicePath is
    // the fallback for a device with no serial descriptor, tied to its physical USB hub/port. If
    // neither breaks the tie, falls back to the first match found rather than leaving the picker
    // unset.
    private static T? FindBestUsbDeviceMatch<T>(
        IReadOnlyList<T> candidates,
        int vendorId,
        int productId,
        string? serialNumber,
        string? devicePath,
        Func<T, int> getVendorId,
        Func<T, int> getProductId,
        Func<T, string?> getSerialNumber,
        Func<T, string?> getDevicePath)
        where T : class
    {
        var matches = candidates.Where(c => getVendorId(c) == vendorId && getProductId(c) == productId).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(serialNumber))
        {
            var exact = matches.FirstOrDefault(c => string.Equals(getSerialNumber(c), serialNumber, StringComparison.Ordinal));
            if (exact is not null)
            {
                return exact;
            }
        }

        if (!string.IsNullOrEmpty(devicePath))
        {
            var exact = matches.FirstOrDefault(c => string.Equals(getDevicePath(c), devicePath, StringComparison.Ordinal));
            if (exact is not null)
            {
                return exact;
            }
        }

        return matches[0];
    }

    private static IReadOnlyList<UsbtmcDeviceOption> SafeDiscover(IUsbtmcDeviceDiscovery discovery)
    {
        try
        {
            return [.. discovery.GetDevices().Select(UsbtmcDeviceOption.FromDescriptor)];
        }
        catch (SystemException)
        {
            return [];
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Profiles { get; } = [];

    public ICommand ConnectCommand { get; }

    public ICommand LoadCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ImportCommand { get; }

    /// <summary>
    /// Deletes every saved profile and then imports every profile in the zip at
    /// <see cref="ImportExportPath"/> — a "restore from backup", unlike <see cref="ImportCommand"/>'s
    /// per-name Replace/Rename/Skip, which never removes a profile the zip doesn't mention. Asks first
    /// via <see cref="ConfirmReplaceAllProfiles"/>; the zip is fully read and checked before anything
    /// is deleted.
    /// </summary>
    public ICommand ReplaceAllFromZipCommand { get; }

    public ICommand ExportCommand { get; }

    /// <summary>Exports the profiles named in <see cref="SelectedProfileNames"/> as a single zip to <see cref="ImportExportPath"/>.</summary>
    public ICommand ExportSelectedProfilesCommand { get; }

    /// <summary>Exports every saved profile as a single zip to <see cref="ImportExportPath"/>, regardless of <see cref="SelectedProfileNames"/>.</summary>
    public ICommand ExportAllProfilesCommand { get; }

    /// <summary>Deletes every profile named in <see cref="SelectedProfileNames"/> (after <see cref="ConfirmDeleteProfiles"/>, if a front end wired one) — the multi-select counterpart to <see cref="DeleteCommand"/>, which still only ever acts on the single <see cref="SelectedProfileName"/>.</summary>
    public ICommand DeleteSelectedProfilesCommand { get; }

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
    /// The valid values for <see cref="Transport"/>/<see cref="PresenterChoices"/>/<see cref="Parser"/>/<see cref="LineEndingText"/>
    /// — instance properties (not static) purely so WPF's <c>{Binding TransportOptions}</c> can find
    /// them on the DataContext directly; the lists themselves are fixed and shared. Matches
    /// <see cref="CliOptionsValidator"/>'s own switch (transports), every presenter
    /// <c>AddTextPresenters</c> registers (see <c>DevTerm.Presenters.Text.ServiceCollectionExtensions</c>),
    /// and every <see cref="Configuration.LineEnding"/> member, respectively.
    /// </summary>
    public IReadOnlyList<string> TransportOptions { get; } = ["serial", "tcp", "hid", "usbtmc", "loopback"];

    /// <summary>
    /// This list is hardcoded rather than resolved from the live <see cref="Core.Presenters.PresenterCatalog"/>
    /// because this view model is constructed before any transport/session exists (it's what builds
    /// the <see cref="CliOptions"/> a session gets built from) — so it must include every
    /// device-specific display presenter (<c>k8055</c>, <c>busylight</c>, ...) by name in addition to
    /// the built-in text ones, or a device's control panel can never receive live input: switching to
    /// that device here without also checking its presenter here leaves <see cref="CliOptions.EffectivePresenters"/>
    /// on the default, so the device's decoder is registered but never wired into the session's
    /// <c>Pipeline</c> and its indicators never update, even though outbound control-panel commands
    /// (which write to the session directly, not through a presenter) work fine — confirmed live
    /// against the K8055 GUI panel 2026-09-22.
    /// </summary>
    public IReadOnlyList<string> PresenterOptions { get; } =
        ["ascii", "utf8", "hex", "decimal", "octal", "binary", "k8055", "busylight", "scpi"];

    public IReadOnlyList<string> LineEndingOptions { get; } = Enum.GetNames<LineEnding>();

    public IReadOnlyList<string> ParityOptions { get; } = Enum.GetNames<Parity>();

    public IReadOnlyList<string> StopBitsOptions { get; } = Enum.GetNames<StopBits>();

    public IReadOnlyList<string> HandshakeOptions { get; } = Enum.GetNames<Handshake>();

    /// <summary>
    /// The saved-profile SCPI-instrument choice — <see cref="ScpiProfileCatalog.AutoDetectChoiceName"/>,
    /// <see cref="ScpiProfileCatalog.Generic"/>'s own name, or a real <see cref="ScpiProfileCatalog.All"/>
    /// entry's name — preselected so the "SCPI Instrument..." menu item doesn't need its picker
    /// re-run every connection. Empty means "always ask" (today's behavior, unchanged).
    /// </summary>
    public IReadOnlyList<string> ScpiProfileOptions { get; } =
        [ScpiProfileCatalog.AutoDetectChoiceName, ScpiProfileCatalog.Generic.Name, .. ScpiProfileCatalog.All.Select(p => p.Name)];

    /// <summary>
    /// Serial ports actually attached to this machine right now (<see cref="ISerialPortDiscovery.GetPortNames"/>,
    /// the same enumeration <c>--listports</c> uses), for a "pick from what's plugged in" combobox
    /// next to <see cref="Port"/> — set once at construction, empty (not an error) if discovery fails
    /// or nothing's attached. <see cref="Port"/> stays freely typable regardless; picking one here
    /// just fills it in via <see cref="SelectedSerialPort"/>. Each entry carries the OS's description
    /// of the port when known (Windows only so far), for display alongside the short name.
    /// </summary>
    public IReadOnlyList<SerialPortOption> SerialPortOptions { get; }

    /// <summary>
    /// Same idea as <see cref="SerialPortOptions"/>, for real HID devices via <see cref="SelectedHidDevice"/> —
    /// except this one is <em>filtered</em> by whatever <see cref="VendorId"/>/<see cref="ProductId"/>
    /// currently hold: a non-zero id keeps only devices with that id, zero means "any"
    /// (<c>Where VendorId in (0, vendorId) and ProductId in (0, productId)</c>), so typing a vendor id
    /// narrows the picker to that vendor's devices. Backed by an <see cref="ObservableCollection{T}"/>
    /// (a bound WPF combobox follows it live, and keeps its selection when the selected device
    /// survives the filter) though exposed read-only; <see cref="HidDevicesHiddenByFilter"/> says
    /// whether the filter is what's making it short.
    /// </summary>
    public IReadOnlyList<HidDeviceOption> HidDeviceOptions => _hidDeviceOptions;

    /// <summary>
    /// <see langword="true"/> when some detected HID device isn't in <see cref="HidDeviceOptions"/>
    /// only because the Vendor/Product ID fields filtered it out — lets a front end say "nothing
    /// matches those ids" rather than a misleading "nothing was detected" for an empty picker.
    /// </summary>
    public bool HidDevicesHiddenByFilter => _hidDeviceOptions.Count < _detectedHidDevices.Count;

    /// <summary>Same idea as <see cref="HidDeviceOptions"/>, for real USBTMC devices via <see cref="SelectedUsbtmcDevice"/> — filtered by the same shared <see cref="VendorId"/>/<see cref="ProductId"/> fields.</summary>
    public IReadOnlyList<UsbtmcDeviceOption> UsbtmcDeviceOptions => _usbtmcDeviceOptions;

    /// <summary>Same idea as <see cref="HidDevicesHiddenByFilter"/>, for <see cref="UsbtmcDeviceOptions"/>.</summary>
    public bool UsbtmcDevicesHiddenByFilter => _usbtmcDeviceOptions.Count < _detectedUsbtmcDevices.Count;

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
            OnPropertyChanged(nameof(IsUsbtmcTransport));
            OnPropertyChanged(nameof(IsUsbDeviceTransport));
            OnPropertyChanged(nameof(IsLoopbackTransport));
            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    public bool IsSerialTransport => string.Equals(Transport, "serial", StringComparison.OrdinalIgnoreCase);

    public bool IsTcpTransport => string.Equals(Transport, "tcp", StringComparison.OrdinalIgnoreCase);

    public bool IsHidTransport => string.Equals(Transport, "hid", StringComparison.OrdinalIgnoreCase);

    public bool IsUsbtmcTransport => string.Equals(Transport, "usbtmc", StringComparison.OrdinalIgnoreCase);

    public bool IsLoopbackTransport => string.Equals(Transport, "loopback", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <see langword="true"/> when the current transport's identifying field(s) — <see cref="Port"/>
    /// for serial; <see cref="VendorId"/>/<see cref="ProductId"/>, tie-broken by <see
    /// cref="SerialNumber"/> then <see cref="DevicePath"/>, for HID/USBTMC — don't resolve to any
    /// currently-detected device. Set right after <see cref="LoadIntoFields"/> loads a saved profile
    /// whose device isn't plugged in (or, for a device with no real serial descriptor, was moved to
    /// a different USB hub/port: <see cref="DevicePath"/> is inherently tied to a physical port, not
    /// portable across ports like a real serial number would be, so that case is indistinguishable
    /// from "unplugged" here — accepted rather than worked around). A front end shows this as a
    /// "device not found" hint next to the port/device field; it never blocks
    /// <see cref="ConnectCommand"/>, which is free to fail on its own via the usual connection-error
    /// handling regardless of this flag.
    /// </summary>
    public bool ConnectedDeviceNotFound =>
        IsSerialTransport ? !string.IsNullOrEmpty(Port) && SelectedSerialPort is null
        : IsHidTransport ? HasUsbIdentity && SelectedHidDevice is null
        : IsUsbtmcTransport && HasUsbIdentity && SelectedUsbtmcDevice is null;

    private bool HasUsbIdentity => ParseFilterId(_vendorId) != 0 || ParseFilterId(_productId) != 0;

    /// <summary>
    /// <see langword="true"/> when the shared USB Vendor/Product ID field group (<see cref="VendorId"/>/
    /// <see cref="ProductId"/>, plus each transport's own device picker) should be shown — both
    /// <see cref="IsHidTransport"/> and <see cref="IsUsbtmcTransport"/> select a physical USB device
    /// the same way, so a front end shows one shared group for either instead of two parallel ones.
    /// </summary>
    public bool IsUsbDeviceTransport => IsHidTransport || IsUsbtmcTransport;

    public string Port
    {
        get => _port;
        set
        {
            SetField(ref _port, value);
            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    /// <summary>
    /// Bound to a picker (WPF's editable "Known ports" combobox; the TUI's "Detect..." button) —
    /// setting it copies the choice into <see cref="Port"/> and is otherwise not itself a tracked
    /// edit (see <see cref="_nonDirtyProperties"/>; <see cref="Port"/> changing is what actually
    /// marks the editor dirty). <see langword="null"/> doesn't clear <see cref="Port"/> — it just
    /// means nothing from the list is currently selected.
    /// </summary>
    public string? SelectedSerialPort
    {
        get => _selectedSerialPort;
        set
        {
            SetField(ref _selectedSerialPort, value);
            if (!string.IsNullOrEmpty(value))
            {
                Port = value;
            }

            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    public string Baud { get => _baud; set => SetField(ref _baud, value); }

    public string DataBits { get => _dataBits; set => SetField(ref _dataBits, value); }

    public string ParityText { get => _parityText; set => SetField(ref _parityText, value); }

    public string StopBitsText { get => _stopBitsText; set => SetField(ref _stopBitsText, value); }

    public string HandshakeText { get => _handshakeText; set => SetField(ref _handshakeText, value); }

    /// <summary>
    /// Bound to the SCPI-profile picker row, shown only when <see cref="IsScpiPresenterSelected"/> —
    /// see <see cref="ScpiProfileOptions"/> for the valid values and <see cref="CliOptions.ScpiProfile"/>
    /// for how a front end's menu handler consumes it.
    /// </summary>
    public string ScpiProfile { get => _scpiProfile; set => SetField(ref _scpiProfile, value); }

    /// <summary>
    /// Whether the "scpi" presenter is currently checked in <see cref="PresenterChoices"/> — gates a
    /// front end's SCPI-profile row the same way <see cref="IsSerialTransport"/>/etc. gate their own
    /// transport-specific field groups.
    /// </summary>
    public bool IsScpiPresenterSelected => SelectedPresenters.Contains("scpi", StringComparer.OrdinalIgnoreCase);

    public string Host { get => _host; set => SetField(ref _host, value); }

    public string TcpPort { get => _tcpPort; set => SetField(ref _tcpPort, value); }

    public bool Listen { get => _listen; set => SetField(ref _listen, value); }

    /// <summary>
    /// Always a plain decimal string — the canonical value <see cref="BuildOptions"/>/<see cref="LoadIntoFields"/>
    /// read and write, and what <see cref="SelectedHidDevice"/>/<see cref="SelectedUsbtmcDevice"/> set.
    /// Shared by the HID and USBTMC transports (both select a physical USB device the same way). A
    /// front end's text field binds to <see cref="VendorIdDisplay"/> instead, not this directly, so
    /// it can show hex without this value ever needing to be anything but decimal.
    /// </summary>
    public string VendorId
    {
        get => _vendorId;
        set
        {
            if (_vendorId == value)
            {
                return;
            }

            SetField(ref _vendorId, value);
            OnPropertyChanged(nameof(VendorIdDisplay));
            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    /// <summary>Same idea as <see cref="VendorId"/>/<see cref="VendorIdDisplay"/>.</summary>
    public string ProductId
    {
        get => _productId;
        set
        {
            if (_productId == value)
            {
                return;
            }

            SetField(ref _productId, value);
            OnPropertyChanged(nameof(ProductIdDisplay));
            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    /// <summary>
    /// <see langword="true"/> to show/accept <see cref="VendorIdDisplay"/>/<see cref="ProductIdDisplay"/>
    /// as 4-digit hex (matching <c>--listhiddevices</c>/<c>--listusbtmcdevices</c>'s own
    /// <c>"046D:C08B"</c> formatting) instead of plain decimal — a display preference only, not
    /// itself a connection field, so it doesn't mark the editor dirty and isn't saved as part of a
    /// profile (<see cref="VendorId"/>/<see cref="ProductId"/> are always decimal regardless of this).
    /// </summary>
    public bool IdsShowHex
    {
        get => _idsShowHex;
        set
        {
            if (_idsShowHex == value)
            {
                return;
            }

            SetField(ref _idsShowHex, value);
            OnPropertyChanged(nameof(VendorIdDisplay));
            OnPropertyChanged(nameof(ProductIdDisplay));
        }
    }

    /// <summary>
    /// What a front end's Vendor ID field actually binds to — decimal or 4-digit hex depending on
    /// <see cref="IdsShowHex"/>, converting to/from the canonical decimal <see cref="VendorId"/>.
    /// Setting this does <em>not</em> re-raise its own change notification (only <see cref="VendorId"/>'s
    /// does, when something else — Load, <see cref="SelectedHidDevice"/>/<see cref="SelectedUsbtmcDevice"/> —
    /// changes the canonical value): a bound WPF <c>TextBox</c> re-pulling and reformatting its own
    /// text on every keystroke would reset the caret to the end after each character typed.
    /// </summary>
    public string VendorIdDisplay
    {
        get => FormatId(_vendorId, _idsShowHex);
        set
        {
            var canonical = ParseId(value, _idsShowHex);
            if (_vendorId == canonical)
            {
                return;
            }

            SetField(ref _vendorId, canonical, nameof(VendorId));
            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    /// <summary>Same idea as <see cref="VendorIdDisplay"/>.</summary>
    public string ProductIdDisplay
    {
        get => FormatId(_productId, _idsShowHex);
        set
        {
            var canonical = ParseId(value, _idsShowHex);
            if (_productId == canonical)
            {
                return;
            }

            SetField(ref _productId, canonical, nameof(ProductId));
            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    /// <summary>
    /// Optional - blank pins the connection to "match the first device found for
    /// <see cref="VendorId"/>/<see cref="ProductId"/>" (both <c>SystemHidDevice</c>/<c>SystemUsbtmcDevice</c>'s
    /// <c>Open</c> already treat a null/empty serial number as a wildcard); set it to disambiguate
    /// when more than one device on the bench shares the same vendor/product ID. Shared by the HID
    /// and USBTMC transports, same as <see cref="VendorId"/>/<see cref="ProductId"/>.
    /// </summary>
    public string SerialNumber { get => _serialNumber; set => SetField(ref _serialNumber, value); }

    /// <summary>
    /// OS device-instance path — HID-only, unlike <see cref="SerialNumber"/> (shared with USBTMC).
    /// Auto-populated from <see cref="SelectedHidDevice"/> or a loaded profile; there's no
    /// corresponding visible control in either front end (nothing to hand-type here), so it's
    /// never directly edited by the user. Serves as the fallback disambiguator when
    /// <see cref="SerialNumber"/> is blank — see <see cref="FindBestUsbDeviceMatch{T}"/> and
    /// <c>DevTerm.Transports.Hid.SystemHidDevice.Open</c>.
    /// </summary>
    public string DevicePath { get => _devicePath; set => SetField(ref _devicePath, value); }

    // Formats/parses a canonical decimal USB vendor/product id string for display — 4-digit
    // uppercase hex (no "0x" prefix, matching --listhiddevices/--listusbtmcdevices' own "046D:C08B"
    // convention) when asHex/isHex, otherwise passed through unchanged. An unparseable value is
    // returned as-is rather than blanked out: the same "don't reject a keystroke, let validation
    // catch it later" behavior every other typed field in this view model already has (see e.g.
    // Baud/DataBits).
    private static string FormatId(string decimalText, bool asHex) =>
        asHex && int.TryParse(decimalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("X4", CultureInfo.InvariantCulture)
            : decimalText;

    private static string ParseId(string text, bool isHex) =>
        isHex
            ? int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : text
            : text;

    /// <summary>Same idea as <see cref="SelectedSerialPort"/>, for <see cref="VendorId"/>/<see cref="ProductId"/> together.</summary>
    public HidDeviceOption? SelectedHidDevice
    {
        get => _selectedHidDevice;
        set
        {
            SetField(ref _selectedHidDevice, value);
            if (value is not null)
            {
                VendorId = value.VendorId.ToString(CultureInfo.InvariantCulture);
                ProductId = value.ProductId.ToString(CultureInfo.InvariantCulture);
                SerialNumber = value.SerialNumber ?? string.Empty;
                DevicePath = value.DevicePath;
            }

            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    /// <summary>Same idea as <see cref="SelectedHidDevice"/>, for a real USBTMC device — writes into the same shared <see cref="VendorId"/>/<see cref="ProductId"/>.</summary>
    public UsbtmcDeviceOption? SelectedUsbtmcDevice
    {
        get => _selectedUsbtmcDevice;
        set
        {
            SetField(ref _selectedUsbtmcDevice, value);
            if (value is not null)
            {
                VendorId = value.VendorId.ToString(CultureInfo.InvariantCulture);
                ProductId = value.ProductId.ToString(CultureInfo.InvariantCulture);
                SerialNumber = value.SerialNumber ?? string.Empty;
                DevicePath = value.DevicePath ?? string.Empty;
            }

            OnPropertyChanged(nameof(ConnectedDeviceNotFound));
        }
    }

    /// <summary>
    /// The presenter picker: one checkable entry per <see cref="PresenterOptions"/> name, in that
    /// order. Every checked one displays incoming data (the session's pipeline fans each chunk out
    /// to all of them, each output line tagged with its presenter's name). Display only — what
    /// encodes a typed line is <see cref="Parser"/>, independent of this.
    /// </summary>
    public IReadOnlyList<PresenterSelection> PresenterChoices { get; }

    /// <summary>The send format for typed lines — see <see cref="CliOptions.Parser"/>. One of <see cref="PresenterOptions"/> (every built-in presenter can encode input).</summary>
    public string Parser { get => _parser; set => SetField(ref _parser, value); }

    /// <summary>The names of the checked <see cref="PresenterChoices"/>, in picker order.</summary>
    public IReadOnlyList<string> SelectedPresenters => [.. PresenterChoices.Where(c => c.IsSelected).Select(c => c.Name)];

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
    /// Set by each front end to show its own native conflict-resolution prompt when importing a zip
    /// (<see cref="Import"/> dispatches to zip handling by <c>.zip</c> extension) and an entry's name
    /// already matches a saved profile — called once per conflicting name. Left <see langword="null"/>,
    /// every conflict resolves to <see cref="ZipImportConflictResolution.Replace"/>, same "proceed
    /// without asking" convention as <see cref="ConfirmOverwrite"/>/<see cref="ConfirmDiscardChanges"/>.
    /// </summary>
    public Func<string, ZipImportConflictResolution>? ResolveZipImportConflict { get; set; }

    /// <summary>
    /// Set by each front end to show its own native "delete these N profiles?" confirmation before
    /// <see cref="DeleteSelectedProfilesCommand"/> removes anything, given the names about to go.
    /// Deleting several profiles at once is the one destructive action here that can't be undone
    /// and touches more than the one row the user is looking at, so unlike the single-profile
    /// <see cref="DeleteCommand"/> it asks first. Left <see langword="null"/>, it proceeds without
    /// asking, same convention as <see cref="ConfirmOverwrite"/>/<see cref="ConfirmDiscardChanges"/>.
    /// </summary>
    public Func<IReadOnlyList<string>, bool>? ConfirmDeleteProfiles { get; set; }

    /// <summary>
    /// Set by each front end to show its own native "delete all N saved profiles and import the M in
    /// the zip?" confirmation before <see cref="ReplaceAllFromZipCommand"/> removes anything, given
    /// (saved profile count, zip profile count). Not asked when nothing is saved yet (there's
    /// nothing to lose). Left <see langword="null"/>, it proceeds without asking, same convention as
    /// <see cref="ConfirmDeleteProfiles"/>.
    /// </summary>
    public Func<int, int, bool>? ConfirmReplaceAllProfiles { get; set; }

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
        _loadedOptions = options;
        Transport = options.Transport;
        Port = options.Port ?? string.Empty;
        SelectedSerialPort = SerialPortOptions.FirstOrDefault(p => string.Equals(p.Name, Port, StringComparison.Ordinal))?.Name;
        Baud = options.Baud.ToString();
        DataBits = options.DataBits.ToString();
        ParityText = options.Parity.ToString();
        StopBitsText = options.StopBits.ToString();
        HandshakeText = options.Handshake.ToString();
        ScpiProfile = options.ScpiProfile ?? string.Empty;
        Host = options.Host ?? string.Empty;
        TcpPort = options.Port ?? "0";
        Listen = options.Listen;
        VendorId = options.VendorId.ToString();
        ProductId = options.ProductId.ToString();
        SerialNumber = options.SerialNumber ?? string.Empty;
        DevicePath = options.DevicePath ?? string.Empty;

        // Bypasses SelectedHidDevice/SelectedUsbtmcDevice's own setters (SetField directly) —
        // those setters push VendorId/ProductId/SerialNumber/DevicePath from whichever device gets
        // picked, and the fields were *just* set from options above; going through the setter risks
        // a best-effort match (see FindBestUsbDeviceMatch) silently overwriting an exact loaded
        // SerialNumber/DevicePath with a different live device's identity when no live device
        // actually matches the one that was saved.
        SetField(
            ref _selectedHidDevice,
            FindBestUsbDeviceMatch(_detectedHidDevices, options.VendorId, options.ProductId, options.SerialNumber, options.DevicePath, d => d.VendorId, d => d.ProductId, d => d.SerialNumber, d => d.DevicePath),
            nameof(SelectedHidDevice));
        SetField(
            ref _selectedUsbtmcDevice,
            FindBestUsbDeviceMatch(_detectedUsbtmcDevices, options.VendorId, options.ProductId, options.SerialNumber, options.DevicePath, d => d.VendorId, d => d.ProductId, d => d.SerialNumber, d => d.DevicePath),
            nameof(SelectedUsbtmcDevice));
        OnPropertyChanged(nameof(ConnectedDeviceNotFound));

        var presenters = options.EffectivePresenters;
        foreach (var choice in PresenterChoices)
        {
            choice.IsSelected = presenters.Contains(choice.Name, StringComparer.OrdinalIgnoreCase);
        }

        Parser = options.EffectiveParser;
        LineEndingText = options.LineEnding.ToString();
        Description = options.Description ?? string.Empty;
    }

    public CliOptions BuildOptions()
    {
        var options = new CliOptions
        {
            Transport = Transport.Trim(),
            Port = IsTcpTransport
                ? (TcpPort.Trim() is { Length: > 0 } tp ? tp : null)
                : (Port.Trim() is { Length: > 0 } p ? p : null),
            Host = Host.Trim() is { Length: > 0 } h ? h : null,
            Listen = Listen,
            Presenter = [.. OrderLikeLoaded(SelectedPresenters, _loadedOptions.EffectivePresenters)],

            // Settings this form doesn't expose, carried over from whatever was loaded rather than
            // reset to CliOptions' defaults. Without this, loading a saved profile and pressing
            // Connect silently changed the connection (a profile saved with DTR off came back with
            // it on) and the result no longer matched the saved profile, which is how the window
            // title identifies it - so the title lost the profile's name.
            Dtr = _loadedOptions.Dtr,
            Rts = _loadedOptions.Rts,
            WriteTimeoutMs = _loadedOptions.WriteTimeoutMs,
            ReadTimeoutMs = _loadedOptions.ReadTimeoutMs,
            AsciiMaxLineLength = _loadedOptions.AsciiMaxLineLength,
            ManifestName = _loadedOptions.ManifestName,
            Parser = Parser.Trim() is { Length: > 0 } parser ? parser : CliOptions.DefaultPresenter,
            Description = Description.Trim() is { Length: > 0 } d ? d : null,
            ScpiProfile = ScpiProfile.Trim() is { Length: > 0 } sp ? sp : null,
            SerialNumber = SerialNumber.Trim() is { Length: > 0 } sn ? sn : null,
            DevicePath = DevicePath.Trim() is { Length: > 0 } dp ? dp : null,
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

        if (Enum.TryParse<Handshake>(HandshakeText, ignoreCase: true, out var handshake))
        {
            options.Handshake = handshake;
        }

        if (int.TryParse(VendorId, out var vendorId))
        {
            options.VendorId = vendorId;
        }

        if (int.TryParse(ProductId, out var productId))
        {
            options.ProductId = productId;
        }

        if (Enum.TryParse<LineEnding>(LineEndingText, ignoreCase: true, out var lineEnding))
        {
            options.LineEnding = lineEnding;
        }

        return options;
    }

    // An untouched selection keeps the loaded profile's own presenter order (the picker lists
    // presenters in a fixed order, which can differ), so load-then-connect round-trips exactly; once
    // the user changes which presenters are ticked, the picker's order applies.
    private static IReadOnlyList<string> OrderLikeLoaded(IReadOnlyList<string> selected, IReadOnlyList<string> loaded) =>
        selected.Count == loaded.Count && selected.All(name => loaded.Contains(name, StringComparer.OrdinalIgnoreCase))
            ? loaded
            : selected;

    /// <summary><see cref="CliOptionsValidator"/> plus the one rule only this editor can break: an empty presenter picker (a bound <see cref="CliOptions"/> with no <c>Presenter</c> means "the default", so it can't tell).</summary>
    private ValidateOptionsResult ValidateFields(CliOptions options) =>
        SelectedPresenters.Count == 0
            ? ValidateOptionsResult.Fail("Select at least one presenter.")
            : _validator.Validate(null, options);

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

    private void DeleteMarkedProfiles()
    {
        if (SelectedProfileNames.Count == 0)
        {
            StatusMessage = "Select one or more saved profiles to delete first.";
            return;
        }

        // A copy: refreshing Profiles below makes the front end's list control fire its own
        // selection-changed handler, which mutates SelectedProfileNames out from under a live loop.
        var names = SelectedProfileNames.ToList();
        if (ConfirmDeleteProfiles?.Invoke(names) == false)
        {
            StatusMessage = "Delete cancelled.";
            return;
        }

        var deleted = names.Count(_store.Delete);
        if (SelectedProfileName is { } current && names.Contains(current))
        {
            SelectedProfileName = null;
        }

        RefreshProfiles();
        SelectedProfileNames.Clear();
        StatusMessage = $"Deleted {deleted} profile(s)."
            + (deleted < names.Count ? $" {names.Count - deleted} not found." : string.Empty);
    }

    private void Connect()
    {
        var options = BuildOptions();
        var validation = ValidateFields(options);
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
        var validation = ValidateFields(options);
        if (validation.Failed)
        {
            StatusMessage = string.Join(" ", validation.Failures);
            return;
        }

        _store.Save(name, options);
        RefreshProfiles();
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

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ImportZip(path);
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

    private void ReplaceAllFromZip()
    {
        var path = ImportExportPath.Trim();
        if (path.Length == 0)
        {
            StatusMessage = "Type a file path to import from.";
            return;
        }

        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Replace All needs a .zip file (one exported with Export Selected/Export All).";
            return;
        }

        // Read and check the whole archive before anything is deleted - a bad zip must never cost
        // the user their saved profiles.
        IReadOnlyList<ZipProfile> incoming;
        try
        {
            incoming = ConnectionProfileStore.ReadZip(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not import '{path}': {ex.Message} Nothing was deleted.";
            return;
        }

        if (incoming.Count == 0)
        {
            StatusMessage = $"'{path}' contains no profiles. Nothing was deleted.";
            return;
        }

        if (Profiles.Count > 0 && ConfirmReplaceAllProfiles?.Invoke(Profiles.Count, incoming.Count) == false)
        {
            StatusMessage = "Replace All cancelled.";
            return;
        }

        try
        {
            var removed = _store.ReplaceAll(incoming);
            SelectedProfileName = null;
            SelectedProfileNames.Clear();
            RefreshProfiles();
            StatusMessage = $"Replaced {removed} saved profile(s) with {incoming.Count} from '{path}'.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RefreshProfiles();
            StatusMessage = $"Replace All failed partway: {ex.Message}";
        }
    }

    // A zip holds several named profiles at once (see ExportProfilesZip) rather than one profile's
    // fields to review - so, unlike a single-file Import above, this saves straight into the store
    // and refreshes the list instead of loading into the on-screen fields.
    private void ImportZip(string path)
    {
        try
        {
            var result = _store.ImportZip(path, ResolveZipImportConflict);
            RefreshProfiles();
            StatusMessage = $"Imported {result.Imported} profile(s) from '{path}'"
                + (result.Renamed > 0 ? $", renamed {result.Renamed}" : string.Empty)
                + (result.Skipped > 0 ? $", skipped {result.Skipped}" : string.Empty)
                + ".";
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
        var validation = ValidateFields(options);
        if (validation.Failed)
        {
            StatusMessage = string.Join(" ", validation.Failures);
            return;
        }

        ConnectionProfileStore.ExportToFile(path, options);
        StatusMessage = $"Exported to '{path}'.";
    }

    private void ExportProfilesZip(IReadOnlyCollection<string> names)
    {
        var path = ImportExportPath.Trim();
        if (path.Length == 0)
        {
            StatusMessage = "Type a file path to export to.";
            return;
        }

        if (names.Count == 0)
        {
            StatusMessage = "Select one or more saved profiles to export first.";
            return;
        }

        try
        {
            _store.ExportZip(path, names);
            StatusMessage = $"Exported {names.Count} profile(s) to '{path}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not export to '{path}': {ex.Message}";
        }
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

    // Re-derives the picker list from everything detected: a non-zero id must match, zero (or
    // anything not a number yet, mid-typing) means "any". Edits the ObservableCollection in place
    // (remove what no longer matches, then insert what now does, in detection order) instead of
    // replacing it, so a bound combobox keeps a still-matching selection rather than being reset.
    private void RefreshHidDeviceOptions() =>
        RefreshList(_vendorId, _productId, _detectedHidDevices, _hidDeviceOptions);

    // Same filtering as RefreshHidDeviceOptions, over the USBTMC discovery list instead — see
    // UsbtmcDeviceOptions' doc comment.
    private void RefreshUsbtmcDeviceOptions() =>
        RefreshList(_vendorId, _productId, _detectedUsbtmcDevices, _usbtmcDeviceOptions);
    private static int ParseFilterId(string decimalText) =>
        int.TryParse(decimalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    private static void RefreshList<T>(string vendorValue, string productValue, IReadOnlyList<T> detected, ObservableCollection<T> set)
        where T : IUsbDeviceOption
    {
        var vendorId = ParseFilterId(vendorValue);
        var productId = ParseFilterId(productValue);

        var wanted = detected
            .Where(d => (vendorId == 0 || d.VendorId == vendorId) && (productId == 0 || d.ProductId == productId))
            .ToList();

        var existing = set.ToList();

        var equality = UsbDeviceOptionEqualityComparer<T>.Default;
        var remove = existing.Except(wanted, equality).ToList();
        var add = wanted.Except(existing, equality).ToList();

        foreach (var item in remove)
        {
            set.Remove(item);
        }

        var comparer = UsbDeviceOptionComparer.Default;
        foreach (var item in add)
        {
            var index = 0;
            while (index < set.Count && comparer.Compare(set[index], item) < 0)
            {
                index++;
            }
            set.Insert(index, item);
        }
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        if (propertyName is nameof(VendorId) or nameof(ProductId))
        {
            RefreshHidDeviceOptions();
            RefreshUsbtmcDeviceOptions();
        }

        if (propertyName is not null && !_nonDirtyProperties.Contains(propertyName))
        {
            IsDirty = true;
        }
    }
}
