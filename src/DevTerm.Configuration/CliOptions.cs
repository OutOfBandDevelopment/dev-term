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
/// than re-decided independently by each front end's editor. They're exactly what
/// <c>DevTerm.UiDefinitions.Forms.FormDefinitionGenerator</c> reads: <c>Generate&lt;CliOptions&gt;()</c>
/// yields a form section per category. The Connection Editor's own form is generated from
/// <see cref="ConnectionEditorViewModel"/> instead (its editable, string-typed properties carry the
/// same categories and labels), since that's the model its fields bind to.
/// </remarks>
public sealed class CliOptions
{
    [Category("General")]
    [DisplayName("Transport")]
    [Description("Which transport to use: serial, tcp, hid, usbtmc, ble, rfc2217, mqtt, amqp, stomp, or loopback.")]
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
    /// Milliseconds paced between each byte written to the transport - for a slow device with no
    /// FIFO buffer that can't absorb a burst write (bytes get dropped or corrupted when a whole
    /// line/packet arrives faster than the device can consume it). -1 (the default) disables pacing
    /// entirely - the buffer is written as a single, unpaced call exactly as before this feature
    /// existed. 0 still writes/flushes one byte at a time with no delay between them; a positive
    /// value additionally delays that long between bytes. Supported by the serial, TCP, and RFC 2217
    /// transports only - HID/USBTMC/BLE write one atomic report/message per call rather than a
    /// continuous byte stream, so inter-byte pacing doesn't apply the same way. Write-only; has no
    /// effect on the read side of a connection. See <see cref="DevTerm.Core.Transports.WriteDelayStream"/>.
    /// </summary>
    [Category("Timing")]
    [DisplayName("Write byte delay (ms)")]
    public int WriteByteDelayMs { get; set; } = -1;

    /// <summary>Minimum milliseconds between two sends, enforced by the session for any transport; 0 (the default) disables it. See <see cref="DevTerm.Core.Sessions.SessionLimits"/>.</summary>
    [Category("Timing")]
    [DisplayName("Min send interval (ms)")]
    public int SendIntervalMs { get; set; }

    /// <summary>Minimum milliseconds between handling two received chunks; 0 (the default) disables it. Nothing is dropped; the backlog waits in the transport's pipe.</summary>
    [Category("Timing")]
    [DisplayName("Min read interval (ms)")]
    public int ReadIntervalMs { get; set; }

    /// <summary>Milliseconds one connect attempt may take before it is abandoned; 0 (the default) leaves it to the transport.</summary>
    [Category("Timing")]
    [DisplayName("Connect timeout (ms)")]
    public int ConnectTimeoutMs { get; set; }

    /// <summary>Extra connect attempts after the first fails; 0 (the default) is a single attempt.</summary>
    [Category("Timing")]
    [DisplayName("Connect retries")]
    public int ConnectRetries { get; set; }

    /// <summary>Milliseconds between connect attempts.</summary>
    [Category("Timing")]
    [DisplayName("Connect retry delay (ms)")]
    public int ConnectRetryDelayMs { get; set; } = 1000;

    /// <summary>The session limits these options describe.</summary>
    public DevTerm.Core.Sessions.SessionLimits SessionLimits => new()
    {
        MinSendIntervalMs = SendIntervalMs,
        MinReadIntervalMs = ReadIntervalMs,
        ConnectTimeoutMs = ConnectTimeoutMs,
        ConnectRetries = ConnectRetries,
        ConnectRetryDelayMs = ConnectRetryDelayMs,
    };

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

    /// <summary>
    /// Milliseconds paced between each pushed line of a naturally multi-line/streaming scripted
    /// loopback response (<c>Samples: N</c>, <c>Send Events: N</c>) — lets a loopback profile
    /// simulate a device that streams samples at a real rate instead of delivering them all
    /// instantly. 0 (the default) preserves the original instant-delivery behavior. Loopback
    /// transport only. See <see cref="DevTerm.Transports.Loopback.LoopbackTransportOptions.SampleIntervalMs"/>.
    /// </summary>
    [Category("Loopback")]
    [DisplayName("Sample interval (ms)")]
    public int LoopbackSampleIntervalMs { get; set; }

    // TCP transport.
    [Category("TCP")]
    [DisplayName("Host")]
    public string? Host { get; set; }

    [Category("TCP")]
    [DisplayName("Listen (server mode)")]
    public bool Listen { get; set; }

    // MQTT transport (also uses Host/Port above).
    [Category("MQTT")]
    [DisplayName("Subscribe topics")]
    [Description("Comma-separated MQTT topic filters to subscribe to (wildcards allowed).")]
    public string? Subscribe { get; set; }

    [Category("MQTT")]
    [DisplayName("Publish topic")]
    [Description("Where a typed line is published unless it is written as topic<TAB>payload.")]
    public string? Publish { get; set; }

    /// <summary>AMQP and STOMP only: encrypt the broker connection.</summary>
    [Category("MQTT")]
    [DisplayName("Use TLS")]
    public bool Tls { get; set; }

    /// <summary>AMQP and STOMP only: an extra CA certificate file to trust for the broker's TLS certificate.</summary>
    [Category("MQTT")]
    [DisplayName("CA certificate")]
    public string? CaCertificate { get; set; }

    [Category("MQTT")]
    [DisplayName("User name")]
    public string? Username { get; set; }

    /// <summary>Command line or environment only: never written to a saved profile or shown in the editor.</summary>
    [Browsable(false)]
    public string? Password { get; set; }

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

    /// <summary>OS device-instance path (e.g. "\\?\hid#vid_10cf&amp;pid_5500#...#{guid}"), tied to a
    /// physical USB hub/port rather than the device itself. HID-only — a fallback for disambiguating
    /// devices with no real <see cref="SerialNumber"/> (e.g. a Velleman K8055). Preferred match order
    /// is <see cref="SerialNumber"/> first (when non-blank), then this.</summary>
    [Category("USB Device")]
    [DisplayName("Device path")]
    public string? DevicePath { get; set; }

    /// <summary>List available USB HID devices and exit, skipping normal validation/connection entirely.</summary>
    [Category("Mode")]
    public bool ListHidDevices { get; set; }

    /// <summary>List attached USBTMC-class USB devices and exit, skipping normal validation/connection entirely.</summary>
    [Category("Mode")]
    public bool ListUsbtmcDevices { get; set; }

    // BLE transport.

    /// <summary>
    /// The peripheral to connect to, in whatever opaque form the active platform BLE backend's own
    /// discovery produced (see <c>DevTerm.Transports.Ble.BleDeviceDescriptor.DeviceId</c>) — a WinRT
    /// device id on Windows, for example. Not a Bluetooth address a user would type by hand.
    /// </summary>
    [Category("BLE")]
    [DisplayName("Device")]
    public string? BleDeviceId { get; set; }

    /// <summary>GATT service UUID. Blank means the Nordic UART Service default — see <c>DevTerm.Transports.Ble.NordicUartService</c>.</summary>
    [Category("BLE")]
    [DisplayName("Service UUID")]
    public string? BleServiceUuid { get; set; }

    /// <summary>Characteristic written to for host-to-device bytes. Blank means the NUS RX characteristic default.</summary>
    [Category("BLE")]
    [DisplayName("Write characteristic UUID")]
    public string? BleWriteCharacteristicUuid { get; set; }

    /// <summary>Characteristic subscribed to for device-to-host bytes. Blank means the NUS TX characteristic default.</summary>
    [Category("BLE")]
    [DisplayName("Notify characteristic UUID")]
    public string? BleNotifyCharacteristicUuid { get; set; }

    /// <summary>Scan the LAN for LXI/VXI-11 instruments, print each with its raw SCPI port and <c>*IDN?</c>, and exit.</summary>
    [Category("Mode")]
    public bool ListLxiDevices { get; set; }

    /// <summary>List every plugin folder found (loaded or skipped, with why) and exit.</summary>
    [Category("Mode")]
    public bool ListPlugins { get; set; }

    /// <summary>List paired BLE devices and exit, skipping normal validation/connection entirely.</summary>
    [Category("Mode")]
    public bool ListBleDevices { get; set; }

    /// <summary>
    /// List a specific BLE device's GATT services/characteristics (by the same device id
    /// <see cref="ListBleDevices"/> prints) and exit, skipping normal validation/connection entirely.
    /// </summary>
    [Category("Mode")]
    public string? ListBleCharacteristics { get; set; }

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

    public const int DefaultScpiAutoDetectTimeoutMs = 3000;

    /// <summary>
    /// How long SCPI auto-detect waits for the <c>*IDN?</c> reply before falling back to the Generic
    /// panel (milliseconds, 100-60000). A slow instrument, or one that needs a moment after being put
    /// into remote, can need longer than the 3 s default.
    /// </summary>
    [Category("Presentation")]
    [DisplayName("SCPI auto-detect timeout (ms)")]
    public int ScpiAutoDetectTimeoutMs { get; set; } = DefaultScpiAutoDetectTimeoutMs;

    /// <summary>
    /// Directory where auto-saved captures (e.g. the Stream Monitor's detected binary/image data —
    /// see docs/design/features/stream-content-detection.md) are written. Unset means
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

    /// <summary>
    /// Logger mode: record every sent/received chunk and connect/disconnect event of the session to
    /// this session-log file from startup (<c>--log capture.jsonl</c>). <c>--log true</c> picks a
    /// timestamped name under <see cref="DevTermUserDataPaths.LogsDirectory"/> instead (see
    /// <see cref="SessionLogging.ResolveLogPath"/>). Honored by every front end; the TUI/WPF can also
    /// start and stop logging from File &gt; Start Logging.... See docs/design/session-logging.md.
    /// </summary>
    [Category("Mode")]
    [DisplayName("Log to file")]
    public string? Log { get; set; }

    /// <summary>
    /// Folder of plugins (<c>&lt;folder&gt;/&lt;plugin&gt;/plugin.json</c> + assemblies) loaded at startup. Defaults to a <c>plugins</c>
    /// folder next to the app when that exists. See docs/design/plugin-model.md.
    /// </summary>
    [Category("Mode")]
    [DisplayName("Plugins folder")]
    public string? Plugins { get; set; }

    /// <summary>
    /// Opt-in OpenTelemetry export of dev-term's own traces and metrics (connection open spans, bytes
    /// sent/received, disconnects) over OTLP/gRPC: <c>--otlp http://localhost:4317</c>, or <c>--otlp true</c> for that
    /// default. Off when unset. See docs/design/observability.md.
    /// </summary>
    [Category("Mode")]
    [DisplayName("OTLP endpoint")]
    public string? Otlp { get; set; }

    /// <summary>
    /// Plays a session log back through the presenters (<c>--presenter</c>, or the log's own) and
    /// prints the decoded output, then exits — no connection is made. Console app only; the TUI/WPF
    /// have File &gt; Open Log for Playback... instead.
    /// </summary>
    [Category("Mode")]
    public string? Playback { get; set; }

    /// <summary>
    /// <see cref="Playback"/>'s speed relative to how it was captured: 1 is realtime, 0.5 half speed,
    /// 10 ten times faster; 0 (the default) prints everything as fast as possible.
    /// </summary>
    [Category("Mode")]
    public double PlaybackSpeed { get; set; }

    /// <summary>
    /// Which mechanism the Stream Monitor's "Convert..." action uses, from
    /// docs/design/features/stream-content-detection.md's "Raster/convert tool integration":
    /// <c>none</c> (default - the action reports nothing is configured), <c>externaltool</c> (run a
    /// configured external converter, e.g. Ghostscript), <c>auto</c> / <c>tool:&lt;name&gt;</c> (a registered
    /// converter tool), or <c>internalhpgltosvg</c> (dev-term's own HP-GL-to-SVG converter -
    /// HP-GL captures only). See <see cref="DevTerm.Configuration.StreamCaptureConverter"/>.
    /// </summary>
    [Category("Stream Monitor")]
    [DisplayName("Convert mode")]
    [Description("How Stream Monitor's Convert action works: none, externaltool, auto, tool:<name>, or internalhpgltosvg.")]
    public string? StreamConvertMode { get; set; }

    /// <summary>The external converter executable (e.g. Ghostscript's <c>gswin64c.exe</c>) run when <see cref="StreamConvertMode"/> is <c>externaltool</c>.</summary>
    [Category("Stream Monitor")]
    [DisplayName("External tool path")]
    public string? StreamConvertExternalToolPath { get; set; }

    /// <summary>
    /// The external tool's argument template, e.g. <c>-sDEVICE=png16m -r{dpi} -o{output} {input}</c>.
    /// Split on whitespace and substituted per-token (<c>{input}</c>, <c>{output}</c>, <c>{dpi}</c>) -
    /// never built into a single shell string, so a substituted path can never be interpreted as
    /// another argument or a shell metacharacter.
    /// </summary>
    [Category("Stream Monitor")]
    [DisplayName("External tool arguments")]
    [Description("Argument template; {input}, {output}, {dpi} are substituted per-token, never shell-expanded.")]
    public string? StreamConvertExternalToolArguments { get; set; }

    /// <summary>The DPI value substituted for <c>{dpi}</c> in <see cref="StreamConvertExternalToolArguments"/>.</summary>
    [Category("Stream Monitor")]
    [DisplayName("External tool DPI")]
    public int StreamConvertDpi { get; set; } = 150;

    /// <summary>
    /// How long (ms) the Stream Monitor waits with no bytes before a capture that has no in-band end (HP-GL, TIFF)
    /// is considered finished and saved. The wait restarts on every byte, so only a pause this long splits a
    /// capture in two; raise it for a slow link (a 4800 baud scope can stall mid-plot for longer than the 2000 default).
    /// </summary>
    [Category("Stream Monitor")]
    [DisplayName("Capture idle timeout (ms)")]
    public int StreamIdleTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// File extension (no leading dot) for a converted output file. Unset falls back to a sensible
    /// default per mechanism (<c>svg</c> for the internal HP-GL converter, <c>png</c> for the other two).
    /// </summary>
    [Category("Stream Monitor")]
    [DisplayName("Converted output extension")]
    public string? StreamConvertOutputExtension { get; set; }

    /// <summary>
    /// External converter tools registered by name (docs/design/features/stream-converter-tools.md). Set in
    /// the profile JSON; <see cref="StreamConvertMode"/> <c>auto</c> picks the first whose formats match a
    /// capture, <c>tool:Name</c> runs one by name.
    /// </summary>
    [Browsable(false)]
    public List<StreamConvertToolOptions> StreamConvertTools { get; set; } = [];
}

/// <summary>One registered Stream Monitor converter tool - see <see cref="CliOptions.StreamConvertTools"/>.</summary>
public sealed class StreamConvertToolOptions
{
    /// <summary>Shown in the conversion list and used by <c>tool:Name</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The executable.</summary>
    public string? Path { get; set; }

    /// <summary>Argument template; <c>{input}</c>, <c>{output}</c>, <c>{dpi}</c> are substituted per token.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Comma-separated capture formats it accepts (<c>ps</c>, <c>pcl</c>, <c>hpgl</c>, <c>image</c> or an extension such as <c>bmp</c>); empty means any.</summary>
    public string Formats { get; set; } = string.Empty;

    /// <summary>Extension (no dot) of the file the tool writes.</summary>
    public string OutputExtension { get; set; } = "png";

    /// <summary>The value substituted for <c>{dpi}</c>.</summary>
    public int Dpi { get; set; } = 150;

    /// <summary>A copy, so an editor can change a tool without touching the options it was loaded from.</summary>
    public static StreamConvertToolOptions Clone(StreamConvertToolOptions tool) => new()
    {
        Name = tool.Name,
        Path = tool.Path,
        Arguments = tool.Arguments,
        Formats = tool.Formats,
        OutputExtension = tool.OutputExtension,
        Dpi = tool.Dpi,
    };
}
