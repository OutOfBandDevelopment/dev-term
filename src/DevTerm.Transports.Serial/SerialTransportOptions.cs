using System.ComponentModel.DataAnnotations;
using System.IO.Ports;

namespace DevTerm.Transports.Serial;

/// <summary>Configuration for a serial/UART session. Bound via the Options pattern.</summary>
public sealed class SerialTransportOptions
{
    [Required(AllowEmptyStrings = false)]
    public string PortName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int BaudRate { get; set; } = 9600;

    [Range(5, 8)]
    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>
    /// Bounds a blocked write (e.g. hardware flow control enabled but the device never asserts
    /// CTS) so it fails with a <see cref="TimeoutException"/> instead of hanging forever. In
    /// milliseconds; use <see cref="System.IO.Ports.SerialPort.InfiniteTimeout"/> (-1) to wait
    /// indefinitely.
    /// </summary>
    public int WriteTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Milliseconds a read blocks before timing out. Deliberately finite by default (unlike
    /// write): <see cref="System.IO.Ports.SerialPort"/>'s <c>BaseStream.ReadAsync</c> does not
    /// reliably honor a <see cref="CancellationToken"/> to interrupt an in-flight read on some
    /// drivers, so <see cref="DevTerm.Core.Transports.StreamToPipePump"/> relies on periodic
    /// read timeouts (treated as "no data yet", not an error) to notice cancellation promptly —
    /// an infinite timeout here would make Close/Ctrl+C hang until the next byte arrives.
    /// </summary>
    public int ReadTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Whether to assert DTR (Data Terminal Ready) on open. Defaults to <c>true</c> — many
    /// devices (bench instruments, modems) treat DTR as a "terminal is present" signal and won't
    /// respond until it's asserted. <see cref="System.IO.Ports.SerialPort"/> defaults this to
    /// <c>false</c> unlike most terminal emulators (and pyserial), which is an easy way to end up
    /// "connected" but never hearing from the device.
    /// </summary>
    public bool DtrEnable { get; set; } = true;

    /// <summary>
    /// Whether to assert RTS (Request To Send) on open, for devices that check it as a readiness
    /// signal even without hardware flow control. Ignored (and left to automatic flow-control
    /// management) when <see cref="Handshake"/> is <see cref="System.IO.Ports.Handshake.RequestToSend"/>
    /// or <see cref="System.IO.Ports.Handshake.RequestToSendXOnXOff"/>.
    /// </summary>
    public bool RtsEnable { get; set; } = true;
}
