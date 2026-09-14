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
}
