using System.IO.Ports;

namespace DevTerm.Console;

/// <summary>
/// The CLI mode's settings, bound from configuration (command-line args, environment
/// variables, and JSON settings files layered via the standard
/// <c>Microsoft.Extensions.Configuration</c> extensions — see <see cref="DevTermConfiguration"/>)
/// via the Options pattern rather than a hand-rolled parser. Property names double as the
/// (case-insensitive) argument/setting names: <c>--transport tcp --tcpport 502</c>.
/// </summary>
public sealed class CliOptions
{
    public string Transport { get; set; } = "serial";

    public string Presenter { get; set; } = "hex";

    // Serial transport.
    public string? Port { get; set; }

    public int Baud { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>Flow control; hardware (RTS/CTS) flow control is off by default like most serial APIs — set to <see cref="Handshake.RequestToSend"/> to enable it.</summary>
    public Handshake Handshake { get; set; } = Handshake.None;

    // TCP transport.
    public string? Host { get; set; }

    public int TcpPort { get; set; }

    public bool Listen { get; set; }
}
