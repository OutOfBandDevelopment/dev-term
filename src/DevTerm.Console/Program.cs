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
    "Usage: dev-term --transport serial --port <name> [--baud <rate>] [--presenter <name>]"
    + "\n   or: dev-term --transport tcp (--host <host> | --listen true) --tcpport <port> [--presenter <name>]";

var cliOptions = new CliOptions();

var hostBuilder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Host.CreateDefaultBuilder already layers command-line args into IConfiguration
        // (highest precedence); bind + validate them here via the Options pattern instead of a
        // hand-rolled parser.
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

    await session.OpenAsync();
    var connectionDescription = string.Equals(cliOptions.Transport, "tcp", StringComparison.OrdinalIgnoreCase)
        ? cliOptions.Listen
            ? $"TCP listener on port {cliOptions.TcpPort}"
            : $"TCP {cliOptions.Host}:{cliOptions.TcpPort}"
        : $"{cliOptions.Port} at {cliOptions.Baud} baud";
    Console.WriteLine($"Connected to {connectionDescription} using '{presenter.Name}'.");
    Console.WriteLine("Type a line and press Enter to send; Ctrl+C to exit.");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    while (!cts.IsCancellationRequested)
    {
        var line = Console.ReadLine();
        if (line is null)
        {
            break;
        }

        if (presenter is IPresenterInput input)
        {
            await session.SendAsync(input.Parse(line));
        }
        else
        {
            Console.Error.WriteLine($"Presenter '{presenter.Name}' does not support sending.");
        }
    }

    await session.CloseAsync();
    return 0;
}
