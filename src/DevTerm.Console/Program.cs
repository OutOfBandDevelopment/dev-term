using DevTerm.Configuration;
using DevTerm.Console;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
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
    + "\n   or: dev-term --playback <log.jsonl> [--presenter <name[,name...]>] [--playbackspeed <rate, 0 = as fast as possible>]"
    + "\n   or: dev-term --listports true"
    + "\n   or: dev-term --listhiddevices true [--vendorid <n>] [--productid <n>]"
    + "\n   or: dev-term --listusbtmcdevices true [--vendorid <n>] [--productid <n>]"
    + "\nThe full-screen TUI is the default mode; pass --cli true for the plain scriptable loop instead"
    + "\n(e.g. for automation/CI), or --tui false, equivalently."
    + "\nAdd --log <file.jsonl> (or --log true for a timestamped file under ~/.dev-term/logs) to any"
    + "\nconnection to record everything sent and received."
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
DevTermConfiguration.Bind(earlyConfigBuilder.Build(), cliOptions);

var useTui = cliOptions.Tui && !cliOptions.Cli;
var validation = new CliOptionsValidator().Validate(null, cliOptions);
if (validation.Failed)
{
    if (!useTui)
    {
        foreach (var failure in validation.Failures)
        {
            Console.Error.WriteLine(failure);
        }

        Console.Error.WriteLine(Usage);
        return 1;
    }

    var configured = ConfigureMode.Run(cliOptions, string.Join(" ", validation.Failures));
    if (configured is null)
    {
        return 0;
    }

    cliOptions = configured;
}

var hostBuilder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) => DevTermConfiguration.Configure(context, config, args))
    .ConfigureServices((_, services) => services.AddDevTermFrontEnd(cliOptions));

var host = hostBuilder.Build();

using (host)
{
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
        ? await TuiMode.RunAsync(session, catalog, cliOptions)
        : await CliMode.RunAsync(session, catalog, cliOptions);
}
