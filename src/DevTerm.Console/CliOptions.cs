namespace DevTerm.Console;

/// <summary>
/// The CLI mode's settings, bound from <c>Microsoft.Extensions.Configuration.CommandLine</c>
/// (command-line arguments) via the Options pattern rather than a hand-rolled parser. Property
/// names double as the (case-insensitive) argument names: <c>--transport tcp --tcpport 502</c>.
/// </summary>
public sealed class CliOptions
{
    public string Transport { get; set; } = "serial";

    public string Presenter { get; set; } = "hex";

    // Serial transport.
    public string? Port { get; set; }

    public int Baud { get; set; } = 9600;

    // TCP transport.
    public string? Host { get; set; }

    public int TcpPort { get; set; }

    public bool Listen { get; set; }
}
