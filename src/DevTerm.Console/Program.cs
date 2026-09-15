using DevTerm.Configuration;
using DevTerm.Console;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Transports.Serial;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

const string Usage =
    "Usage: dev-term --transport serial --port <name> [--baud <rate>] [--databits <5-8>] [--parity <name>] [--stopbits <name>] [--handshake <name>] [--dtr <bool>] [--rts <bool>] [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--tui <bool>]"
    + "\n   or: dev-term --transport tcp (--host <host> | --listen true) --tcpport <port> [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--tui <bool>]"
    + "\n   or: dev-term --listports true"
    + "\nSettings can also come from environment variables (DEVTERM_PORT, DEVTERM_BAUD, ...) or"
    + $"\nfrom an untracked '{DevTermConfiguration.LocalSettingsFileName}' next to the app, for a saved default profile."
    + "\nCommand-line arguments always win, then environment variables, then the settings file.";

// A plain command-line peek, ahead of the full host/config pipeline: listing ports is a one-off
// action, not something that should go through the profile/env-var layering or transport
// validation (which would otherwise demand a --port that the user is trying to discover).
if (new ConfigurationBuilder().AddCommandLine(args).Build().GetValue<bool>(nameof(CliOptions.ListPorts)))
{
    foreach (var portName in new SystemSerialPortDiscovery().GetPortNames())
    {
        Console.WriteLine(portName);
    }

    return 0;
}

var cliOptions = new CliOptions();

var hostBuilder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) => DevTermConfiguration.Configure(context, config, args))
    .ConfigureServices((context, services) =>
    {
        // Bind + validate the layered configuration (files, env vars, command line — see
        // DevTermConfiguration) here via the Options pattern instead of a hand-rolled parser.
        context.Configuration.Bind(cliOptions);

        services.AddOptions<CliOptions>().Bind(context.Configuration).ValidateOnStart();
        services.AddSingleton<IValidateOptions<CliOptions>, CliOptionsValidator>();

        var validation = new CliOptionsValidator().Validate(null, cliOptions);
        if (validation.Failed)
        {
            throw new OptionsValidationException(nameof(CliOptions), typeof(CliOptions), validation.Failures);
        }

        services.AddDevTermFrontEnd(cliOptions);
    });

IHost host;
try
{
    host = hostBuilder.Build();
}
catch (OptionsValidationException ex)
{
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine(failure);
    }

    Console.Error.WriteLine(Usage);
    return 1;
}

using (host)
{
    var catalog = host.Services.GetRequiredService<PresenterCatalog>();
    if (!catalog.TryGet(cliOptions.Presenter, out var presenter))
    {
        Console.Error.WriteLine(
            $"Unknown presenter '{cliOptions.Presenter}'. Available: {string.Join(", ", catalog.Names)}");
        return 1;
    }

    var transport = host.Services.GetRequiredService<ITransport>();
    var sessionFactory = host.Services.GetRequiredService<ISessionFactory>();
    await using var session = sessionFactory.Create(transport, new Pipeline([presenter]));

    return cliOptions.Tui
        ? await TuiMode.RunAsync(session, presenter, cliOptions)
        : await CliMode.RunAsync(session, presenter, cliOptions);
}
