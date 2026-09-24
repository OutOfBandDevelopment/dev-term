using System.ComponentModel;
using System.IO.Ports;

namespace DevTerm.Configuration;

/// <summary>
/// A front end's connection settings, bound from configuration (command-line args, environment
/// variables, and JSON settings files layered via the standard
/// <c>Microsoft.Extensions.Configuration</c> extensions — see <see cref="DevTermConfiguration"/>)
/// via the Options pattern rather than a hand-rolled parser. Property names double as the
/// (case-insensitive) argument/setting names: <c>--transport tcp --port 502</c>. Shared by
/// every front end (console CLI/TUI, WPF) so the same saved profile works from any of them.
/// </summary>
/// <remarks>
/// Properties carry <see cref="CategoryAttribute"/>/<see cref="DisplayNameAttribute"/> from
/// <c>System.ComponentModel</c> — plain metadata, not tied to any particular UI framework — so a
/// property's group ("Serial"/"TCP"/"USB HID"/"Presentation"/"Mode") is declared once, here, rather
/// than re-decided independently by each front end's editor. <see cref="ConnectionEditorViewModel"/>
/// doesn't read these back via reflection today (its own <c>IsSerialTransport</c>/etc. properties
/// group fields for show/hide instead) — this is the metadata layer such a reflection-driven
/// approach would consume if the editor grows one later, and documents the grouping either way.
/// </remarks>
public sealed class CliOptions
{
    [Category("General")]
    [DisplayName("Transport")]
    [Description("Which transport to use: serial, tcp, hid, usbtmc, or loopback.")]
    public string Transport { get; set; } = "serial";

    /// <summary>A free-text note about this connection/profile — purely descriptive, never read by any transport or validated.</summary>
    [Category("General")]
    [DisplayName("Description")]
    public string? Description { get; set; }

    /// <summary>List available serial ports and exit, skipping normal validation/connection entirely.</summary>
    [Category("Mode")]
    public bool ListPorts { get; set; }

    /// <summary>
    /// Run the console app's full-screen TUI. Defaults to <c>true</c> — the TUI is the console
    /// app's default mode; pass <see cref="Cli"/> to force the plain scriptable loop instead. See
    /// docs/design/frontends.md.
    /// </summary>
    [Category("Mode")]
    public bool Tui { get; set; } = true;

    /// <summary>Force the plain scriptable CLI loop instead of the default full-screen TUI — e.g. for automation/CI. See docs/design/frontends.md.</summary>
    [Category("Mode")]
    public bool Cli { get; set; }

    /// <summary>The name used when <see cref="Presenter"/> is empty/unset.</summary>
    public const string DefaultPresenter = "hex";

    /// <summary>
    /// Which presenters render incoming bytes — one or more of ascii, utf8, hex, decimal, octal,
    /// binary, all shown side by side (each output line is tagged with its presenter's name). A JSON
    /// array in a profile (<c>"Presenter": ["ascii", "hex"]</c>); on the command line/environment,
    /// a single comma-separated value (<c>--presenter ascii,hex</c>) — see
    /// <see cref="DevTermConfiguration.Bind"/>, which also still reads the older single-string form
    /// (<c>"Presenter": "hex"</c>) that profiles saved before this became a list use. Empty by
    /// default (not <c>["hex"]</c>): the configuration binder appends bound array items to an
    /// existing default array, so a non-empty default would leak into every bound profile — read
    /// <see cref="EffectivePresenters"/> for the resolved list.
    /// </summary>
    [Category("Presentation")]
    [DisplayName("Presenter")]
    [Description("How incoming bytes are rendered: any of ascii, utf8, hex, decimal, octal, binary — comma-separated to show several.")]
    public string[] Presenter { get; set; } = [];

    /// <summary><see cref="Presenter"/> with the <see cref="DefaultPresenter"/> fallback applied when empty, blanks removed, duplicates (case-insensitive) collapsed.</summary>
    [Browsable(false)]
    public IReadOnlyList<string> EffectivePresenters =>
        Presenter.Select(p => p?.Trim() ?? string.Empty).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() is { Length: > 0 } names
            ? names
            : [DefaultPresenter];

    /// <summary>
    /// The send format: which presenter's input encoding turns a typed line into bytes (ascii text,
    /// hex digits, decimal numbers, ...). Independent of <see cref="Presenter"/>, which only picks
    /// what's displayed. Unset (older profiles) means the first of <see cref="EffectivePresenters"/>,
    /// which is what sent input was always encoded with before the two were separated. A front end
    /// can switch it per typed line during a session; this is only the starting value.
    /// </summary>
    [Category("Presentation")]
    [DisplayName("Parser")]
    [Description("How a typed line is turned into bytes to send: ascii, utf8, hex, decimal, octal, or binary. Defaults to the first presenter.")]
    public string? Parser { get; set; }

    /// <summary><see cref="Parser"/> with the older-profile fallback applied (the first of <see cref="EffectivePresenters"/>).</summary>
    [Browsable(false)]
    public string EffectiveParser => Parser is { Length: > 0 } parser ? parser : EffectivePresenters[0];

    /// <summary>Appended to each typed line before sending, for presenters that support sending. See <see cref="LineEnding"/>.</summary>
    [Category("Presentation")]
    [DisplayName("Line ending")]
    public LineEnding LineEnding { get; set; } = LineEnding.None;

    /// <summary>See <see cref="DevTerm.Presenters.Text.AsciiPresenterOptions.MaxLineLength"/>. 0 means unbounded (wait for a line terminator only).</summary>
    [Category("Presentation")]
    [DisplayName("ASCII max line length")]
    public int AsciiMaxLineLength { get; set; } = DevTerm.Presenters.Text.AsciiPresenter.DefaultMaxLineLength;

    /// <summary>Serial COM port name (e.g. "COM3"), or the TCP port number as a string when <see cref="Transport"/> is "tcp" — shared under one flag/property so both transports use "--port".</summary>
    [Category("Serial")]
    [DisplayName("Port")]
    public string? Port { get; set; }

    [Category("Serial")]
    [DisplayName("Baud")]
    public int Baud { get; set; } = 9600;

    [Category("Serial")]
    [DisplayName("Data bits")]
    public int DataBits { get; set; } = 8;

    [Category("Serial")]
    [DisplayName("Parity")]
    public Parity Parity { get; set; } = Parity.None;

    [Category("Serial")]
    [DisplayName("Stop bits")]
    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>Flow control; hardware (RTS/CTS) flow control is off by default like most serial APIs — set to <see cref="Handshake.RequestToSend"/> to enable it.</summary>
    [Category("Serial")]
    [DisplayName("Handshake")]
    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>Bounds a blocked write (e.g. hardware flow control on but the device never asserts CTS) instead of hanging forever; -1 waits indefinitely.</summary>
    [Category("Serial")]
    [DisplayName("Write timeout (ms)")]
    public int WriteTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Milliseconds a read blocks before timing out. Kept finite by default: SerialPort's
    /// BaseStream doesn't reliably honor cancellation on an in-flight read on all drivers, so a
    /// periodic timeout is how Close/Ctrl+C notice they should stop instead of hanging.
    /// </summary>
    [Category("Serial")]
    [DisplayName("Read timeout (ms)")]
    public int ReadTimeoutMs { get; set; } = 1000;

    /// <summary>Assert DTR on open; many devices treat it as a "terminal present" signal and stay silent without it. See <see cref="SerialTransportOptions.DtrEnable"/>.</summary>
    [Category("Serial")]
    [DisplayName("DTR")]
    public bool Dtr { get; set; } = true;

    /// <summary>Assert RTS on open (ignored when <see cref="Handshake"/> already manages RTS). See <see cref="SerialTransportOptions.RtsEnable"/>.</summary>
    [Category("Serial")]
    [DisplayName("RTS")]
    public bool Rts { get; set; } = true;

    // TCP transport.
    [Category("TCP")]
    [DisplayName("Host")]
    public string? Host { get; set; }

    [Category("TCP")]
    [DisplayName("Listen (server mode)")]
    public bool Listen { get; set; }

    // USB HID and USBTMC transports share the same "vendor/product/serial" identity fields below —
    // both select a physical USB device the same way, so a saved profile's Vendor/Product ID
    // carries over if you switch Transport between "hid" and "usbtmc" instead of needing two
    // parallel, near-identical sets of fields.

    /// <summary>USB Vendor ID, decimal (Device Manager shows hex, e.g. "VID_1915" is 6421 decimal). Used by both the HID and USBTMC transports.</summary>
    [Category("USB Device")]
    [DisplayName("Vendor ID")]
    public int VendorId { get; set; }

    /// <summary>USB Product ID, decimal (Device Manager shows hex, e.g. "PID_AFDA" is 45018 decimal). Used by both the HID and USBTMC transports.</summary>
    [Category("USB Device")]
    [DisplayName("Product ID")]
    public int ProductId { get; set; }

    /// <summary>Disambiguates when more than one connected device matches <see cref="VendorId"/>/<see cref="ProductId"/>. Used by both the HID and USBTMC transports.</summary>
    [Category("USB Device")]
    [DisplayName("Serial number")]
    public string? SerialNumber { get; set; }

    /// <summary>List available USB HID devices and exit, skipping normal validation/connection entirely.</summary>
    [Category("Mode")]
    public bool ListHidDevices { get; set; }

    /// <summary>List attached USBTMC-class USB devices and exit, skipping normal validation/connection entirely.</summary>
    [Category("Mode")]
    public bool ListUsbtmcDevices { get; set; }

    /// <summary>
    /// Names a device manifest to load alongside this connection — <b>a name, not a path</b>;
    /// resolves to <c>~/.dev-term/manifests/{ManifestName}</c> or this app's own
    /// <c>./manifests/{ManifestName}</c> (see <see cref="DevTermUserDataPaths.ResolveManifestDirectory"/>),
    /// so a saved profile stays portable instead of embedding a filesystem path. A name that
    /// doesn't resolve is a warning, not a connection failure — see docs/design/connection-profiles.md.
    /// </summary>
    [Category("General")]
    [DisplayName("Device manifest")]
    public string? ManifestName { get; set; }

    /// <summary>
    /// Names an entry from <c>DevTerm.Devices.Scpi.ScpiProfileCatalog.All</c> (or its
    /// <c>Generic.Name</c>/<c>AutoDetectChoiceName</c> synthetic choices) to preselect when the "SCPI
    /// Instrument..." menu item opens, so a saved connection doesn't need the picker re-run every
    /// time. Null/unrecognized falls back to today's picker-always-shown behavior. Only meaningful
    /// when <see cref="Presenter"/> includes <c>"scpi"</c>.
    /// </summary>
    [Category("Presentation")]
    [DisplayName("SCPI profile")]
    public string? ScpiProfile { get; set; }

    /// <summary>
    /// Directory where auto-saved captures (e.g. the Stream Monitor's detected binary/image data —
    /// see docs/design/proposals/stream-content-detection.md) are written. Unset means
    /// <see cref="DevTermUserDataPaths.ExportsDirectory"/> (<c>~/.dev-term/exports</c>) — see
    /// <see cref="EffectiveExportDirectory"/>. Bound the same as every other property here (a
    /// settings file, the <c>DEVTERM_</c>-prefixed environment variable, or <c>--exportdirectory</c>
    /// on the command line) via the standard layering in <see cref="DevTermConfiguration"/>, not a
    /// hand-rolled path/env lookup.
    /// </summary>
    [Category("General")]
    [DisplayName("Export directory")]
    public string? ExportDirectory { get; set; }

    /// <summary><see cref="ExportDirectory"/> with the <see cref="DevTermUserDataPaths.ExportsDirectory"/> default applied.</summary>
    [Browsable(false)]
    public string EffectiveExportDirectory => ExportDirectory is { Length: > 0 } dir ? dir : DevTermUserDataPaths.ExportsDirectory;
}
