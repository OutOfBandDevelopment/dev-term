using System.IO.Ports;

namespace DevTerm.Configuration;

/// <summary>
/// A front end's connection settings, bound from configuration (command-line args, environment
/// variables, and JSON settings files layered via the standard
/// <c>Microsoft.Extensions.Configuration</c> extensions — see <see cref="DevTermConfiguration"/>)
/// via the Options pattern rather than a hand-rolled parser. Property names double as the
/// (case-insensitive) argument/setting names: <c>--transport tcp --tcpport 502</c>. Shared by
/// every front end (console CLI/TUI, WPF) so the same saved profile works from any of them.
/// </summary>
public sealed class CliOptions
{
    public string Transport { get; set; } = "serial";

    /// <summary>List available serial ports and exit, skipping normal validation/connection entirely.</summary>
    public bool ListPorts { get; set; }

    /// <summary>Run the console app's full-screen TUI instead of the plain scriptable CLI loop. See docs/design/frontends.md.</summary>
    public bool Tui { get; set; }

    public string Presenter { get; set; } = "hex";

    /// <summary>Appended to each typed line before sending, for presenters that support sending. See <see cref="LineEnding"/>.</summary>
    public LineEnding LineEnding { get; set; } = LineEnding.None;

    /// <summary>See <see cref="DevTerm.Presenters.Text.AsciiPresenterOptions.MaxLineLength"/>. 0 means unbounded (wait for a line terminator only).</summary>
    public int AsciiMaxLineLength { get; set; } = DevTerm.Presenters.Text.AsciiPresenter.DefaultMaxLineLength;

    // Serial transport.
    public string? Port { get; set; }

    public int Baud { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>Flow control; hardware (RTS/CTS) flow control is off by default like most serial APIs — set to <see cref="Handshake.RequestToSend"/> to enable it.</summary>
    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>Bounds a blocked write (e.g. hardware flow control on but the device never asserts CTS) instead of hanging forever; -1 waits indefinitely.</summary>
    public int WriteTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Milliseconds a read blocks before timing out. Kept finite by default: SerialPort's
    /// BaseStream doesn't reliably honor cancellation on an in-flight read on all drivers, so a
    /// periodic timeout is how Close/Ctrl+C notice they should stop instead of hanging.
    /// </summary>
    public int ReadTimeoutMs { get; set; } = 1000;

    /// <summary>Assert DTR on open; many devices treat it as a "terminal present" signal and stay silent without it. See <see cref="SerialTransportOptions.DtrEnable"/>.</summary>
    public bool Dtr { get; set; } = true;

    /// <summary>Assert RTS on open (ignored when <see cref="Handshake"/> already manages RTS). See <see cref="SerialTransportOptions.RtsEnable"/>.</summary>
    public bool Rts { get; set; } = true;

    // TCP transport.
    public string? Host { get; set; }

    public int TcpPort { get; set; }

    public bool Listen { get; set; }
}
