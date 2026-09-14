namespace DevTerm.Console;

public enum TransportKind
{
    Serial,
    Tcp,
}

/// <summary>
/// Parses the CLI mode's command-line arguments. Kept as a plain, side-effect-free parser
/// (no <c>System.Console</c> or process exit calls) so it can be unit tested directly.
/// </summary>
public sealed record CliArguments(
    TransportKind Transport,
    string PresenterName,
    string? SerialPortName,
    int SerialBaudRate,
    string? TcpHost,
    int TcpPort,
    bool TcpListen)
{
    public const string DefaultPresenterName = "hex";
    public const int DefaultBaudRate = 9600;

    public static CliArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var transport = TransportKind.Serial;
        var presenterName = DefaultPresenterName;
        string? serialPortName = null;
        var serialBaudRate = DefaultBaudRate;
        string? tcpHost = null;
        var tcpPort = 0;
        var tcpListen = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--transport":
                    transport = ParseTransportKind(RequireValue(args, ref i, "--transport"));
                    break;
                case "--presenter":
                    presenterName = RequireValue(args, ref i, "--presenter");
                    break;
                case "--port":
                    serialPortName = RequireValue(args, ref i, "--port");
                    break;
                case "--baud":
                    serialBaudRate = RequireInt(args, ref i, "--baud");
                    break;
                case "--host":
                    tcpHost = RequireValue(args, ref i, "--host");
                    break;
                case "--tcp-port":
                    tcpPort = RequireInt(args, ref i, "--tcp-port");
                    break;
                case "--listen":
                    tcpListen = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        switch (transport)
        {
            case TransportKind.Serial when serialPortName is null:
                throw new ArgumentException("Missing required '--port' argument for the serial transport.");
            case TransportKind.Tcp when tcpPort == 0:
                throw new ArgumentException("Missing required '--tcp-port' argument for the TCP transport.");
            case TransportKind.Tcp when !tcpListen && tcpHost is null:
                throw new ArgumentException("The TCP transport requires '--host' unless '--listen' is set.");
        }

        return new CliArguments(transport, presenterName, serialPortName, serialBaudRate, tcpHost, tcpPort, tcpListen);
    }

    private static TransportKind ParseTransportKind(string value) => value.ToLowerInvariant() switch
    {
        "serial" => TransportKind.Serial,
        "tcp" => TransportKind.Tcp,
        _ => throw new ArgumentException($"Unknown transport '{value}'. Expected 'serial' or 'tcp'."),
    };

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"'{optionName}' requires a value.");
        }

        return args[++index];
    }

    private static int RequireInt(string[] args, ref int index, string optionName)
    {
        var text = RequireValue(args, ref index, optionName);
        if (!int.TryParse(text, out var value))
        {
            throw new ArgumentException($"'{optionName}' expects an integer, got '{text}'.");
        }

        return value;
    }
}
