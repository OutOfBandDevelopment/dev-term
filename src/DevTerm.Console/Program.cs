using DevTerm.Console;
using DevTerm.Core.Hosting;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Transports.Serial;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

CliArguments cliArguments;
try
{
    cliArguments = CliArguments.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(
        "Usage: dev-term --transport serial --port <name> [--baud <rate>] [--presenter <name>]"
        + "\n   or: dev-term --transport tcp (--host <host> | --listen) --tcp-port <port> [--presenter <name>]");
    return 1;
}

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddDevTermCore();
        services.AddTextPresenters();

        switch (cliArguments.Transport)
        {
            case TransportKind.Serial:
                services.AddSerialTransport();
                services.Configure<SerialTransportOptions>(o =>
                {
                    o.PortName = cliArguments.SerialPortName!;
                    o.BaudRate = cliArguments.SerialBaudRate;
                });
                break;
            case TransportKind.Tcp:
                services.AddTcpTransport();
                services.Configure<TcpTransportOptions>(o =>
                {
                    o.Mode = cliArguments.TcpListen ? TcpTransportMode.Listener : TcpTransportMode.Client;
                    o.Host = cliArguments.TcpHost;
                    o.Port = cliArguments.TcpPort;
                });
                break;
        }
    })
    .Build();

var catalog = host.Services.GetRequiredService<PresenterCatalog>();
if (!catalog.TryGet(cliArguments.PresenterName, out var presenter))
{
    Console.Error.WriteLine(
        $"Unknown presenter '{cliArguments.PresenterName}'. Available: {string.Join(", ", catalog.Names)}");
    return 1;
}

var transport = host.Services.GetRequiredService<ITransport>();
var sessionFactory = host.Services.GetRequiredService<ISessionFactory>();
await using var session = sessionFactory.Create(transport, new Pipeline([presenter]));

session.Output += (_, output) => Console.WriteLine($"[{output.PresenterName}] {output.Text}");

await session.OpenAsync();
var connectionDescription = cliArguments.Transport == TransportKind.Serial
    ? $"{cliArguments.SerialPortName} at {cliArguments.SerialBaudRate} baud"
    : cliArguments.TcpListen
        ? $"TCP listener on port {cliArguments.TcpPort}"
        : $"TCP {cliArguments.TcpHost}:{cliArguments.TcpPort}";
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
