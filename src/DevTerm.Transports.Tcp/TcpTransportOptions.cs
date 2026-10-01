namespace DevTerm.Transports.Tcp;

public enum TcpTransportMode
{
    /// <summary>Connect out to a remote host:port.</summary>
    Client,

    /// <summary>Bind a local port and accept one inbound connection.</summary>
    Listener,
}

/// <summary>Configuration for a TCP session, in either direction. See docs/design/transports.md.</summary>
public sealed class TcpTransportOptions
{
    public TcpTransportMode Mode { get; set; } = TcpTransportMode.Client;

    /// <summary>Required in <see cref="TcpTransportMode.Client"/> mode. Ignored (bind-any) if unset in Listener mode.</summary>
    public string? Host { get; set; }

    /// <summary>The remote port to connect to (Client mode) or the local port to bind (Listener mode).</summary>
    public int Port { get; set; }

    /// <summary>
    /// Milliseconds a single <see cref="TcpTransport.WriteAsync"/> call may block before it's abandoned with a
    /// <see cref="TimeoutException"/> - a peer that stops reading (a full TCP send window) would otherwise block
    /// the caller (often the UI thread, via Session.SendAsync) forever. See
    /// docs/bugs/fixed/029-tcp-write-blocks-no-timeout.md.
    /// </summary>
    public int WriteTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Opt-in software flow control (XON/XOFF) - off by default, since stripping 0x11/0x13 from a
    /// stream that isn't actually using them for flow control would silently eat legitimate device
    /// data. Some serial-to-Ethernet bridges forward the attached serial port's XON/XOFF bytes over
    /// the wire rather than honoring them locally; enabling this strips those bytes out of the
    /// decoded output and pauses/resumes dev-term's own writes on them instead. Only the initial
    /// value for a new <see cref="TcpTransport"/> - once open, <see cref="TcpTransport.SoftwareFlowControl"/>
    /// is the live, settable switch (a front end can toggle it without reconnecting). See
    /// docs/design/transports.md.
    /// </summary>
    public bool SoftwareFlowControl { get; set; }

    /// <summary>
    /// Milliseconds paced between each byte written, or -1 (the default) to disable pacing and
    /// write the buffer as a single, unpaced call. See <see cref="DevTerm.Core.Transports.WriteDelayStream"/>.
    /// </summary>
    public int WriteByteDelayMs { get; set; } = -1;
}
