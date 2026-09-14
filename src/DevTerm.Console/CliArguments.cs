namespace DevTerm.Console;

/// <summary>
/// Parses the CLI mode's command-line arguments. Kept as a plain, side-effect-free parser
/// (no <c>System.Console</c> or process exit calls) so it can be unit tested directly.
/// </summary>
public sealed record CliArguments(string PortName, int BaudRate, string PresenterName)
{
    public const string DefaultPresenterName = "hex";
    public const int DefaultBaudRate = 9600;

    public static CliArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? portName = null;
        var baudRate = DefaultBaudRate;
        var presenterName = DefaultPresenterName;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port":
                    portName = RequireValue(args, ref i, "--port");
                    break;
                case "--baud":
                    var baudText = RequireValue(args, ref i, "--baud");
                    if (!int.TryParse(baudText, out baudRate))
                    {
                        throw new ArgumentException($"'--baud' expects an integer, got '{baudText}'.");
                    }

                    break;
                case "--presenter":
                    presenterName = RequireValue(args, ref i, "--presenter");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (portName is null)
        {
            throw new ArgumentException("Missing required '--port' argument.");
        }

        return new CliArguments(portName, baudRate, presenterName);
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"'{optionName}' requires a value.");
        }

        return args[++index];
    }
}
