using DevTerm.Configuration;
using DevTerm.Console;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Transports.Hid;
using DevTerm.Transports.Serial;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

const string Usage =
    "Usage: dev-term --transport serial --port <name> [--baud <rate>] [--databits <5-8>] [--parity <name>] [--stopbits <name>] [--handshake <name>] [--dtr <bool>] [--rts <bool>] [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --transport tcp (--host <host> | --listen true) --tcpport <port> [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --transport hid --hidvendorid <n> --hidproductid <n> [--hidserialnumber <sn>] [--presenter <name[,name...]>] [--parser <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]"
    + "\n   or: dev-term --listports true"
    + "\n   or: dev-term --listhiddevices true"
    + "\nThe full-screen TUI is the default mode; pass --cli true for the plain scriptable loop instead"
    + "\n(e.g. for automation/CI), or --tui false, equivalently."
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
    foreach (var device in new SystemHidDeviceDiscovery().GetDevices())
    {
        var serial = device.SerialNumber is null ? string.Empty : $"  SN:{device.SerialNumber}";
        Console.WriteLine($"{device.VendorId:X4}:{device.ProductId:X4}  {device.ProductName ?? "(unknown)"}{serial}");
    }

    return 0;
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

    // TUI is the default mode; --cli true (or --tui false) forces the plain scriptable loop —
    // reuses the same useTui computed above (before any ConfigureMode run), since ConfigureMode's
    // output only carries connection fields, not the original Tui/Cli mode flags.
    return useTui
        ? await TuiMode.RunAsync(session, catalog, cliOptions)
        : await CliMode.RunAsync(session, catalog, cliOptions);
}
