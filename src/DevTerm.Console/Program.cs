using System.IO.Ports;
using System.Net.Sockets;
using DevTerm.Console;
using DevTerm.Core.Hosting;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Transports.Serial;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

const string Usage =
    "Usage: dev-term --transport serial --port <name> [--baud <rate>] [--databits <5-8>] [--parity <name>] [--stopbits <name>] [--handshake <name>] [--dtr <bool>] [--rts <bool>] [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>]"
    + "\n   or: dev-term --transport tcp (--host <host> | --listen true) --tcpport <port> [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>]"
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

        services.AddDevTermCore();
        services.AddTextPresenters();
        services.Configure<AsciiPresenterOptions>(o => o.MaxLineLength = cliOptions.AsciiMaxLineLength);

        if (string.Equals(cliOptions.Transport, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddTcpTransport();
            services.Configure<TcpTransportOptions>(o =>
            {
                o.Mode = cliOptions.Listen ? TcpTransportMode.Listener : TcpTransportMode.Client;
                o.Host = cliOptions.Host;
                o.Port = cliOptions.TcpPort;
            });
        }
        else
        {
            services.AddSerialTransport();
            services.Configure<SerialTransportOptions>(o =>
            {
                o.PortName = cliOptions.Port!;
                o.BaudRate = cliOptions.Baud;
                o.DataBits = cliOptions.DataBits;
                o.Parity = cliOptions.Parity;
                o.StopBits = cliOptions.StopBits;
                o.Handshake = cliOptions.Handshake;
                o.WriteTimeoutMs = cliOptions.WriteTimeoutMs;
                o.ReadTimeoutMs = cliOptions.ReadTimeoutMs;
                o.DtrEnable = cliOptions.Dtr;
                o.RtsEnable = cliOptions.Rts;
            });
        }
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

    session.Output += (_, output) => Console.WriteLine($"[{output.PresenterName}] {output.Text}");

    try
    {
        await session.OpenAsync();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or SocketException)
    {
        Console.Error.WriteLine(ConnectionErrorMessages.For(cliOptions.Transport, ex));
        return 1;
    }

    var connectionDescription = string.Equals(cliOptions.Transport, "tcp", StringComparison.OrdinalIgnoreCase)
        ? cliOptions.Listen
            ? $"TCP listener on port {cliOptions.TcpPort}"
            : $"TCP {cliOptions.Host}:{cliOptions.TcpPort}"
        : $"{cliOptions.Port} at {cliOptions.Baud} baud ({cliOptions.DataBits}{cliOptions.Parity.ToString()[0]}{(cliOptions.StopBits == StopBits.One ? 1 : cliOptions.StopBits == StopBits.Two ? 2 : 1.5)})";
    Console.WriteLine($"Connected to {connectionDescription} using '{presenter.Name}'.");
    Console.WriteLine("Type a line and press Enter to send; Ctrl+C to exit.");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    while (true)
    {
        string? line;
        try
        {
            // A plain Console.ReadLine() blocks on the OS read and ignores cts entirely, so
            // Ctrl+C would set the flag but never unblock the loop; ReadLineAsync(CancellationToken)
            // actually interrupts a pending interactive console read.
            line = await Console.In.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        if (line is null)
        {
            break;
        }

        if (presenter is IPresenterInput input)
        {
            try
            {
                await session.SendAsync(cliOptions.LineEnding.Append(input.Parse(line)));
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine(
                    "Send timed out — no response to hardware flow control (CTS)? Check the device or --handshake.");
            }
        }
        else
        {
            Console.Error.WriteLine($"Presenter '{presenter.Name}' does not support sending.");
        }
    }

    await session.CloseAsync();
    return 0;
}
