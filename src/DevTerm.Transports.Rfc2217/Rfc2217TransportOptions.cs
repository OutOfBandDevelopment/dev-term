using System.ComponentModel.DataAnnotations;
using System.IO.Ports;

namespace DevTerm.Transports.Rfc2217;

/// <summary>
/// Configuration for an RFC 2217 (Telnet COM Port Control) client session — a serial port reached
/// over the network, e.g. via <c>ser2net</c>. Bound via the Options pattern. Mirrors
/// <c>SerialTransportOptions</c>'s field set (it's "a serial port"), plus <see cref="Host"/>/
/// <see cref="Port"/> (it's "reached over TCP").
/// </summary>
public sealed class Rfc2217TransportOptions
{
    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; }

    [Range(1, int.MaxValue)]
    public int BaudRate { get; set; } = 9600;

    [Range(5, 8)]
    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>Whether to assert DTR on the remote port once negotiation succeeds. Defaults to <c>true</c> — see <c>SerialTransportOptions.DtrEnable</c>'s reasoning.</summary>
    public bool DtrEnable { get; set; } = true;

    /// <summary>Whether to assert RTS on the remote port once negotiation succeeds. Defaults to <c>true</c> — see <c>SerialTransportOptions.RtsEnable</c>'s reasoning.</summary>
    public bool RtsEnable { get; set; } = true;

    /// <summary>
    /// Bounds a blocked write so it fails with a <see cref="TimeoutException"/> instead of hanging
    /// forever. In milliseconds.
    /// </summary>
    public int WriteTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Milliseconds to wait for the server's reply to the opening <c>WILL/DO COM-PORT-OPTION</c>
    /// offer before falling back to plain data passthrough (see <see cref="Rfc2217Transport"/>'s
    /// <c>OpenAsync</c>). Code-only — no CLI/UI surface in v1; a generous default rarely needs
    /// tuning before real-server feedback exists to justify exposing it.
    /// </summary>
    public int NegotiationTimeoutMs { get; set; } = 3000;

    /// <summary>
    /// Milliseconds paced between each byte written, or -1 (the default) to disable pacing and
    /// write the buffer as a single, unpaced call. See <see cref="DevTerm.Core.Transports.WriteDelayStream"/>.
    /// </summary>
    public int WriteByteDelayMs { get; set; } = -1;
}
