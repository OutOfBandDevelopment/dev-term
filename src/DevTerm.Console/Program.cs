using DevTerm.Configuration;
using DevTerm.Console;
using DevTerm.Core.Control;
using DevTerm.Core.Plugins;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Transports.Ble;
using DevTerm.Transports.Hid;
using DevTerm.Transports.Serial;
using DevTerm.Transports.Usbtmc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// A faulted background Task whose exception nobody ever observed (no await, no .Result, no
// continuation checking it) used to crash the process when its finalizer ran - .NET no longer does
// that by default, but this still reports it instead of letting it vanish silently, and marks it
// observed so nothing downstream re-escalates it. Mirrors DevTerm.Wpf's App.xaml.cs handler; the
// TUI's own on-screen equivalent for a *synchronous* unhandled exception is TuiMode.RunAsync's
// Application.Run errorHandler, since there's no Dispatcher here to intercept those instead.
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    e.SetObserved();
    Console.Error.WriteLine($"A background operation failed and has been ignored so dev-term can keep running:\n\n{e.Exception}");
};

const string Usage =
    "Usage: dev-term --transport serial --port <name> [--baud <rate>] [--databits <5-8>] [--parity <name>] [--stopbits <name>] [--handshake <name>] [--dtr <bool>] [--rts <bool>] [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --transport tcp (--host <host> | --listen true) --port <port> [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --transport hid --vendorid <n> --productid <n> [--serialnumber <sn>] [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --transport usbtmc --vendorid <n> --productid <n> [--serialnumber <sn>] [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --transport ble --bledeviceid <id> [--bleserviceuuid <uuid>] [--blewritecharacteristicuuid <uuid>] [--blenotifycharacteristicuuid <uuid>] [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --transport rfc2217 --host <host> --port <port> [--baud <rate>] [--databits <5-8>] [--parity <name>] [--stopbits <name>] [--dtr <bool>] [--rts <bool>] [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --transport vxi11 --host <host> [--port <core-port>] [--presenter <name[,name...]>] [--lineending <None|Cr|Lf|CrLf>] [--cli <bool>]"
    + "\n   or: dev-term --transport amqp|stomp --host <host> --port <port> [--subscribe <key[,key...]>] [--publish <key>] [--username <name>] [--password <pw>]"
    + "\n   or: dev-term --transport mqtt--host <host> --port <port> [--subscribe <topic[,topic...]>] [--publish <topic>] [--username <name>] [--password <pw>] [--presenter <name[,name...]>] [--cli <bool>]"
    + "\n   or: dev-term --playback <log.jsonl> [--presenter <name[,name...]>] [--playbackspeed <rate, 0 = as fast as possible>]"
    + "\n   or: dev-term --listports true"
    + "\n   or: dev-term --listhiddevices true [--vendorid <n>] [--productid <n>]"
    + "\n   or: dev-term --listusbtmcdevices true [--vendorid <n>] [--productid <n>]"
    + "\n   or: dev-term --listbledevices true"
    + "\n   or: dev-term --listblecharacteristics <deviceid>"
    + "\nThe full-screen TUI is the default mode; pass --cli true for the plain scriptable loop instead"
    + "\n(e.g. for automation/CI), or --tui false, equivalently."
    + "\nAdd --log <file.jsonl> (or --log true for a timestamped file under ~/.dev-term/logs) to any"
    + "\nconnection to record everything sent and received."
    + "\nAdd --otlp <http://host:4317> (or --otlp true for localhost) to export dev-term's own traces and metrics over OTLP."
    + "\nAdd --theme <light|dark|system|name> to pick the TUI's colors for this run (View > Theme saves a choice;"
    + "\ncustom themes are JSON files under ~/.dev-term/themes)."
    + "\nSettings can also come from environment variables (DEVTERM_PORT, DEVTERM_BAUD, ...) or"
    + $"\nfrom an untracked '{DevTermConfiguration.LocalSettingsFileName}' next to the app, for a saved default profile."
    + "\nCommand-line arguments always win, then environment variables, then the settings file.";

// A plain command-line peek, ahead of the full host/config pipeline: listing ports/devices is a
// one-off action, not something that should go through the profile/env-var layering or transport
// validation (which would otherwise demand e.g. a --port that the user is trying to discover).
var earlyConfig = new ConfigurationBuilder().AddCommandLine(args).Build();

if (earlyConfig.GetValue<bool>(nameof(CliOptions.ListPorts)))
{
    foreach (var portName in new SystemSerialPortDiscovery().GetPortNames())
    {
        Console.WriteLine(portName);
    }

    return 0;
}

if (earlyConfig.GetValue<bool>(nameof(CliOptions.ListHidDevices)))
{
    // 0 (the default, from an omitted flag) means "any" — same convention the Connection Editor's
    // detected-devices picker already uses for filtering by these same two fields.
    var filterVendorId = earlyConfig.GetValue<int>(nameof(CliOptions.VendorId));
    var filterProductId = earlyConfig.GetValue<int>(nameof(CliOptions.ProductId));
    foreach (var device in new SystemHidDeviceDiscovery().GetDevices())
    {
        if ((filterVendorId != 0 && device.VendorId != filterVendorId) || (filterProductId != 0 && device.ProductId != filterProductId))
        {
            continue;
        }

        var serial = device.SerialNumber is null ? string.Empty : $"  SN:{device.SerialNumber}";
        Console.WriteLine($"{device.VendorId:X4}:{device.ProductId:X4}  {device.ProductName ?? "(unknown)"}{serial}");
    }

    return 0;
}

if (earlyConfig.GetValue<bool>(nameof(CliOptions.ListUsbtmcDevices)))
{
    var filterVendorId = earlyConfig.GetValue<int>(nameof(CliOptions.VendorId));
    var filterProductId = earlyConfig.GetValue<int>(nameof(CliOptions.ProductId));
    foreach (var device in new SystemUsbtmcDeviceDiscovery().GetDevices())
    {
        if ((filterVendorId != 0 && device.VendorId != filterVendorId) || (filterProductId != 0 && device.ProductId != filterProductId))
        {
            continue;
        }

        var serial = device.SerialNumber is null ? string.Empty : $"  SN:{device.SerialNumber}";
        var manufacturer = device.Manufacturer is null ? string.Empty : $"{device.Manufacturer} ";
        var location = device.DevicePath is null ? string.Empty : $"  at {device.DevicePath}";
        Console.WriteLine($"{device.VendorId:X4}:{device.ProductId:X4}  {manufacturer}{device.Product ?? "(unknown)"}{serial}{location}");
    }

    return 0;
}

if (earlyConfig.GetValue<int>(nameof(CliOptions.ListCaptures)) > 0 || earlyConfig.GetValue<int>(nameof(CliOptions.ExportCaptures)) > 0)
{
    using var captureMonitor = new StreamMonitor();
    captureMonitor.LoadFromDisk();
    var exportCount = earlyConfig.GetValue<int>(nameof(CliOptions.ExportCaptures));
    var chosen = CaptureExport.Newest(captureMonitor.Captures, exportCount > 0 ? exportCount : earlyConfig.GetValue<int>(nameof(CliOptions.ListCaptures)));
    if (exportCount > 0)
    {
        if (earlyConfig[nameof(CliOptions.ExportTo)] is not { Length: > 0 } exportTo)
        {
            Console.Error.WriteLine("--exportcaptures needs --exportto <folder>.");
            return 1;
        }

        var written = CaptureExport.CopyTo(chosen, exportTo);
        foreach (var path in written)
        {
            Console.WriteLine(path);
        }

        Console.Error.WriteLine($"Copied {written.Count} of {chosen.Count} capture(s) to {exportTo}.");
        return 0;
    }

    foreach (var capture in chosen)
    {
        Console.WriteLine($"{capture.LocalStartedAt:yyyy-MM-dd HH:mm:ss}  {capture.SavedPath}");
    }

    if (chosen.Count == 0)
    {
        Console.Error.WriteLine("No saved captures found.");
    }

    return 0;
}

if (earlyConfig.GetValue<bool>(nameof(CliOptions.ListLxiDevices)))
{
    foreach (var device in LxiDeviceScanner.Scan())
    {
        Console.WriteLine($"{device.Host}:{device.Port}  {device.Display}");
    }

    return 0;
}

if (earlyConfig[nameof(CliOptions.Attach)] is { Length: > 0 } attachName)
{
    using var attachStop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        attachStop.Cancel();
    };

    try
    {
        await foreach (var line in SessionPipeClient.ReadLinesAsync(attachName, cancellationToken: attachStop.Token))
        {
            Console.WriteLine(SessionPipeClient.Describe(line));
        }

        return 0;
    }
    catch (OperationCanceledException)
    {
        return 0;
    }
    catch (TimeoutException)
    {
        Console.Error.WriteLine($"No session named '{attachName}' is publishing. Start one with --pipe {attachName}.");
        return 1;
    }
}

if (earlyConfig.GetValue<bool>(nameof(CliOptions.ListPlugins)))
{
    var pluginOptions = new CliOptions { Plugins = earlyConfig[nameof(CliOptions.Plugins)] };
    var pluginServices = new ServiceCollection().AddPlugins(pluginOptions);
    var pluginResults = pluginServices.BuildServiceProvider().GetRequiredService<IReadOnlyList<PluginLoadResult>>();
    foreach (var line in PluginReport.Lines(pluginResults))
    {
        Console.WriteLine(line);
    }

    return 0;
}

if (earlyConfig.GetValue<bool>(nameof(CliOptions.ListBleDevices)))
{
    // Same reflection-based platform-adapter loading AddDevTermFrontEnd uses for a real connection
    // (see BlePlatformAdapterLoader's doc comment) - the real, Windows-backed discovery only exists
    // in a Windows-versioned TFM this plain net10.0 project can't reference directly.
    var bleServices = new ServiceCollection();
    bleServices.AddBleTransport();
    BlePlatformAdapterLoader.TryRegisterPlatformAdapter(bleServices);
    using var bleProvider = bleServices.BuildServiceProvider();
    foreach (var device in bleProvider.GetRequiredService<IBleDeviceDiscovery>().GetDevices())
    {
        Console.WriteLine($"{device.DeviceId}  {device.Name ?? "(unknown)"}");
    }

    return 0;
}

if (earlyConfig[nameof(CliOptions.ListBleCharacteristics)] is { Length: > 0 } bleDeviceId)
{
    var bleProfileServices = new ServiceCollection();
    bleProfileServices.AddBleTransport();
    BlePlatformAdapterLoader.TryRegisterPlatformAdapter(bleProfileServices);
    using var bleProfileProvider = bleProfileServices.BuildServiceProvider();
    foreach (var service in bleProfileProvider.GetRequiredService<IBleGattProfileExplorer>().GetServices(bleDeviceId))
    {
        Console.WriteLine($"Service {service.Uuid}");
        foreach (var characteristic in service.Characteristics)
        {
            var flags = string.Join(",", new[]
            {
                characteristic.CanRead ? "Read" : null,
                characteristic.CanWrite ? "Write" : null,
                characteristic.CanWriteWithoutResponse ? "WriteWithoutResponse" : null,
                characteristic.CanNotify ? "Notify" : null,
                characteristic.CanIndicate ? "Indicate" : null,
            }.Where(f => f is not null));
            Console.WriteLine($"  Characteristic {characteristic.Uuid}  [{flags}]{(characteristic.Name is null ? string.Empty : $"  {characteristic.Name}")}");
        }
    }

    return 0;
}

// Playback is a one-off action like the listings above: it replays a log file and never connects,
// so it skips profile layering and transport validation entirely (command-line flags only).
if (earlyConfig[nameof(CliOptions.Playback)] is { Length: > 0 })
{
    var playbackOptions = new CliOptions();
    DevTermConfiguration.Bind(earlyConfig, playbackOptions);
    return await PlaybackCliMode.RunAsync(playbackOptions, [.. playbackOptions.Presenter.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())]);
}

// Bind cliOptions from the same layered sources the host will use, but before building the host
// at all — building the host eagerly wires a transport from cliOptions (AddDevTermFrontEnd), so an
// invalid CliOptions has to be caught here, ahead of that, to decide whether to hard-fail (CLI) or
// show a Configure screen instead (TUI) — see docs/design/connection-profiles.md's startup flow.
var earlyConfigBuilder = new ConfigurationBuilder();
DevTermConfiguration.Configure(earlyConfigBuilder, args, Environments.Production);
var cliOptions = new CliOptions();
var layeredConfig = earlyConfigBuilder.Build();

// A bad value on the command line, in an environment variable, or in a corrupt/truncated saved
// profile (e.g. --baud fast) throws from inside the configuration binder before validation ever
// runs - see docs/bugs/resolved/032-startup-bind-failure-crash.md. Tui/Cli are read from the raw
// config rather than cliOptions, since a failed Bind may have left cliOptions only partially
// populated; cliOptions itself is reset to a clean default so it doesn't carry that partial state
// into ConfigureMode/the CLI error path below.
var useTui = (layeredConfig.GetValue<bool?>(nameof(CliOptions.Tui)) ?? true) && !(layeredConfig.GetValue<bool?>(nameof(CliOptions.Cli)) ?? false);

// The theme is an app preference, not part of the connection: --theme / DEVTERM_THEME for this run,
// else the saved View > Theme choice (~/.dev-term/preferences.json), else "system". Applied before
// any TUI screen (including the startup Connection Editor below) - Terminal.Gui's scheme overrides
// are process-wide and survive Application.Init. Problems are shown in the main window's output.
// Housekeeping: drop old logs/exports per the saved retention rules (keeps everything unless a rule is set).
RetentionSweeper.Sweep(new AppPreferencesStore().Load());

if (useTui)
{
    ActiveTheme.Initialize(layeredConfig);
    TuiTheme.ApplyActive();
}

string? bindError = null;
try
{
    DevTermConfiguration.Bind(layeredConfig, cliOptions);
}
catch (Exception ex) when (ex is InvalidOperationException or FormatException or InvalidDataException)
{
    // cliOptions is reset to a clean default so it doesn't carry whatever partial state a failed
    // Bind left behind into ConfigureMode/the CLI error path below - see
    // docs/bugs/resolved/032-startup-bind-failure-crash.md.
    cliOptions = new CliOptions();
    bindError = ex.Message;
}

if (bindError is null)
{
    var validation = new CliOptionsValidator().Validate(null, cliOptions);
    if (validation.Failed)
    {
        bindError = string.Join(" ", validation.Failures);
    }
}

if (bindError is not null)
{
    if (!useTui)
    {
        Console.Error.WriteLine(bindError);
        Console.Error.WriteLine(Usage);
        return 1;
    }

    var configured = ConfigureMode.Run(cliOptions, bindError);
    if (configured is null)
    {
        return 0;
    }

    cliOptions = configured;
}

// Out-of-process plugins run only once approved; prompt for them here, before the UI takes over the terminal
// (a script with redirected input is never prompted: only remembered approvals run there).
if (!System.Console.IsInputRedirected)
{
    DevTerm.Core.Plugins.PluginTrust.Approver = new ConsolePluginApprover();
}

var hostBuilder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) => DevTermConfiguration.Configure(context, config, args))
    .ConfigureServices((_, services) => services.AddDevTermFrontEnd(cliOptions));

var host = hostBuilder.Build();

using (host)
{
    foreach (var plugin in host.Services.GetRequiredService<IReadOnlyList<PluginLoadResult>>().Where(p => !p.Loaded))
    {
        Console.Error.WriteLine($"Plugin '{plugin.Name}' skipped: {plugin.Message}");
    }

    var catalog = host.Services.GetRequiredService<PresenterCatalog>();
    IReadOnlyList<IPresenter> presenters;
    try
    {
        presenters = DevTermSessionBuilder.ResolvePresenters(catalog, cliOptions);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    var transport = host.Services.GetRequiredService<ITransport>();
    var sessionFactory = host.Services.GetRequiredService<ISessionFactory>();
    await using var session = sessionFactory.Create(transport, new Pipeline(presenters));

    // --log: the CLI starts logging here, before connecting; the TUI starts it itself (its File
    // menu owns the logger so it can stop it).
    using var cliLogger = useTui ? null : CliLogging.Start(session, cliOptions, Console.Error);

    // TUI is the default mode; --cli true (or --tui false) forces the plain scriptable loop —
    // reuses the same useTui computed above (before any ConfigureMode run), since ConfigureMode's
    // output only carries connection fields, not the original Tui/Cli mode flags.
    return useTui
        ? await TuiMode.RunAsync(session, catalog, cliOptions, plugins: host.Services.GetService<IReadOnlyList<PluginLoadResult>>(), panels: [.. host.Services.GetServices<IDevicePanelContribution>().Where(c => !DevicePanels.BuiltInPanelIds.Contains(c.Id))])
        : await CliMode.RunAsync(session, catalog, cliOptions);
}
